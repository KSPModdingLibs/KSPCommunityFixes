using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // The editor acts on a left click when the button is released. Input.mousePosition in the release
    // frame includes cursor movement made after the button was physically released, up to a full frame
    // of it. It can then miss the part or placement that was clicked on.
    // On a release frame, give EditorLogic the cursor position of the previous frame if the button was
    // down then, which is the latest position Unity had while the button was down.
    internal class EditorQuickClickPosition : BasePatch
    {
        private static int heldFrame = -1;
        private static Vector3 heldPosition;

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(Mouse), "Update");

            MethodInfo m_Input_get_mousePosition = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mousePosition));

            // EditorLogic reads the cursor position in its own methods and in FSM lambdas, some of
            // which are compiled into nested closure classes.
            List<Type> types = new List<Type> { typeof(EditorLogic) };
            types.AddRange(typeof(EditorLogic).GetNestedTypes(AccessTools.all));

            foreach (Type type in types)
            {
                foreach (MethodInfo method in type.GetMethods(AccessTools.allDeclared))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;

                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                    {
                        if (instruction.Calls(m_Input_get_mousePosition))
                        {
                            AddPatch(PatchType.Transpiler, method, nameof(EditorLogic_MousePosition_Transpiler));
                            break;
                        }
                    }
                }
            }
        }

        static void Mouse_Update_Postfix()
        {
            if (Input.GetMouseButton(0))
            {
                heldFrame = Time.frameCount;
                heldPosition = Input.mousePosition;
            }
        }

        static IEnumerable<CodeInstruction> EditorLogic_MousePosition_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_Input_get_mousePosition = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mousePosition));
            MethodInfo m_ClickMousePosition = AccessTools.Method(typeof(EditorQuickClickPosition), nameof(ClickMousePosition));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(m_Input_get_mousePosition))
                    instruction.operand = m_ClickMousePosition;

                yield return instruction;
            }
        }

        static Vector3 ClickMousePosition()
        {
            if (Time.frameCount == heldFrame + 1 && Input.GetMouseButtonUp(0))
                return heldPosition;

            return Input.mousePosition;
        }
    }
}
