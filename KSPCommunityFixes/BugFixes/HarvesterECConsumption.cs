// ModuleResourceHarvester.GetInfo() multiplies the harvester's INPUT_RESOURCE
// rates by Efficiency before displaying them. However, ModuleResourceHarvester
// itself does not actually multiply input resources by Efficiency when figuring
// out how much to consume, so that figures displayed in the editor are wrong.
//
// This patch overrides GetInfo so that it returns the correct values.
//
// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/358

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes;

internal class HarvesterECConsumption : BasePatch
{
    protected override Version VersionMin => new(1, 8, 0);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Transpiler, typeof(ModuleResourceHarvester), nameof(ModuleResourceHarvester.GetInfo));
    }

    static IEnumerable<CodeInstruction> ModuleResourceHarvester_GetInfo_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var efficiency = AccessTools.Field(typeof(BaseDrill), nameof(BaseDrill.Efficiency));
        var matcher = new CodeMatcher(instructions);

        // End fence: the "Outputs:" header. Efficiency uses at or after this point belong
        // to the output loop and must be left scaled.
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Ldstr, "#autoLOC_259698"))
            .ThrowIfInvalid("Unable to find the outputs section header (#autoLOC_259698)");
        int outputsHeader = matcher.Pos;

        // Start fence: the "Inputs:" header. This skips the `Efficiency * 100f` use in the
        // part header, which has a different shape and isn't part of the inputs loop.
        matcher
            .Start()
            .MatchStartForward(new CodeMatch(OpCodes.Ldstr, "#autoLOC_259676"))
            .ThrowIfInvalid("Unable to find the inputs section header (#autoLOC_259676)");

        int replaced = 0;
        while (true)
        {
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldfld, efficiency),
                new CodeMatch(OpCodes.Conv_R8),
                new CodeMatch(OpCodes.Mul));

            if (!matcher.IsValid || matcher.Pos >= outputsHeader)
                break;

            // Replace this.Efficiency with 1.0f.
            // ldarg.0 -> nop, ldfld Efficiency -> ldc.r4 1 (conv.r8 then makes it 1.0).
            matcher
                .SetAndAdvance(OpCodes.Nop, null)
                .SetAndAdvance(OpCodes.Ldc_R4, 1f);
            replaced++;
        }

        if (replaced != 5)
            throw new Exception($"[HarvesterECConsumption] Expected 5 Efficiency factors in the inputs loop, found {replaced}");

        return matcher.Instructions();
    }
}
