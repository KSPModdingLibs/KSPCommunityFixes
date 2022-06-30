using Expansions.Serenity;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

// fix scaled servo parts propagating their scale to childrens after actuating the servo in the editor
// see issue #48 : https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/48

namespace KSPCommunityFixes.BugFixes
{
    class RescaledRoboticParts : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.SetChildParentTransform)),
                this));
        }

        private static IEnumerable<CodeInstruction> BaseServo_SetChildParentTransform_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo mInfo_TransformSetParent = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new Type[] { typeof(Transform) });
            MethodInfo mInfo_TransformLocalScale = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localScale));
            FieldInfo fInfo_BaseServoMovingPartObject = AccessTools.Field(typeof(BaseServo), nameof(BaseServo.movingPartObject));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldfld && ReferenceEquals(code[i].operand, fInfo_BaseServoMovingPartObject))
                {
                    for (int j = i + 1; j < i + 6; j++)
                    {
                        if (code[j].opcode == OpCodes.Callvirt && ReferenceEquals(code[j].operand, mInfo_TransformSetParent))
                        {
                            int k = j;
                            bool end = false;
                            do
                            {
                                k++;
                                end = code[k].opcode == OpCodes.Callvirt && ReferenceEquals(code[k].operand, mInfo_TransformLocalScale);
                                code[k].opcode = OpCodes.Nop;
                                code[k].operand = null;
                            }
                            while (!end);

                            i = k;
                            break;
                        }
                    }
                }
            }

            return code;
        }
    }
}
