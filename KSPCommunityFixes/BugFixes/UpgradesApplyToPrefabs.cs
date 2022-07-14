using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Editor;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class UpgradesApplyToPrefabs : BasePatch
    {
        private static readonly Dictionary<AvailablePart, Part> _apToPart = new Dictionary<AvailablePart, Part>();

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "Setup", new Type[] {typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltip), "Setup", new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "UpdateVariantText"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltip), "UpdateVariantText"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "UpdateCargoPartModuleInfo"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltip), "UpdateCargoPartModuleInfo"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "CreateExtendedInfo"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltip), "CreateExtendedInfo"),
                this));
        }

        static Part GetSubstitutePart(AvailablePart ap)
        {
            if (!_apToPart.TryGetValue(ap, out Part p))
            {
                Debug.Log($"Creating substitute part for {ap.name}.");
                p = GameObject.Instantiate(ap.partPrefab);
                p.partInfo = ap;
                var scene = HighLogic.LoadedScene;
                var wasEd = HighLogic.LoadedSceneIsEditor;
                HighLogic.LoadedSceneIsEditor = false;
                HighLogic.LoadedScene = GameScenes.LOADING;
                p.gameObject.SetActive(false);

                // Remove all partmodules so we can readd fresh
                PartModule[] mods = p.GetComponentsInChildren<PartModule>();
                foreach (var pm in mods)
                    GameObject.DestroyImmediate(pm);
                if (p.modules != null)
                {
                    p.modules.modules?.Clear();
                    p.ClearModuleReferenceCache();
                }

                p.gameObject.SetActive(true);

                ConfigNode[] modules = ap.partConfig.nodes.GetNodes("MODULE");
                for (int i = 0; i < modules.Length; ++i)
                    p.AddModule(modules[i]);

                // Fix for mods that add partmodules after loading
                if (ap.partPrefab.modules.Count > p.modules.Count)
                {
                    Debug.Log($"Prefab has more modules than the partConfig: prefab has {ap.partPrefab.modules.Count} and config had {p.modules.Count}. Adding extra modules.");
                    for (int i = p.modules.Count; i < ap.partPrefab.modules.Count; ++i)
                    {
                        ConfigNode n = new ConfigNode("MODULE");
                        ap.partPrefab.modules[i].Save(n);
                        Debug.Log($"Adding {n.GetValue("name") ?? "null"}.");
                        p.AddModule(n);
                    }
                }
                Debug.Log($"Added modules.");

                var ondestComp = p.gameObject.AddComponent<SubstitutePartRemoveFromDictOnDestroy>();

                p.gameObject.SetActive(false);

                HighLogic.LoadedScene = scene;
                HighLogic.LoadedSceneIsEditor = wasEd;
                ondestComp.Setup(_apToPart, ap);
                _apToPart[ap] = p;

                Debug.Log($"Created substitute.");

                //// For some reason we have to copy resources
                //p._resources = new PartResourceList(p, ap.partPrefab.Resources);
                //// and this goes null
                //var dest = p.FindModulesImplementing<ModulePartVariants>();
                //var src = p.partInfo.partPrefab.FindModulesImplementing<ModulePartVariants>();
                //for (int i = src.Count - 1; i >= 0; --i)
                //    dest[i].partMaterials = src[i].partMaterials;

                //Debug.Log($"Created substitute part for {ap.name}.");
                //try
                //{
                //    ModulePartVariants mpv = p.FindModuleImplementing<ModulePartVariants>();
                //    Debug.Log($"MPV null? {mpv == null}");
                //    if (mpv.variantIndex >= 0)
                //    {
                //        Debug.Log($"MPV var list null? {mpv.variantList == null}. Mats null? {mpv.partMaterials == null}");
                //        if (mpv.variantIndex <= mpv.variantList.Count)
                //        {
                //            ModulePartVariants.ApplyVariant(p, p.transform.Find("model"), mpv.variantList[mpv.variantIndex], mpv.partMaterials.ToArray(), skipShader: false, mpv.variantIndex);
                //        }
                //    }

                //}
                //catch (Exception e)
                //{
                //    Debug.Log("EXC: " + e);
                //}
            }
            return p;
        }
        
        static void PartListTooltip_Setup_Prefix(PartListTooltip __instance, ref AvailablePart availablePart, out Part __state)
        {
            __state = availablePart.partPrefab;
            availablePart.partPrefab = GetSubstitutePart(availablePart);
        }

        static void PartListTooltip_Setup_Postfix(PartListTooltip __instance, ref AvailablePart availablePart, Part __state)
        {
            availablePart.partPrefab = __state;
        }

        static void PartListTooltip_UpdateVariantText_Prefix(PartListTooltip __instance, out Part __state)
        {
            __state = __instance.partInfo.partPrefab;
            __instance.partInfo.partPrefab = GetSubstitutePart(__instance.partInfo);
        }

        static void PartListTooltip_UpdateVariantText_Postfix(PartListTooltip __instance, Part __state)
        {
            __instance.partInfo.partPrefab = __state;
        }

        static void PartListTooltip_UpdateCargoPartModuleInfo_Prefix(PartListTooltip __instance, out Part __state)
        {
            __state = __instance.partInfo.partPrefab;
            __instance.partInfo.partPrefab = GetSubstitutePart(__instance.partInfo);
        }

        static void PartListTooltip_UpdateCargoPartModuleInfo_Postfix(PartListTooltip __instance, Part __state)
        {
            __instance.partInfo.partPrefab = __state;
        }

        static void PartListTooltip_CreateExtendedInfo_Prefix(PartListTooltip __instance, out Part __state)
        {
            __state = __instance.partInfo.partPrefab;
            __instance.partInfo.partPrefab = GetSubstitutePart(__instance.partInfo);
        }

        static void PartListTooltip_CreateExtendedInfo_Postfix(PartListTooltip __instance, Part __state)
        {
            __instance.partInfo.partPrefab = __state;
        }
    }

    public class SubstitutePartRemoveFromDictOnDestroy : MonoBehaviour
    {
        private Dictionary<AvailablePart, Part> _dict;
        private AvailablePart _key;

        public void Setup(Dictionary<AvailablePart, Part> dict, AvailablePart key)
        {
            _dict = dict;
            _key = key;
        }

        private void OnDestroy()
        {
            // Don't use Unity equality here
            if (_dict != null && (object)_key != null)
                _dict.Remove(_key);

            Debug.Log($"Part name {_key.name} OnDestroy, removed from dict.");
        }
    }
}
