using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPCommunityFixes : MonoBehaviour
    {
        void Start()
        {
            StartCoroutine(DelayedStart());
        }

        public IEnumerator DelayedStart()
        {
            yield return null;

            Harmony harmony = new Harmony("KSPCommunityFixes");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Destroy(this);
        }
    }
}
