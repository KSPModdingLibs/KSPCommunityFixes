using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

// fix issue #169 : IVA doesn’t work properly for parts with ModuleAnimateGeneric that modify crew capacity
namespace KSPCommunityFixes.BugFixes
{
    internal class ModuleAnimateGenericCrewModSpawnIVA : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(ModuleAnimateGeneric), nameof(ModuleAnimateGeneric.CheckCrewState));
        }

        // Insert a call to our static OnCrewCapacityChanged() method in the "if (crewCapacity != base.part.CrewCapacity)" condition
        static IEnumerable<CodeInstruction> ModuleAnimateGeneric_CheckCrewState_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_RefreshPartContextWindow = AccessTools.Method(typeof(MonoUtilities), nameof(MonoUtilities.RefreshPartContextWindow));
            MethodInfo m_PartModule_GetPart = AccessTools.PropertyGetter(typeof(PartModule), nameof(PartModule.part));
            FieldInfo f_Part_CrewCapacity = AccessTools.Field(typeof(Part), nameof(Part.CrewCapacity));
            MethodInfo m_OnCrewCapacityChanged = AccessTools.Method(typeof(ModuleAnimateGenericCrewModSpawnIVA), nameof(OnCrewCapacityChanged));

            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Call && ReferenceEquals(code.operand, m_RefreshPartContextWindow))
                {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, m_PartModule_GetPart);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, m_PartModule_GetPart);
                    yield return new CodeInstruction(OpCodes.Ldfld, f_Part_CrewCapacity);
                    yield return new CodeInstruction(OpCodes.Call, m_OnCrewCapacityChanged);
                }
                else
                {
                    yield return code;
                }
            }
        }

        static void OnCrewCapacityChanged(Part part, int newCrewCapacity)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (newCrewCapacity > 0)
                part.SpawnIVA();
            else
                part.DespawnIVA();
        }
    }
}
