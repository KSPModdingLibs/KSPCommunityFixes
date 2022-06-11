using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes
{
    class TweakableWheelsAutostrut : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleWheelBase), nameof(ModuleWheelBase.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModuleWheelBase), nameof(ModuleWheelBase.OnStart)),
                this));

            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
        }

        // set heaviest part autostrut option by default on all parts implementing ModuleWheelBase
        private void OnPartLoaderLoaded()
        {
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                if (availablePart.partPrefab.HasModuleImplementing<ModuleWheelBase>())
                {
                    availablePart.partPrefab.autoStrutMode = Part.AutoStrutMode.Heaviest;
                }
            }
        }

        // unlock autostrut option on existing crafts/vessels
        static void ModuleWheelBase_OnStart_Prefix(ModuleWheelBase __instance)
        {
            if (__instance.part.autoStrutMode == Part.AutoStrutMode.ForceHeaviest)
            {
                __instance.part.autoStrutMode = Part.AutoStrutMode.Heaviest;
            }
        }

        // remove the hardcoded autostrut affection to ForceHeaviest
        static IEnumerable<CodeInstruction> ModuleWheelBase_OnStart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // base.part.autoStrutMode = Part.AutoStrutMode.ForceHeaviest;
            //IL_007f: ldarg.0
            //IL_0080: call instance class Part PartModule::get_part()
            //possible obfuscation dup/pop
            //IL_0085: ldc.i4.5
            //IL_0086: stfld valuetype Part/AutoStrutMode Part::autoStrutMode

            FieldInfo Part_autoStrutMode = AccessTools.Field(typeof(Part), nameof(Part.autoStrutMode));
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 1; i < code.Count - 4; i++)
            {
                if (code[i - 1].opcode == OpCodes.Ldc_I4_5 
                    && code[i].opcode == OpCodes.Stfld
                    && ReferenceEquals(code[i].operand, Part_autoStrutMode))
                {
                    for (int j = i; j >= i - 10; j--)
                    {
                        OpCode opcode = code[j].opcode;
                        code[j].opcode = OpCodes.Nop;
                        code[j].operand = null;

                        if (opcode == OpCodes.Ldarg_0)
                            break;
                    }
                }
            }

            return code;
        }
    }
}
