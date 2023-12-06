// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/170
// When engaging non-physical timewarp, if the game is lagging, and the max dt per frame main menu setting is set
// to high values, the active vessel orbit will shift significantely. The root cause isn't identified, but this
// heavily suggest that some KSP components rely implicitly on Update and FixedUpdate to be called exactely once
// per frame, which isn't true in such a laggy situation (there will usually be multiple FixedUpdate calls back to
// to back followed by an Update call). We can fix this by forcing the the max dt per frame to 0.02 for a single
// frame after timewarp has been engaged, which will temporarily force a 1:1 call ratio between Update and FixedUpdate.

// debug tool : induce simulated lag in order to have multiple FixedUpdate calls per Update
// (works better by also setting max dt per frame to high value in the KSP main menu settings)
// #define LAGMACHINETEST

using System;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class TimeWarpOrbitShift : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TimeWarp), nameof(TimeWarp.setRate)),
                this));
        }

        static void TimeWarp_setRate_Prefix(TimeWarp __instance, int rateIdx)
        {
            if (HighLogic.LoadedSceneIsFlight 
                && __instance.Mode == TimeWarp.Modes.HIGH 
                && rateIdx > __instance.maxPhysicsRate_index
                && __instance.current_rate_index <= __instance.maxPhysicsRate_index)
            {
                __instance.StartCoroutine(FixMaxDTPerFrameOnEngageHighWarpCoroutine());
            }
        }

        static IEnumerator FixMaxDTPerFrameOnEngageHighWarpCoroutine()
        {
            Time.maximumDeltaTime = 0.02f;
            yield return null;
            Time.maximumDeltaTime = GameSettings.PHYSICS_FRAME_DT_LIMIT;
        }
    }

#if LAGMACHINETEST
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TimeWarpOrbitShiftTest : MonoBehaviour
    {
        private int updateLag = 30;
        private int fixedUpdateLag = 15;
        private string updateLagTxt;
        private string fixedUpdateLagTxt;
        private float maximumDeltaTime;
        private string maximumDeltaTimeTxt;

        void Start()
        {
            updateLagTxt = updateLag.ToString();
            fixedUpdateLagTxt = fixedUpdateLag.ToString();
            maximumDeltaTime = Time.maximumDeltaTime;
            maximumDeltaTimeTxt = maximumDeltaTime.ToString();
        }

        void Update() => Thread.Sleep(updateLag);
        void FixedUpdate() => Thread.Sleep(fixedUpdateLag);

        void OnGUI()
        {
            updateLagTxt = GUI.TextField(new Rect(20, 100, 50, 20), updateLagTxt);
            if (GUI.Button(new Rect(80, 100, 100, 20), "updateLag"))
                updateLag = int.Parse(updateLagTxt);

            fixedUpdateLagTxt = GUI.TextField(new Rect(20, 120, 50, 20), fixedUpdateLagTxt);
            if (GUI.Button(new Rect(80, 120, 100, 20), "fixedUpdateLag"))
                fixedUpdateLag = int.Parse(fixedUpdateLagTxt);

            if (maximumDeltaTime != Time.maximumDeltaTime)
            {
                maximumDeltaTime = Time.maximumDeltaTime;
                maximumDeltaTimeTxt = maximumDeltaTime.ToString();
            }

            maximumDeltaTimeTxt = GUI.TextField(new Rect(20, 140, 50, 20), maximumDeltaTimeTxt);
            if (GUI.Button(new Rect(80, 140, 100, 20), "maxDT"))
            {
                maximumDeltaTime = float.Parse(maximumDeltaTimeTxt);
                Time.maximumDeltaTime = maximumDeltaTime;
            }
            
            if (GUI.Button(new Rect(20, 160, 100, 20), "start warp"))
                TimeWarp.SetRate(TimeWarp.fetch.warpRates.Length - 1, false);

            if (GUI.Button(new Rect(20, 180, 100, 20), "stop warp"))
                TimeWarp.SetRate(0, true);
        }
    }
#endif
}
