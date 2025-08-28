using HarmonyLib;
using KSP.UI.Screens.DebugToolbar;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine.EventSystems;

namespace KSPCommunityFixes.BugFixes
{
    internal class DebugConsoleDontStealInput : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(DebugScreenConsole), nameof(DebugScreenConsole.OnEnable), nameof(ScrollDownTranspiler));
            AddPatch(PatchType.Transpiler, typeof(DebugScreenConsole), nameof(DebugScreenConsole.SubmitCommand), nameof(ScrollDownTranspiler));
            AddPatch(PatchType.Transpiler, typeof(DebugScreenConsole), nameof(DebugScreenConsole.OnMemoryLogUpdated), nameof(ScrollDownTranspiler));
        }

        private static IEnumerable<CodeInstruction> ScrollDownTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo mScrollDownOriginal = AccessTools.Method(typeof(DebugScreenConsole), nameof(DebugScreenConsole.ScrollDown));
            MethodInfo mScrollDownPatched = AccessTools.Method(typeof(DebugConsoleDontStealInput), nameof(ScrollDownPatched));

            foreach (CodeInstruction il in instructions)
            {
                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, mScrollDownOriginal))
                    il.operand = mScrollDownPatched;

                yield return il;
            }
        }

        private static IEnumerator ScrollDownPatched(DebugScreenConsole console)
        {
            yield return null;

            if (console.scrollRect != null)
                console.scrollRect.verticalNormalizedPosition = 0f;

            EventSystem.current.SetSelectedGameObject(console.inputField.gameObject, null);
            //console.inputField.OnPointerClick(new PointerEventData(EventSystem.current));
        }
    }
}
