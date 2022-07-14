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
                ConfigNode savedModules = new ConfigNode("PART");
                for (int i = 0; i < ap.partPrefab.modules.Count; ++i)
                    ap.partPrefab.modules[i].Save(savedModules.AddNode("MODULE"));

                p = GameObject.Instantiate(ap.partPrefab);
                p.partInfo = ap;
                p.gameObject.SetActive(false);

                p.gameObject.SetActive(true);

                for (int i = 0; i < savedModules.nodes.Count; ++i)
                    p.Modules[i].Load(savedModules.nodes[i]);

                Debug.Log($"Reloaded modules.");

                var ondestComp = p.gameObject.AddComponent<SubstitutePartRemoveFromDictOnDestroy>();

                p.gameObject.SetActive(false);

                ondestComp.Setup(_apToPart, ap);
                _apToPart[ap] = p;

                Debug.Log($"Created substitute part for {ap.name}.");
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
