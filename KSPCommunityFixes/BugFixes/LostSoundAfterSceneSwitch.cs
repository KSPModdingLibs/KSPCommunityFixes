using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    class LostSoundAfterSceneSwitch : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(FlightCamera), nameof(FlightCamera.EnableCamera)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(FlightCamera), nameof(FlightCamera.DisableCamera), new[] {typeof(bool)}),
                this));
        }

        static IEnumerable<CodeInstruction> FlightCamera_EnableCamera_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setParentOriginal = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] {typeof(Transform)});
            MethodInfo setParentReplacement = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, setParentOriginal))
                {
                    code[i].operand = setParentReplacement;
                    code.Insert(i, new CodeInstruction(OpCodes.Ldc_I4_0));
                }
            }


            return code;
        }

        static IEnumerable<CodeInstruction> FlightCamera_DisableCamera_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setParentOriginal = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform) });
            MethodInfo setParentReplacement = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, setParentOriginal))
                {
                    code[i].operand = setParentReplacement;
                    code.Insert(i, new CodeInstruction(OpCodes.Ldc_I4_0));
                }
            }

            return code;
        }
    }
}
