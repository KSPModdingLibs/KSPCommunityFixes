using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KSPCommunityFixes.BugFixes
{
    // https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/185

    class EVAConstructionMass : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EVAConstructionModeEditor), nameof(EVAConstructionModeEditor.PickupPart)),
                this));
        }

        // the stock code sets selectedpart.mass to the prefab mass, which breaks terribly in cases where there are mass modifiers involved
        // I suspect this code exists at all because ModuleCargoPart.MakePartSettle alters the part's prefabMass to make it harder to move the part around when it's been dropped on the ground
        // To fix this, we restore the *prefabMass* field from the prefab, and then call UpdateMass so that IPartMassModifiers can do their thing.
        static IEnumerable<CodeInstruction> EVAConstructionModeEditor_PickupPart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo partMassField = AccessTools.Field(typeof(Part), nameof(Part.mass));
            FieldInfo partPrefabMassField = AccessTools.Field(typeof(Part), nameof(Part.prefabMass));
            FieldInfo EditorLogicBase_selectedPart = AccessTools.Field(typeof(EditorLogicBase), nameof(EditorLogicBase.selectedPart));
            MethodInfo Part_UpdateMass = AccessTools.Method(typeof(Part), nameof(Part.UpdateMass));
            foreach (var instruction in instructions)
            {
                if (instruction.StoresField(partMassField))
                {
                    instruction.operand = partPrefabMassField;
                    yield return instruction;

                    // call Part.UpdateMass
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, EditorLogicBase_selectedPart);
                    yield return new CodeInstruction(OpCodes.Call, Part_UpdateMass);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }
}
