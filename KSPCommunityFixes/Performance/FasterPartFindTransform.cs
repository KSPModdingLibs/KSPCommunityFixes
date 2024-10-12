// Faster, and minimal GC alloc relacements for the Part FindModelTransform* and FindHeirarchyTransform* methods.
// Roughly 4 times faster on average for callers in a stock install, but this can go up to 20x faster on deep hierarchies.
// The main trick is to use Transform.Find() instead of checking the Transform.name property (which will allocate a new string)
// for every transform in the hierarchy. There are a few quirks with Transform.Find() requiring additional care, but this is 
// overall much faster and eliminate GC allocations almost entirely.
// The methods are used quite a bit in stock, and massively over the modding ecosystem, so this can account to significant
// gains overall : https://github.com/search?q=FindModelTransform+OR+FindModelTransforms+language%3AC%23&type=code

// Uncomment to enable verification that kspcf results are matching stock, and stopwatch based perf comparison. Very logspammy.
// #define FPFT_DEBUG_PROFILE

using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class FasterPartFindTransform : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
#if !FPFT_DEBUG_PROFILE
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindHeirarchyTransform)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindHeirarchyTransforms)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindModelTransform)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindModelTransforms)),
                this));
#else
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindModelTransform)),
                this, nameof(Part_FindModelTransform_Prefix_Debug)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.FindModelTransforms)),
                this, nameof(Part_FindModelTransforms_Prefix_Debug)));
#endif
        }

        static bool Part_FindModelTransform_Prefix(Part __instance, string childName, out Transform __result)
        {
            // We need to check for the empty case because calling myTransform.Find("") will return myTransform...
            if (string.IsNullOrEmpty(childName))
            {
                __result = null;
                return false;
            }

            Transform model = __instance.partTransform.Find("model");

            if (model.IsNullRef())
            {
                __result = null;
                return false;
            }

            // special case where the method is called to find the model transform itself
            if (childName == "model")
            {
                __result = model;
                return false;
            }

            // Transform.Find() will treat '/' as a hierarchy path separator.
            // In that (hopefully very rare) case we fall back to the stock method.
            if (childName.IndexOf('/') != -1)
            {
                __result = null;
                return true;
            }
            __result = FindChildRecursive(model, childName);
            return false;
        }

        static bool Part_FindHeirarchyTransform_Prefix(Transform parent, string childName, out Transform __result)
        {
            // We need to check for the empty case because calling myTransform.Find("") will return myTransform...
            if (parent.IsNullOrDestroyed() || string.IsNullOrEmpty(childName))
            {
                __result = null;
                return false;
            }

            if (parent.gameObject.name == childName)
            {
                __result = parent;
                return false;
            }

            // Transform.Find() will treat '/' as a hierarchy path separator.
            // In that (hopefully very rare) case we fall back to the stock method.
            if (childName.IndexOf('/') != -1)
            {
                __result = null;
                return true;
            }

            __result = FindChildRecursive(parent, childName);
            return false;
        }

        static Transform FindChildRecursive(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child.IsNotNullRef())
                return child;

            int childCount = parent.childCount;
            for (int i = 0; i < childCount; i++)
            {
                child = FindChildRecursive(parent.GetChild(i), childName);
                if (child.IsNotNullRef())
                    return child;
            }

            return null;
        }

        static readonly List<Transform> transformBuffer = new List<Transform>();

        static bool Part_FindModelTransforms_Prefix(Part __instance, string childName, out Transform[] __result)
        {
            try
            {
                // We need to check for the empty case because calling myTransform.Find("") will return myTransform...
                if (string.IsNullOrEmpty(childName))
                {
                    __result = Array.Empty<Transform>();
                    return false;
                }

                // Transform.Find() will treat '/' as a hierarchy path separator.
                // In that (hopefully very rare) case we fall back to the stock method.
                if (childName.IndexOf('/') != -1)
                {
                    __result = null;
                    return true;
                }

                Transform model = __instance.partTransform.Find("model");

                if (model.IsNullRef())
                {
                    __result = Array.Empty<Transform>();
                    return false;
                }

                // special case where the method is called to find the model transform itself
                if (childName == "model")
                    transformBuffer.Add(model);

                FindChildsRecursive(model, childName, transformBuffer);
                __result = transformBuffer.ToArray();
            }
            finally
            {
                transformBuffer.Clear();
            }

            return false;
        }

        static bool Part_FindHeirarchyTransforms_Prefix(Transform parent, string childName, List<Transform> tList)
        {
            // We need to check for the empty case because calling myTransform.Find("") will return myTransform...
            if (parent.IsNullOrDestroyed() || string.IsNullOrEmpty(childName))
                return false;

            // Transform.Find() will treat '/' as a hierarchy path separator.
            // In that (hopefully very rare) case we fall back to the stock method.
            if (childName.IndexOf('/') != -1)
                return true;

            if (parent.gameObject.name == childName)
                tList.Add(parent);

            FindChildsRecursive(parent, childName, tList);
            return false;
        }

        static void FindChildsRecursive(Transform parent, string childName, List<Transform> results)
        {
            int childCount = parent.childCount;
            if (childCount == 0)
                return;

            Transform matchingChild = parent.Find(childName);

            if (matchingChild.IsNotNullRef())
            {
                if (childCount == 1)
                {
                    results.Add(matchingChild);
                    FindChildsRecursive(matchingChild, childName, results);
                }
                else
                {
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = parent.GetChild(i);
                        if (child.name == childName)
                            results.Add(child);

                        FindChildsRecursive(child, childName, results);
                    }
                }
            }
            else
            {
                for (int i = 0; i < childCount; i++)
                    FindChildsRecursive(parent.GetChild(i), childName, results);
            }
        }

#if FPFT_DEBUG_PROFILE
        private static System.Diagnostics.Stopwatch findModel_Stock = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch findModel_KSPCF = new System.Diagnostics.Stopwatch();
        private static int findModelMismatches = 0;
        private static System.Diagnostics.Stopwatch findModels_Stock = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch findModels_KSPCF = new System.Diagnostics.Stopwatch();
        private static int findModelsMismatches = 0;

        static bool Part_FindModelTransform_Prefix_Debug(Part __instance, string childName, out Transform __result)
        {
            findModel_Stock.Start();
            __result = Part.FindHeirarchyTransform(__instance.partTransform.Find("model"), childName);
            findModel_Stock.Stop();

            findModel_KSPCF.Start();
            if (Part_FindModelTransform_Prefix(__instance, childName, out Transform kspcfResult))
                kspcfResult = Part.FindHeirarchyTransform(__instance.partTransform.Find("model"), childName);
            findModel_KSPCF.Stop();

            if (__result != kspcfResult)
            {
                findModelMismatches++;
                Part_FindModelTransform_Prefix(__instance, childName, out kspcfResult);
            }

            Debug.Log($"FindModel [Stock: {findModel_Stock.Elapsed.TotalMilliseconds:F0} ms] [KSPCF: {findModel_KSPCF.Elapsed.TotalMilliseconds:F0} ms] [Mistmatches:{findModelMismatches}]");
            return false;
        }

        static bool Part_FindModelTransforms_Prefix_Debug(Part __instance, string childName, out Transform[] __result)
        {
            findModels_Stock.Start();
            List<Transform> list = new List<Transform>();
            Part.FindHeirarchyTransforms(__instance.partTransform.Find("model"), childName, list);
            __result = list.ToArray();
            findModels_Stock.Stop();

            findModels_KSPCF.Start();
            if (Part_FindModelTransforms_Prefix(__instance, childName, out Transform[] kspcfResult))
            {
                list = new List<Transform>();
                Part.FindHeirarchyTransforms(__instance.partTransform.Find("model"), childName, list);
                kspcfResult = list.ToArray();
            }
            findModels_KSPCF.Stop();

            if (!System.Linq.Enumerable.SequenceEqual(__result, kspcfResult))
            {
                findModelsMismatches++;
                Part_FindModelTransforms_Prefix(__instance, childName, out kspcfResult);
            }

            Debug.Log($"[FindModels Stock: {findModels_Stock.Elapsed.TotalMilliseconds:F0} ms] [KSPCF: {findModels_KSPCF.Elapsed.TotalMilliseconds:F0} ms] [Mistmatches:{findModelsMismatches}]");
            return false;
        }
#endif
    }
}
