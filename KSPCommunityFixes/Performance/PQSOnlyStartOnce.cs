
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.Performance
{
    class PQSOnlyStartOnce : BasePatch
    {

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(FlightDriver), nameof(FlightDriver.setStartupNewVessel));
        }
        

        static IEnumerable<CodeInstruction> FlightDriver_setStartupNewVessel_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
        {
            var method1 = SymbolExtensions.GetMethodInfo<PSystemSetup>(p => p.SetPQSActive(null));
            var method2 = SymbolExtensions.GetMethodInfo<PSystemSetup>(p => p.SetPQSActive());

            var matcher = new CodeMatcher(instructions);
            var state = gen.DeclareLocal(typeof(State));

            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, method1))
                .Repeat(
                    matcher =>
                    {
                        matcher.RemoveInstruction();
                        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldloca, state));
                        matcher.InsertAndAdvance(CodeInstruction.Call<State>(s => SetPQSActive(null, null, ref s)));
                    }
                );

            matcher.Start();
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, method2))
                .Repeat(
                    matcher =>
                    {
                        matcher.RemoveInstruction();
                        matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldloca, state));
                        matcher.InsertAndAdvance(CodeInstruction.Call<State>(s => SetPQSActive(null, ref s)));
                    }
                );

            return matcher.Instructions();
        }

        struct State
        {
            public bool activated;
        }

        static void SetPQSActive(PSystemSetup psystem, PQS pqs, ref State state)
        {
            psystem.SetPQSActive(pqs);
            state.activated = true;
        }

        static void SetPQSActive(PSystemSetup psystem, ref State state)
        {
            if (state.activated)
                return;

            psystem.SetPQSActive();
        }
    }
}
