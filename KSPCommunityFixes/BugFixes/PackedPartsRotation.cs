using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // When a vessel is packed (docking, timewarping, getting out of physics range...), OrbitDriver always update the part
    // transform positions based on Part.orgPos. However, it only update the transform rotations (with Part.orgRot) if the
    // vessel is landed (while in OrbitDriver.UpdateMode.IDLE). While this functionaly make sense, it mean the part rotations
    // of non-landed vessels will remain as they were in physics, including physics-induced displacement.
    // This can easily be reproduced by having a very wobbly vessel in orbit and engaging timewarp. The parts will snap to
    // their pristine positions, but will keep whatever orientation they were in.
    // This is the source of a bunch of minor/cosmetic inacuracies, but this also has a more problematic effect when docking. 
    // Prior to "merging" vessels while docking, they are packed to ensure the part orgPos/orgRot are updated using part
    // transforms in a "pristine" position/orientation. Since the transforms rotation aren't updated, the current in-physics
    // deformation will become permanent.
    // To fix this, we reset the part transform rotations to their pristine orgRot when a vessel is being packed.

    // Finding the ideal timing to do this is a bit difficult. Packed vessels position is updated by the OrbitDriver, meaning
    // this won't happen immediately when packing, but usually on the next frame. Doing it in the OrbitDriver update is messy
    // and will likely interfere with mods implementing packed rotation. We certainly don't want to rotate the parts before
    // GoOnRails(), as this will have a (minor, but still) effect on the VesselPrecalculate integrator and on anything using
    // the various callbacks : partmodules with OnPartPack(), vesselmodules with OnGoOnRails(), and the various GameEvents.
    // Moreover, we need orgRot to be up-to-date, and many stuff (like BG robotics) are updating it on vessel packing.

    // So we settle for a one time call just after Vessel.GoOnRails(). This ensure things are unchanged for everything doing
    // stuff during that event, and it shouldn't affect anything messing with rotation afterwards.

    class PackedPartsRotation : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Vessel), nameof(Vessel.GoOnRails)),
                this));
        }

        static void Vessel_GoOnRails_Postfix(Vessel __instance)
        {
            if (__instance.orbitDriver.updateMode != OrbitDriver.UpdateMode.UPDATE)
                return;

            Quaternion vesselRotation = __instance.vesselTransform.rotation;

            int partCount = __instance.parts.Count;
            for (int i = 0; i < partCount; i++)
            {
                __instance.parts[i].transform.rotation = vesselRotation * __instance.parts[i].orgRot;
            }
        }
    }
}
