// Copypasted from https://forum.unity.com/threads/garbage-collector-spikes-because-of-gui.60217/page-2#post-6317280
// Basically, every OnGUI() method implies a call to GUILayout.Repaint(), itself calling GUILayoutUtility.Begin(),
// which does a bunch of allocations (even if the OnGUI() method does nothing). This replacement method reuse the
// existing objects instead of instantiating new ones, which both reduce GC pressure (about 0.36 KB/frame per OnGUI()
// method) and slightly reduce CPU usage. This can sum up to a significant improvement, especially for plugins
// having an OnGUI() method on PartModules. An example of such a plugin is PAWS, which spam a such a partmodule on 
// every part. In that (worst case) scenario, with a stock dynawing (~130 parts), this patch reduce GC pressure by
// 52 KB/frame and reduce CPU time by about 0.3ms.

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class IMGUIOptimization : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(GUILayoutUtility), nameof(GUILayoutUtility.Begin)),
                this));
        }

        static bool GUILayoutUtility_Begin_Prefix(int instanceID)
        {
            GUILayoutUtility.LayoutCache layoutCache = GUILayoutUtility.SelectIDList(instanceID, false);
            if (Event.current.type == EventType.Layout)
            {
                if (layoutCache.topLevel == null)
                    layoutCache.topLevel = new GUILayoutGroup();
                if (layoutCache.windows == null)
                    layoutCache.windows = new GUILayoutGroup();

                layoutCache.topLevel.entries.Clear();
                layoutCache.windows.entries.Clear();

                GUILayoutUtility.current.topLevel = layoutCache.topLevel;
                GUILayoutUtility.current.layoutGroups.Clear();
                GUILayoutUtility.current.layoutGroups.Push(GUILayoutUtility.current.topLevel);
                return false;
            }
            GUILayoutUtility.current.topLevel = layoutCache.topLevel;
            GUILayoutUtility.current.layoutGroups = layoutCache.layoutGroups;
            GUILayoutUtility.current.windows = layoutCache.windows;
            return false;
        }
    }
}
