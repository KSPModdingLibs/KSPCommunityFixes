using System;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // Mouse.MouseButton.dragDelta is the distance the cursor moved since the button was pressed, and
    // WasDragging() compares it against a threshold. It is only updated on frames where the button is
    // held, and so holds stale info for later short clicks. In the editor, every such click is then
    // ignored by part pick, place and gizmo selection, until a longer click updates the value.
    internal class StaleMouseDragDelta : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(Mouse.MouseButton), nameof(Mouse.MouseButton.Update));
        }

        static void MouseButton_Update_Postfix(bool ___down, ref Vector2 ___dragDelta)
        {
            if (___down)
                ___dragDelta = Vector2.zero;
        }
    }
}
