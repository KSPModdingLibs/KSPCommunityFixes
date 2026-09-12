using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes;

/// <summary>
/// Correct subdivision angle wrapping and bound recursive searches in the modern
/// closest-approach solver. The legacy targeting setting is left unchanged.
/// </summary>
internal class ModernTargeting : BasePatch
{
    protected override Version VersionMin => new Version(1, 12, 5);
    protected override Version VersionMax => new Version(1, 12, 5);

    private const string HarmonyId = "KSPCommunityFixes.ModernTargeting";
    // A numerical search should converge long before this many nested subdivisions.
    // This independent backstop also bounds paths that keep shrinking without resolving.
    private const int MaxSearchDepth = 64;

    private struct SearchContext
    {
        public int Depth;
        public double Width;
    }

    private struct CallState
    {
        public bool Entered;
        public SearchContext Previous;
    }

    protected override void ApplyPatches()
    {
        // Use a separate owner so a failed installation can roll back this entire
        // feature without unpatching unrelated Community Fixes or other mods.
        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.CreateClassProcessor(typeof(RootSearch)).Patch();
            harmony.CreateClassProcessor(typeof(CrossingSearch)).Patch();
            harmony.CreateClassProcessor(typeof(SubdivisionWrap)).Patch();
        }
        catch
        {
            harmony.UnpatchAll(HarmonyId);
            throw;
        }
    }

    private static bool TryEnter(Targeting.Interval interval, ref SearchContext context, out CallState state)
    {
        state = default;
        double start = interval.s1.v;
        double end = interval.s2.v;
        // Match FindRoot's wrap handling: adjust the endpoint before subtraction.
        if (end < start) end += Math.PI * 2;
        double width = end - start;

        if (double.IsNaN(width) || double.IsInfinity(width) || width < 0 ||
            context.Depth >= MaxSearchDepth ||
            (context.Depth > 0 && !(width < context.Width)))
            return false;

        state.Previous = context;
        state.Entered = true;
        context = new SearchContext { Depth = context.Depth + 1, Width = width };
        return true;
    }

    private static void Exit(ref SearchContext context, ref CallState state)
    {
        // Another prefix may have skipped ours. A rejected call also never entered.
        // Clearing the flag makes cleanup idempotent if another finalizer throws.
        if (!state.Entered) return;
        context = state.Previous;
        state.Entered = false;
    }

    [HarmonyPatch(typeof(Targeting.Interval), nameof(Targeting.Interval.FindRoot))]
    private static class RootSearch
    {
        [ThreadStatic] private static SearchContext context;

        [HarmonyPrefix]
        private static bool Prefix(Targeting.Interval __instance, ref Targeting.Sample __result, out CallState __state)
        {
            if (TryEnter(__instance, ref context, out __state)) return true;
            // Stock accepts null and performs its usual interval cleanup.
            __result = null;
            return false;
        }

        [HarmonyFinalizer]
        private static void Finalizer(ref CallState __state) => Exit(ref context, ref __state);
    }

    [HarmonyPatch(typeof(Targeting), "add_crossing_subdivisions")]
    private static class CrossingSearch
    {
        [ThreadStatic] private static SearchContext context;

        [HarmonyPrefix]
        private static bool Prefix(List<Targeting.Interval> intervals, Targeting.Interval ival, out CallState __state)
        {
            if (TryEnter(ival, ref context, out __state)) return true;
            // Preserve the leaf and its ownership in stock traversal order. The later
            // root-search prefix also rejects invalid top-level intervals, so retaining
            // an invalid leaf here cannot bypass the numerical input check.
            intervals.Add(ival);
            return false;
        }

        [HarmonyFinalizer]
        private static void Finalizer(ref CallState __state) => Exit(ref context, ref __state);
    }

    [HarmonyPatch(typeof(Targeting.Interval), nameof(Targeting.Interval.Subdivide))]
    private static class SubdivisionWrap
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var result = new List<CodeInstruction>(codes.Count);
            int count = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                var code = new CodeInstruction(codes[i]);
                // The two internal angles must wrap by a full turn. A prefix/postfix
                // cannot repair this intermediate arithmetic without reimplementing
                // subdivision. Keep instruction positions, labels and block boundaries.
                if (code.opcode == OpCodes.Ldc_R8 && code.operand is double value && value == Math.PI &&
                    i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Sub)
                {
                    code.operand = Math.PI * 2;
                    count++;
                }
                result.Add(code);
            }
            if (count != 2)
                throw new InvalidOperationException($"ModernTargeting: expected two subdivision wrap subtractions, found {count}.");
            return result;
        }
    }
}
