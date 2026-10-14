using System;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    /// <summary>
    /// Make the orientation of Unity's world axes at the start of a flight scene a function of the clock alone.<para/>
    /// KSP keeps two angles per body: in an inertial scene the body turns in a fixed world
    /// (<c>directRotAngle = rotationAngle - Planetarium.InverseRotAngle</c>); in the rotating frame of a flight below
    /// the inverse-rotation altitude the body sits at <c>directRotAngle</c> and the world turns
    /// (<c>Planetarium.InverseRotAngle = rotationAngle - directRotAngle</c>). Each scene freezes one and moves the other,
    /// and neither is in the save (<see cref="CelestialBody.CBUpdate"/>).<para/>
    /// That is consistent while the clock runs on, but a clock jump that lands in an inertial pass - the first frames of
    /// a flight scene loaded after "Revert to VAB/SPH" (the clock goes back over the flight), or after a mod reverted
    /// from its own time - recomputes <c>directRotAngle</c> from an <c>InverseRotAngle</c> the clock no longer agrees with.
    /// The planet's azimuth in world coordinates then shifts by the planet's rotation over the jump, cumulatively with
    /// every such cycle. Anything judged in world axes - the axis-aligned rectangle test in
    /// <see cref="OcclusionData.Update"/> that stock's convection, solar and body occlusion between parts rests on -
    /// changes with what was flown before: the same vessel on the same launch, same clock, gets different part
    /// shading and heating from one launch to the next.<para/>
    /// When a flight scene has loaded, before <see cref="PSystemSetup"/> sets it up, <c>InverseRotAngle</c> and
    /// <see cref="Planetarium.Zup"/> are zeroed and every body is rebuilt from the clock, the state a freshly started game
    /// has. <c>Zup</c> goes with the angle because everything that orbits is placed through it: left alone, orbits and
    /// body surfaces disagree by the old angle, and mods that build body frames from it (TiltEm) keep the drift. Doing
    /// it on load rather than on the request leaves the outgoing scene, which may still run after the request, in the
    /// frame it had. <c>FlightDriver</c> moves the clock (resuming a save) while the scene is still inertial, so the
    /// flight gets the frame of the clock it flies at. Other scenes keep stock's behaviour, and within a rotating scene
    /// the frame stays fixed at its start as in stock, so a clock jump made inside one (warping on the pad) is still
    /// carried by the world axes.
    /// </summary>
    class WorldFrameDrift : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(PSystemSetup), nameof(PSystemSetup.OnLevelLoaded));
        }

        private static void PSystemSetup_OnLevelLoaded_Prefix(GameScenes level)
        {
            if (level != GameScenes.FLIGHT || FlightGlobals.Bodies == null || Planetarium.fetch.IsNullOrDestroyed())
                return;

            // PSystemSetup.OnSceneChange cleared these on the load request; a scene loaded some other way, or a mod
            // setting them early, would otherwise send UpdateCBs down the rotating branch, rebuilding the old frame.
            for (int i = FlightGlobals.Bodies.Count; i-- > 0;)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body.IsNullOrDestroyed())
                    continue;

                body.inverseRotation = false;
                if (body.orbitDriver.IsNotNullOrDestroyed())
                    body.orbitDriver.reverse = false;
            }

            // As Planetarium.Awake builds it; stock only ever sets Zup from InverseRotAngle.
            Planetarium.InverseRotAngle = 0.0;
            Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, 0.0, ref Planetarium.Zup);
            Planetarium.Rotation = QuaternionD.Inverse(Planetarium.Zup.Rotation).swizzle;

            // Each body takes the inertial branch: directRotAngle = rotationAngle, and BodyFrame and the orbits are
            // rebuilt against the new Zup before the scene places the launch site and loads vessels against them.
            Planetarium.fetch.UpdateCBs();
        }
    }
}
