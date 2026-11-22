using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Object = UnityEngine.Object;

namespace KSPCommunityFixes.Tests
{
    public static class Bodies
    {
        // helper to fake the m_CachedPtr field off UnityEngine objects to make null checks work
        private static void SetCachedPtr(object obj)
        {
            typeof(Object)
                .GetField("m_CachedPtr", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(obj, new IntPtr(1));
        }

        public static (CelestialBody earth, CelestialBody moon) MakeEarthMoon() => MakeParentChild(3.9860043543609598e+14, 924649202.461023, 4.9028000661637961e+12, 66167158.6569544, 28.3626779079849, 0.0532814935368257, 384308437.770707, 2.29661616112602, 199.764093016082, 3.88686980063246, -31542641.784);

        public static (CelestialBody jool, CelestialBody tylo) MakeJoolTylo() => MakeParentChild(282528004209995, 2455985185.42347, 207481499473.751, 10856518.3683586, 0.025000000372529, 0, 68500000, 0, 0, 3.14000010490417, 0);

        public static (CelestialBody parent, CelestialBody child) MakeParentChild(
            double parentMu, double parentSoi,
            double childMu, double childSoi,
            double inc, double e, double sma, double lan, double argPe, double mEp, double epoch)
        {
            CelestialBody parent = MakeBody(parentMu, parentSoi);
            CelestialBody child  = MakeBody(childMu, childSoi);

            if (!(FormatterServices.GetUninitializedObject(typeof(OrbitDriver)) is OrbitDriver orbitDriver))
                throw new InvalidOperationException("Failed to create OrbitDriver");

            SetCachedPtr(orbitDriver);

            orbitDriver.orbit         = new Orbit(inc, e, sma, lan, argPe, mEp, epoch, parent);
            orbitDriver.celestialBody = child;
            child.orbitDriver         = orbitDriver;

            parent.orbitingBodies.Add(child);

            return (parent, child);
        }

        private static CelestialBody MakeBody(double mu, double soi)
        {
            if (!(FormatterServices.GetUninitializedObject(typeof(CelestialBody)) is CelestialBody cb))
                throw new InvalidOperationException("Failed to create CelestialBody");

            SetCachedPtr(cb);

            cb.gravParameter     = mu;
            cb.sphereOfInfluence = soi;
            cb.orbitingBodies    = new List<CelestialBody>();

            return cb;
        }
    }

    public static class Helpers
    {
        public static string OrbitDataString(Orbit o) => $"COrbitData({o.semiMajorAxis}, {o.eccentricity}, {o.inclination * Math.PI / 180.0}, {o.argumentOfPeriapsis * Math.PI / 180.0}, {o.LAN * Math.PI / 180.0})";
    }
}
