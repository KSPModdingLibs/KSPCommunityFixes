// Based upon https://forum.unity.com/threads/garbage-collector-spikes-because-of-gui.60217/page-2#post-6317280
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
                AccessTools.Method(typeof(GUILayoutUtility), nameof(GUILayoutUtility.Begin))));
        }

        static bool GUILayoutUtility_Begin_Prefix(int instanceID)
        {
            GUILayoutUtility.LayoutCache layoutCache = GUILayoutUtility.SelectIDList(instanceID, false);
            if (Event.current.type == EventType.Layout)
            {
                if (layoutCache.topLevel == null)
                    layoutCache.topLevel = new GUILayoutGroup();
                else
                    ResetGUILayoutGroup(layoutCache.topLevel);

                if (layoutCache.windows == null)
                    layoutCache.windows = new GUILayoutGroup();
                else
                    ResetGUILayoutGroup(layoutCache.windows);

                GUILayoutUtility.current.topLevel = layoutCache.topLevel;
                GUILayoutUtility.current.layoutGroups.Clear();
                GUILayoutUtility.current.layoutGroups.Push(GUILayoutUtility.current.topLevel);
                GUILayoutUtility.current.windows = layoutCache.windows;
                return false;
            }
            GUILayoutUtility.current.topLevel = layoutCache.topLevel;
            GUILayoutUtility.current.layoutGroups = layoutCache.layoutGroups;
            GUILayoutUtility.current.windows = layoutCache.windows;
            return false;
        }

        static void ResetGUILayoutGroup(GUILayoutGroup layoutGroup)
        {
            layoutGroup.entries.Clear();
            layoutGroup.isVertical = true;
            layoutGroup.resetCoords = false;
            layoutGroup.spacing = 0f;
            layoutGroup.sameSize = true;
            layoutGroup.isWindow = false;
            layoutGroup.windowID = -1;
            layoutGroup.m_Cursor = 0;
            layoutGroup.m_StretchableCountX = 100;
            layoutGroup.m_StretchableCountY = 100;
            layoutGroup.m_UserSpecifiedWidth = false;
            layoutGroup.m_UserSpecifiedHeight = false;
            layoutGroup.m_ChildMinWidth = 100f;
            layoutGroup.m_ChildMaxWidth = 100f;
            layoutGroup.m_ChildMinHeight = 100f;
            layoutGroup.m_ChildMaxHeight = 100f;
            layoutGroup.m_MarginLeft = 0;
            layoutGroup.m_MarginRight = 0;
            layoutGroup.m_MarginTop = 0;
            layoutGroup.m_MarginBottom = 0;
            layoutGroup.rect = new Rect(0f, 0f, 0f, 0f);
            layoutGroup.consideredForMargin = true;
            layoutGroup.m_Style = GUIStyle.none;
            layoutGroup.stretchWidth = 1;
            layoutGroup.stretchHeight = 0;
            layoutGroup.minWidth = 0f;
            layoutGroup.maxWidth = 0f;
            layoutGroup.minHeight = 0f;
            layoutGroup.maxHeight = 0f;
        }
    }
}
