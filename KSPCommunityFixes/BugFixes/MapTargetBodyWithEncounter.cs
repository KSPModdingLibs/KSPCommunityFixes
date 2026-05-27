// Fix for issue #381 : left-clicking or double-clicking a celestial body's icon in the map view
// does nothing when a maneuver node produces an encounter with that body, making it impossible
// to set the encounter body as target.
//
// When the player clicks a body's map icon, OrbitTargeter.TargetCastNodes() decides which orbit
// was clicked. It deliberately ignores any orbit whose body is equal to refPatch.referenceBody,
// the intent being to prevent targeting the body whose SOI the active vessel currently is in.
// However, when a maneuver node exists, OrbitTargeter.ReferencePatchSelect() sets refPatch to the
// *last* patch of the flight plan, and the referenceBody of that patch is the encounter body. That
// body then gets wrongly excluded by TargetCastNodes(), so its map icon becomes unclickable. Note
// that clicking the body's orbit line still works, because that path (TargetCastSplines()) has no
// such exclusion.
//
// We patch it here to remove this part of the check entirely. If the TargetParentBody patch is
// disabled then DropInvalidTargets will clear it on the next frame. Otherwise, there really
// isn't a reason to restrict what you can focus on here.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes;

class MapTargetBodyWithEncounter : BasePatch
{
    protected override Version VersionMin => new(1, 12, 0);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Transpiler, typeof(OrbitTargeter), nameof(OrbitTargeter.TargetCastNodes));
    }

    static IEnumerable<CodeInstruction> OrbitTargeter_TargetCastNodes_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo OrbitTargeter_refPatch = AccessTools.Field(typeof(OrbitTargeter), nameof(OrbitTargeter.refPatch));

        CodeMatcher matcher = new(instructions);

        // We want to patch this check
        //
        //   if (!(orbitDriver.celestialBody == refPatch.referenceBody))
        //       continue;
        //
        // to never run. The IL for the "orbitDriver.celestialBody == refPatch.referenceBody"
        // expression is:
        //
        //   ldloc.2
        //   ldfld class CelestialBody OrbitDriver::celestialBody
        //   ldarg.0
        //   ldfld class Orbit OrbitTargeter::refPatch
        //   ldfld class CelestialBody Orbit::referenceBody
        //   call bool UnityEngine.Object::op_Equality(class UnityEngine.Object, class UnityEngine.Object)
        //   // some obfuscation junk
        //   brtrue
        //
        // We delete the whole expression (ldloc.2 through the op_Equality call) and replace it with a
        // single ldc.i4.0, so the comparison is always false and the following brtrue is never taken:
        //
        //   ldc.i4.0
        //   // some obfuscation junk
        //   brtrue
        //
        // The first instruction of the expression (ldloc.2) is the target of a branch, so its
        // labels have to be carried over to the replacement ldc.i4.0, otherwise that branch
        // would point at a deleted instruction and break method generation.
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Ldfld, OrbitTargeter_refPatch))
            .ThrowIfNotMatch($"Could not find 'ldfld {nameof(OrbitTargeter.refPatch)}' in {nameof(OrbitTargeter.TargetCastNodes)}")
            .Advance(-3); // back up from "ldfld refPatch" onto the "ldloc.2" that starts the expression

        List<Label> labels = matcher.Instruction.labels;

        matcher
            .RemoveInstructions(6)
            .Insert(new CodeInstruction(OpCodes.Ldc_I4_0) { labels = labels });

        return matcher.InstructionEnumeration();
    }
}
