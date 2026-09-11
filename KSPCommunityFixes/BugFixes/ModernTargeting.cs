using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes;

/// <summary>
/// Correct subdivision angle wrapping and bound recursive searches in the modern
/// closest-approach solver. The legacy targeting setting is left unchanged.
/// </summary>
internal class ModernTargeting : BasePatch
{
    protected override Version VersionMin => new Version(1, 12, 5);
    protected override Version VersionMax => new Version(1, 12, 5);

    private const int MaxRecursiveCalls = 64;
    private static readonly MethodInfo findRoot = AccessTools.Method(typeof(Targeting.Interval), "FindRoot");
    private static readonly MethodInfo crossings = AccessTools.Method(typeof(Targeting), "add_crossing_subdivisions");
    private static readonly MethodInfo subdivide = AccessTools.Method(typeof(Targeting.Interval), "Subdivide");
    private static readonly MethodInfo guardedRoot = AccessTools.Method(typeof(ModernTargeting), nameof(FindRootGuarded));
    private static readonly MethodInfo guardedCrossings = AccessTools.Method(typeof(ModernTargeting), nameof(AddCrossingsGuarded));
    private static readonly Action<List<Targeting.Interval>, Targeting.Interval, bool> addCrossings =
        (Action<List<Targeting.Interval>, Targeting.Interval, bool>)Delegate.CreateDelegate(
            typeof(Action<List<Targeting.Interval>, Targeting.Interval, bool>), crossings);

    [ThreadStatic] private static int rootDepth;
    [ThreadStatic] private static int crossingDepth;
    private static int stoppedRoots;
    private static int stoppedCrossings;

    protected override void ApplyPatches()
    {
        // Check all three patterns before registering any patch, including other mods'
        // current transpilers. In particular, don't apply over the standalone TargetingGuard.
        Interval_FindRoot_Transpiler(PatchProcessor.GetCurrentInstructions(findRoot));
        Targeting_add_crossing_subdivisions_Transpiler(PatchProcessor.GetCurrentInstructions(crossings));
        Interval_Subdivide_Transpiler(PatchProcessor.GetCurrentInstructions(subdivide));

        AddPatch(PatchType.Transpiler, findRoot);
        AddPatch(PatchType.Transpiler, crossings);
        AddPatch(PatchType.Transpiler, subdivide);
    }

    private static IEnumerable<CodeInstruction> Interval_FindRoot_Transpiler(IEnumerable<CodeInstruction> instructions)
        => GuardRecursiveCalls(instructions, findRoot, guardedRoot, OpCodes.Ldarg_0);

    private static IEnumerable<CodeInstruction> Targeting_add_crossing_subdivisions_Transpiler(IEnumerable<CodeInstruction> instructions)
        => GuardRecursiveCalls(instructions, crossings, guardedCrossings, OpCodes.Ldarg_1);

    private static List<CodeInstruction> GuardRecursiveCalls(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, MethodInfo guarded, OpCode loadParent)
    {
        var codes = new List<CodeInstruction>(instructions);
        int count = 0;
        foreach (CodeInstruction code in codes)
            if (code.Calls(original)) count++;
        if (count != 2)
            throw new InvalidOperationException($"ModernTargeting: expected two recursive calls in {original.Name}, found {count}.");

        var result = new List<CodeInstruction>(codes.Count + 2);
        foreach (CodeInstruction code in codes)
        {
            if (!code.Calls(original))
            {
                result.Add(code);
                continue;
            }

            // Append the parent to the original call arguments. Branches and exception
            // boundaries must remain at the beginning of the replacement sequence.
            var parent = new CodeInstruction(loadParent);
            parent.labels.AddRange(code.labels);
            parent.blocks.AddRange(code.blocks);
            result.Add(parent);
            result.Add(new CodeInstruction(OpCodes.Call, guarded));
        }
        return result;
    }

    private static Targeting.Sample FindRootGuarded(Targeting.Interval child, Targeting.Interval parent)
    {
        string reason = RejectReason(parent, child, rootDepth);
        if (reason != null)
        {
            // Stock accepts a null root and still releases the child interval on return.
            // Do not return an invalid sample or fabricate an encounter for this branch.
            if (Interlocked.Increment(ref stoppedRoots) <= 3)
                Debug.LogWarning($"[KSPCommunityFixes] ModernTargeting: stopped root search ({reason}).");
            return null;
        }

        rootDepth++;
        try { return child.FindRoot(); }
        finally { rootDepth--; }
    }

    private static void AddCrossingsGuarded(List<Targeting.Interval> output, Targeting.Interval child,
        bool reversed, Targeting.Interval parent)
    {
        string reason = RejectReason(parent, child, crossingDepth);
        if (reason != null)
        {
            // Keep the leaf in stock traversal order for the following root-search stage.
            // Its ownership stays with the output; the caller releases its temporary list.
            output.Add(child);
            if (Interlocked.Increment(ref stoppedCrossings) <= 3)
                Debug.LogWarning($"[KSPCommunityFixes] ModernTargeting: stopped crossing subdivision ({reason}).");
            return;
        }

        crossingDepth++;
        try { addCrossings(output, child, reversed); }
        finally { crossingDepth--; }
    }

    private static string RejectReason(Targeting.Interval parent, Targeting.Interval child, int depth)
    {
        if (depth >= MaxRecursiveCalls) return "recursion limit";
        double parentWidth = Width(parent.s1.v, parent.s2.v);
        double childWidth = Width(child.s1.v, child.s2.v);
        if (double.IsNaN(parentWidth) || double.IsInfinity(parentWidth) || parentWidth < 0 ||
            double.IsNaN(childWidth) || double.IsInfinity(childWidth) || childWidth < 0)
            return "non-finite or invalid interval";
        if (!(childWidth < parentWidth)) return "interval did not shrink";
        return null;
    }

    private static double Width(double start, double end)
    {
        // Match FindRoot's wrap handling: adjust the endpoint before subtraction.
        if (end < start) end += Math.PI * 2;
        return end - start;
    }

    private static IEnumerable<CodeInstruction> Interval_Subdivide_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var result = new List<CodeInstruction>(codes.Count);
        int count = 0;
        for (int i = 0; i < codes.Count; i++)
        {
            var code = new CodeInstruction(codes[i]);
            // Stock wraps an interior anomaly past +pi by subtracting pi, placing it
            // on the opposite side of the orbit. Subtract a full turn (2*pi) instead.
            // Leave the +pi comparison thresholds and all other arithmetic unchanged.
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
