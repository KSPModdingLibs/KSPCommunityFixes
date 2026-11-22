using System.Runtime.Serialization;
using JetBrains.Annotations;
using UnityEngine;
using Xunit;

namespace KSPCommunityFixes.Tests
{
    /// <summary>
    ///     Defines the "KSP" collection. Any test class tagged [Collection("KSP")]
    ///     shares this fixture, which is created once before the first test in the
    ///     collection and disposed after the last.
    /// </summary>
    [CollectionDefinition("KSP")]
    public class KspCollection : ICollectionFixture<KspFixture>
    {
    }

    /// <summary>
    ///     One-time setup for KSP global state that must be initialised before any
    ///     test runs. Constructor runs once per test session (or once per isolated
    ///     run if only a subset of tests is executed).
    /// </summary>
    [UsedImplicitly]
    public class KspFixture
    {
        public KspFixture()
        {
            Planetarium.fetch = (Planetarium)FormatterServices.GetUninitializedObject(typeof(Planetarium));
            Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, 0, ref Planetarium.Zup);
            Planetarium.fetch.rotation = QuaternionD.Inverse(Planetarium.Zup.Rotation).swizzle;
            Planetarium.fetch.time     = double.NaN;
        }
    }
}
