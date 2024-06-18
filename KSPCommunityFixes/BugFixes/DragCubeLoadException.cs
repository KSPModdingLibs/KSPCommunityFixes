using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes
{
class DragCubeLoadException : BasePatch
{
    // The name field in a DRAG_CUBE config node is supposed to be optional. However, when the name is not included in
    // the list of values, an IndexOutOfRangeException is thrown.
    //
    // Even when not loaded from a config file, when the name field on a DragCube object is the empty string (for
    // example, when it is default-constructed in code) it is not included in the string returned by
    // DragCube.SaveToString(); this causes a problem when the FlightIntegrator.Setup() method uses this string to
    // create a clone of a drag cube.

    protected override Version VersionMin => new Version(1, 8, 0);

    protected override void ApplyPatches(List<PatchInfo> patches)
    {
        patches.Add(
            new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(DragCube), nameof(DragCube.Load)),
                this));
    }

    static IEnumerable<CodeInstruction> DragCube_Load_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // The first instructions in DragCube.Load(string[] data) are:
        //  - Check if it has the correct length (24 or 25), logging an error and returning if this is not the case
        //  - Check if the length is 12; if it is not, take the first field as the name and skip it.
        // That last check should have been against a length of 24 instead; this patch replaces the first occurrence of
        // ldc.i4.s 12 in the code by ldc.i4.s 24.

        bool found = false;
        foreach (var instruction in instructions)
        {
            if (!found && instruction.Is(OpCodes.Ldc_I4_S, (sbyte)12))
            {
                found = true;
                instruction.operand = (sbyte)24;
            }
            yield return instruction;
        }
    }
}
}
