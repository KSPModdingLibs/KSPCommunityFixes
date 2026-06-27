using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    // The "distance travelled" / "distance over ground" values shown in the F3 flight results dialog are
    // accumulated twice :
    //
    // - FlightLogger.FixedUpdate() integrates srfSpeed / horizontalSrfSpeed over the physics timestep
    //   (speed * TimeWarp.fixedDeltaTime). This is the correct Riemann sum, done once per physics tick.
    //
    // - FlightLogger.LateUpdate() does the *same* integration a second time, which is wrong on two counts :
    //     1. It double counts : the distance is already integrated correctly in FixedUpdate().
    //     2. LateUpdate() runs once per rendered frame, but still multiplies by TimeWarp.fixedDeltaTime
    //        (the physics timestep) rather than the frame time (Time.deltaTime). The extra contribution is
    //        therefore srfSpeed * 0.02 * (frames this second), which is physically meaningless and scales
    //        with framerate (~+100% at 50 fps, more at higher framerates).
    //
    // The net result is a wildly inflated, framerate-dependent distance readout. We fix it by dropping the
    // duplicate integration from LateUpdate(), leaving FixedUpdate() as the single, correct integrator.

    internal class FlightLoggerInflatedDistance : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(FlightLogger), nameof(FlightLogger.LateUpdate));
        }

        static IEnumerable<CodeInstruction> FlightLogger_LateUpdate_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo totalDistance = AccessTools.Field(typeof(FlightLogger), nameof(FlightLogger.totalDistance));
            FieldInfo groundDistance = AccessTools.Field(typeof(FlightLogger), nameof(FlightLogger.groundDistance));

            foreach (CodeInstruction instruction in instructions)
            {
                // Neutralize the "totalDistance += ..." / "groundDistance += ..." compound assignments by
                // replacing their field store with two pops : one for the value being stored, one for the
                // FlightLogger instance reference. The (side effect free) speed * dt computation that
                // precedes the store is left in place, which keeps the IL stack balanced and doesn't disturb
                // any branch targets pointing at the start of those statements.
                if (instruction.StoresField(totalDistance) || instruction.StoresField(groundDistance))
                {
                    yield return new CodeInstruction(OpCodes.Pop).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Pop);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }
}
