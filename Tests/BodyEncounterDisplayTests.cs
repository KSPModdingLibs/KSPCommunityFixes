using PatchedConicFixes;
using BodyEncounterDisplay = KSPCommunityFixes.BodyEncounterDisplay;
using System;
using System.Reflection;
using HarmonyLib;
using Xunit;
using Xunit.Abstractions;

namespace KSPCommunityFixes.Tests
{
    [Collection("KSP")]
    public class BodyEncounterDisplayTests
    {
        private readonly ITestOutputHelper _output;
        public BodyEncounterDisplayTests(ITestOutputHelper output) => _output = output;

        private static Orbit Patch(CelestialBody body, double start, double end, double periapsisUT,
            bool active = true, Orbit.PatchTransitionType transition = Orbit.PatchTransitionType.ESCAPE) =>
            new Orbit(0, 0.2, 7000000, 0, 0, 0, start, body)
            {
                StartUT = start, EndUT = end, timeToPe = periapsisUT - start,
                activePatch = active, patchEndTransition = transition
            };

        [Fact]
        public void BodyEncounterIsSelectedBeforeTheLaterParentApproach()
        {
            var (parent, child) = Bodies.MakeEarthMoon();
            Orbit current = Patch(parent, 100, 200, 150);
            Orbit encounter = Patch(child, 200, 400, 300);
            Orbit exit = Patch(parent, 400, 800, 600);
            current.nextPatch = encounter;
            encounter.nextPatch = exit;
            Assert.True(BodyEncounterDisplay.TryGetPeriapsis(current, child, 100, out Orbit selected, out double ut));
            Assert.Same(encounter, selected);
            Assert.Equal(300, ut);
        }

        [Fact]
        public void MissingEncounterLeavesTheStockReadoutUntouched()
        {
            var (parent, child) = Bodies.MakeEarthMoon();
            Assert.False(BodyEncounterDisplay.TryGetPeriapsis(Patch(parent, 100, 200, 150), child, 100, out _, out _));
        }

        [Fact]
        public void ManeuverPreviewUsesItsOwnFutureChain()
        {
            var (parent, child) = Bodies.MakeEarthMoon();
            Orbit real = Patch(child, 100, 200, 150);
            Orbit preview = Patch(parent, 100, 200, 150);
            Assert.True(BodyEncounterDisplay.TryGetPeriapsis(real, child, 100, out _, out _));
            Assert.False(BodyEncounterDisplay.TryGetPeriapsis(preview, child, 100, out _, out _));
            preview.nextPatch = Patch(child, 200, 400, 300);
            Assert.True(BodyEncounterDisplay.TryGetPeriapsis(preview, child, 100, out Orbit selected, out _));
            Assert.Same(preview.nextPatch, selected);
        }

        [Theory]
        [InlineData(99)]
        [InlineData(201)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void PeriapsisMustBeFiniteAndInsideTheFuturePatch(double ut)
        {
            var (_, child) = Bodies.MakeEarthMoon();
            Assert.False(BodyEncounterDisplay.TryGetPeriapsis(Patch(child, 100, 200, ut), child, 100, out _, out _));
        }

        [Fact]
        public void InactiveEncounterIsNotUsed()
        {
            var (parent, child) = Bodies.MakeEarthMoon();
            Orbit current = Patch(parent, 100, 200, 150);
            current.nextPatch = Patch(child, 200, 400, 300, active: false);
            Assert.False(BodyEncounterDisplay.TryGetPeriapsis(current, child, 100, out _, out _));
        }

        [Fact]
        public void CyclicPatchChainTerminatesWithoutAnEncounter()
        {
            var (parent, child) = Bodies.MakeEarthMoon();
            Orbit current = Patch(parent, 100, 200, 150);
            current.nextPatch = current;
            Assert.False(BodyEncounterDisplay.TryGetPeriapsis(current, child, 100, out _, out _));
        }

        [Fact]
        public void KspcfRegistersOnlyTheApproachPostfix()
        {
            var patch = new TargetBodyEncounterPeriapsis();
            AccessTools.Method(typeof(TargetBodyEncounterPeriapsis), "ApplyPatches").Invoke(patch, null);
            var registered = (System.Collections.IList)AccessTools.Field(typeof(BasePatch), "patches").GetValue(patch);
            Assert.Single(registered);
            object info = registered[0];
            var method = (MethodBase)AccessTools.Field(info.GetType(), "patchedMethod").GetValue(info);
            Assert.Equal(typeof(ManeuverNodeEditorTabIntercept), method.DeclaringType);
            Assert.Equal("UpdateUIElements", method.Name);
            Assert.Equal("Postfix", AccessTools.Field(info.GetType(), "patchType").GetValue(info).ToString());
            Assert.NotNull(AccessTools.Method(typeof(TargetBodyEncounterPeriapsis),
                "ManeuverNodeEditorTabIntercept_UpdateUIElements_Postfix"));
        }

        [Fact]
        public void ProductionPatchDoesNotDependOnPcf()
        {
            Assert.DoesNotContain(typeof(TargetBodyEncounterPeriapsis).Assembly.GetReferencedAssemblies(),
                reference => reference.Name == "PatchedConicFixes");
        }

        [Fact]
        public void PostfixFieldsExistInTheInstalledStockUiMethod()
        {
            Type type = typeof(ManeuverNodeEditorTabIntercept);
            Assert.NotNull(AccessTools.Method(type, nameof(ManeuverNodeEditorTabIntercept.UpdateUIElements)));
            Assert.Equal(typeof(Orbit), AccessTools.Field(type, "orbitContainingUT").FieldType);
            Assert.Equal(typeof(string), AccessTools.Field(type, "cacheAutoLOC_7001411").FieldType);
            Assert.Equal(typeof(string), AccessTools.Field(type, "cacheAutoLOC_6002332").FieldType);
            Assert.NotNull(type.GetField("intercept1DistanceLabel", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(type.GetField("intercept1TimeLabel", BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public unsafe void RecordedMunEncounterIsSelectedDespiteCrossBodyIntersectionRejection()
        {
            Logger.Register(o => _output.WriteLine((string)o));
            const double now = 28782590.235272601;
            var (kerbin, mun) = Bodies.MakeParentChild(3531600000000, 84159286,
                65138398000, 2429559.11656475, 0, 0, 12000000, 0, 0, 1.7, 0);
            kerbin.Radius = 600000;
            mun.Radius = 200000;
            // Recorded Kerbin-to-Mun transfer orbit and stock Mun ephemeris.
            var current = new Orbit(1.303728754981702, 0.87004394617151537, 6168433.3796730088,
                23.40533512408183, 358.85396286076622, 0.016804177472986417, now, kerbin)
                { StartUT = now, activePatch = true };
            current.EndUT = now + current.period;
            var encounter = new Orbit();
            Assert.True(HarmonyPatches.CheckEncounter(current, encounter, now, mun.orbitDriver, mun,
                new PatchedConics.SolverParameters(), false));
            encounter.StartUT = encounter.epoch;
            encounter.activePatch = true;
            encounter.patchEndTransition = Orbit.PatchTransitionType.FINAL;
            current.nextPatch = encounter;

            double cd = 0, ccd = 0, ffp = 0, ffs = 0, sfp = 0, sfs = 0;
            int iterations = 0;
            Assert.Equal(0, InterceptDisplay.SelectDisplayIntercepts(encounter, mun.orbit,
                ref cd, ref ccd, ref ffp, ref ffs, ref sfp, ref sfs, 0.0001, 20, ref iterations));
            Assert.True(BodyEncounterDisplay.TryGetPeriapsis(current, mun, now, out Orbit selected, out double ut));
            Assert.Same(encounter, selected);
            Assert.Equal(encounter.StartUT + encounter.timeToPe, ut);
            _output.WriteLine($"Recorded approach: Mun PeA={selected.PeA:R} m; periapsisUT={ut:R}");
            Assert.InRange(selected.PeA, 24972.5, 24973.5); // screenshot rounds to 24,973 m
        }
    }
}
