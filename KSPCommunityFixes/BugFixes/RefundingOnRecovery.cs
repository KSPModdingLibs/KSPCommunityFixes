using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes
{
    // Fix overview :

    // Since the introduction of inventories in KSP 1.11, cost modifiers from the PartModule IPartCostModifier interface are ignored on vessel recovery.
    // This seems to be an oversight in the Funding.onVesselRecoveryProcessing() callback when Squad refactored it for inventories support.
    // Corresponding KSP bugtracker entry : https://bugs.kerbalspaceprogram.com/issues/26988

    // Technical overview :

    // In Funding.onVesselRecoveryProcessing(), we search for the following call, and change "includeModuleCosts" to true :

    // ShipConstruction.GetPartCosts(protoPartSnapshot, includeModuleCosts: false, availablePart, out var dryCost, out var fuelCost);
    //IL_0066: ldloc.2
    //IL_0067: ldc.i4.0
    //IL_0068: ldloc.s 6
    //IL_006a: ldloca.s 7
    //IL_006c: ldloca.s 8
    //IL_006e: call float32 ShipConstruction::GetPartCosts(class ProtoPartSnapshot, bool, class AvailablePart, float32&, float32&)
    //IL_0073: pop

    // What this changes is add the ProtoPartSnapshot.moduleCosts value to the returned dryCost param.
    // ProtoPartSnapshot.moduleCosts is the sum of all IPartCostModifier.GetModuleCost() results on the part,
    // and is populated when the ProtoPartSnapshot is saved/created (typically when a vessel is unloaded)
    // In the case of ModuleInventoryPart, the ProtoPartSnapshot.moduleCosts value include the full cost of
    // all stored parts (including their own moduleCosts).
    // Since Funding.onVesselRecoveryProcessing() iterates on all the protovessel protoparts + the in-inventory
    // protoparts (calls ProtoVessel.GetAllProtoPartsIncludingCargo()), by setting the includeModuleCosts to true,
    // we would have the in-inventory parts costs counted twice.
    // Setting includeModuleCosts to false for parts having a ModuleInventoryPart isn't really an option either,
    // as any other IPartCostModifier on the part would be ignored.

    // The solution is to parse all stored parts in every ModuleInventoryPart ProtoModule,
    // get the sum of their cost, and substract it to the dryCost returned by ShipConstruction:GetPartCosts()
    // This is done from a custom method GetStoredPartsModuleCosts(), which is derived from the ModuleInventoryPart.OnLoad() code.
    // We append that substraction and method call just after the "pop" OpCode following the original ShipConstruction:GetPartCosts() call.

    // dryCost -= GetStoredPartsModuleCosts(protoPartSnapshot);
    // IL_00eb: ldloc.s 7
    // IL_00ed: ldloc.2
    // IL_00ee: call float32 KSPCommunityFixes.BugFixes.Funding_onVesselRecoveryProcessing::GetStoredPartsModuleCosts(class ['Assembly-CSharp'] ProtoPartSnapshot)
    // IL_00f3: sub
    // IL_00f4: stloc.s 7

    class RefundingOnRecovery : BasePatch
    {
        private static readonly List<string> moduleInventoryPartDerivatives = new List<string>();

        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(Funding), "onVesselRecoveryProcessing"),
                this));

            moduleInventoryPartDerivatives.Clear();
            moduleInventoryPartDerivatives.Add(nameof(ModuleInventoryPart));
            foreach (Type type in AssemblyLoader.GetSubclassesOfParentClass(typeof(ModuleInventoryPart)))
            {
                moduleInventoryPartDerivatives.Add(type.Name);
            }
        }

        static IEnumerable<CodeInstruction> Funding_onVesselRecoveryProcessing_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            // public static float GetPartCosts(ProtoPartSnapshot protoPart, bool includeModuleCosts, AvailablePart aP, out float dryCost, out float fuelCost)
            MethodInfo getPartCostsMethod = AccessTools.Method(typeof(ShipConstruction), "GetPartCosts", new[] { typeof(ProtoPartSnapshot), typeof(bool), typeof(AvailablePart), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });

            if (getPartCostsMethod == null)
            {
                UnityEngine.Debug.LogError("Error patching recovery costs : GetPartCosts method not found");
                return instructions;
            }


            int insertionIndex = -1;
            object dryCostVarOperand = default;
            OpCode protoPartSnapshotOpcode = default;

            for (int i = 4; i < code.Count - 1; i++) 
            {
                if (code[i].opcode == OpCodes.Call && (MethodInfo)code[i].operand == getPartCostsMethod)
                {
                    // change includeModuleCosts from false to true
                    code[i - 4].opcode = OpCodes.Ldc_I4_1;
                    // Insert our method call after "pop"
                    insertionIndex = i + 1; 
                    // find the variables we need
                    dryCostVarOperand = code[i - 2].operand;
                    protoPartSnapshotOpcode = code[i - 5].opcode;
                    break;
                }
            }

            if (insertionIndex == -1)
            {
                UnityEngine.Debug.LogError("Error patching recovery costs : transpiler patch failed");
                return instructions;
            }

            List<CodeInstruction> instructionsToInsert = new List<CodeInstruction>();
            MethodInfo getStoredPartsModuleCosts = AccessTools.Method(typeof(RefundingOnRecovery), nameof(GetStoredPartsCosts));

            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldloc_S, dryCostVarOperand));
            instructionsToInsert.Add(new CodeInstruction(protoPartSnapshotOpcode));
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Call, getStoredPartsModuleCosts));
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Sub));
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Stloc_S, dryCostVarOperand));

            code.InsertRange(insertionIndex, instructionsToInsert);

            return code;
        }


        // Derived from the ModuleInventoryPart.OnLoad() code, get stored parts cost
        static float GetStoredPartsCosts(ProtoPartSnapshot protoPart)
        {
            ConstructorInfo storedPartCtor = AccessTools.Constructor(typeof(StoredPart), new[] {typeof(ConfigNode)});

            float cost = 0f;
            foreach (ProtoPartModuleSnapshot protoModule in protoPart.modules)
            {
                if (!moduleInventoryPartDerivatives.Contains(protoModule.moduleName))
                    continue;

                ConfigNode moduleNode = protoModule.moduleValues;

                List<StoredPart> storedParts = new List<StoredPart>();

                ConfigNode storedPartsNode = null;
                if (moduleNode.TryGetNode("STOREDPARTS", ref storedPartsNode))
                {
                    ConfigNode[] storedPartsNodes = storedPartsNode.GetNodes("STOREDPART");
                    if (storedPartsNodes.Length != 0)
                    {
                        for (int i = 0; i < storedPartsNodes.Length; i++)
                        {
                            storedParts.Add((StoredPart)storedPartCtor.Invoke(new object[] { storedPartsNodes[i] }));
                        }
                    }
                    else
                    {
                        storedPartsNodes = storedPartsNode.GetNodes("PART");
                        ConfigNode stackAmountsNode = null;
                        bool flag = moduleNode.TryGetNode("STACKAMOUNTS", ref stackAmountsNode);
                        for (int j = 0; j < storedPartsNodes.Length; j++)
                        {
                            ConfigNode node4 = storedPartsNodes[j];

                            ProtoPartSnapshot protoPartSnapshot = new ProtoPartSnapshot(node4, null, null);
                            StoredPart storedPart = new StoredPart(protoPartSnapshot.partName, j);
                            storedPart.snapshot = protoPartSnapshot;


                            if (flag && j < stackAmountsNode.nodes.Count)
                            {
                                stackAmountsNode.nodes[j].TryGetValue("amount", ref storedPart.quantity);
                            }
                            else
                            {
                                storedPart.quantity = 1;
                            }
                            storedParts.Add(storedPart);
                        }
                    }
                }

                foreach (StoredPart storedPart in storedParts)
                {
                    cost += ShipConstruction.GetPartCosts(storedPart.snapshot, true, storedPart.snapshot.partInfo, out _, out _) * storedPart.quantity;
                }
            }

            return cost;
        }

    }

    public class ModulePartCostTest : PartModule, IPartCostModifier
    {
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            return 1000f;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.CONSTANTLY;
    }

    public class ModuleInventoryPartDerivativeTest : ModuleInventoryPart
    {
    }
}
