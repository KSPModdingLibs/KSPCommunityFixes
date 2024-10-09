﻿using System;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    class FlightSceneLoadKraken : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(HighLogic), nameof(HighLogic.LoadScene));

            AddPatch(PatchType.Prefix, typeof(FlightDriver), nameof(FlightDriver.Start));
        }

        private static void HighLogic_LoadScene_Postfix(GameScenes scene)
        {
            if (scene == GameScenes.FLIGHT)
            {
                Debug.Log($"[VesselLoadKraken] Setting maximumDeltaTime to 0.02 (was {Time.maximumDeltaTime})");
                Time.maximumDeltaTime = 0.02f;
            }
        }

        private static void FlightDriver_Start_Prefix()
        {
            HighLogic.fetch.StartCoroutine(ResetMaxDeltaTime());
        }

        private static IEnumerator ResetMaxDeltaTime()
        {
            for (int i = 0; i < 50; i++)
            {
                yield return null;
            }

            Time.maximumDeltaTime = Mathf.Clamp(GameSettings.PHYSICS_FRAME_DT_LIMIT, 0.03f, 0.12f);
            Debug.Log($"[VesselLoadKraken] Resetting maximumDeltaTime to {Time.maximumDeltaTime}");
        }
    }
}