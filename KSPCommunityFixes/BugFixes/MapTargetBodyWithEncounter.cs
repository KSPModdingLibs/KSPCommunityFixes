using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes;

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
// The fix transpiles TargetCastNodes() so the comparison uses the active vessel's actual current
// main body instead of refPatch.referenceBody, leaving only the body the vessel is really orbiting
// excluded.
class MapTargetBodyWithEncounter : BasePatch
{
    protected override Version VersionMin => new Version(1, 12, 0);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Transpiler, typeof(OrbitTargeter), nameof(OrbitTargeter.TargetCastNodes));
    }

    static IEnumerable<CodeInstruction> OrbitTargeter_TargetCastNodes_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo OrbitTargeter_refPatch = AccessTools.Field(typeof(OrbitTargeter), nameof(OrbitTargeter.refPatch));
        MethodInfo currentReferenceOrbit = SymbolExtensions.GetMethodInfo(() => CurrentReferenceOrbit(null));

        CodeMatcher matcher = new(instructions);

        // Replace "this.refPatch" with "CurrentReferenceOrbit(this)". The preceding ldarg.0 has already
        // pushed the OrbitTargeter instance, which the call consumes and replaces with the orbit to read
        // referenceBody from, so the following "ldfld Orbit.referenceBody" still works.
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Ldfld, OrbitTargeter_refPatch))
            .ThrowIfNotMatch($"Could not find 'ldfld {nameof(OrbitTargeter.refPatch)}' in {nameof(OrbitTargeter.TargetCastNodes)}")
            .Set(OpCodes.Call, currentReferenceOrbit);

        return matcher.InstructionEnumeration();
    }

    // The orbit whose referenceBody is the body the active vessel is currently within the SOI of,
    // i.e. the only body that should be excluded from icon-click targeting.
    static Orbit CurrentReferenceOrbit(OrbitTargeter targeter)
    {
        return targeter.pcr.vessel.orbit;
    }
}
