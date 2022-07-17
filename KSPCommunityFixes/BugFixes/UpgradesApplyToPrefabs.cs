using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Editor;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class UpgradesApplyToPrefabs : BasePatch
    {
        public static readonly Dictionary<AvailablePart, Tuple<Part, bool>> _apToPart = new Dictionary<AvailablePart, Tuple<Part, bool>>();
        private static GameObject _substitutePartRoot = null;

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.Setup), new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.Setup), new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.UpdateVariantText)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.GetUpgradedPrimaryInfo)),
                this));
        }

        // We're not using UpgradesAvailable / FindUpgrades
        // because we really only care about actually-available upgrades
        static bool PartHasUpgrades(AvailablePart availablePart)
        {
            foreach (var pm in availablePart.partPrefab.Modules)
            {
                if (pm.upgrades != null)
                {
                    foreach (var node in pm.upgrades)
                    {
                        string name = node.GetValue("name__");
                        if (!string.IsNullOrEmpty(name) && PartUpgradeManager.Handler.IsEnabled(name))
                            return true;
                    }
                }
            }
            return false;
        }

        static Part GetSubstitutePart(AvailablePart ap)
        {
            if (!PartUpgradeManager.Handler.UgpradesAllowed() || !PartHasUpgrades(ap))
                return ap.partPrefab;

            if (!_apToPart.TryGetValue(ap, out var tuple))
            {
                Debug.Log($"[KSPCommunityFixes] Creating substitute part for {ap.name}");

                if (_substitutePartRoot == null)
                {
                    _substitutePartRoot = new GameObject("SubstitutePartHolder");
                    var ondestComp = _substitutePartRoot.AddComponent<ClearSubsitutePartDictOnDestroy>();
                    ondestComp.Setup(_apToPart);
                }

                ConfigNode savedModules = new ConfigNode("PART");
                for (int i = 0; i < ap.partPrefab.modules.Count; ++i)
                {
                    var cn = savedModules.AddNode("MODULE");
                    try
                    {
                        ap.partPrefab.modules[i].Save(cn);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[KSPCommunityFixes] Exception saving partmodule " + ap.partPrefab.modules[i].name + ": " + e);
                    }
                }
                Part p = GameObject.Instantiate(ap.partPrefab);
                p.gameObject.transform.SetParent(_substitutePartRoot.transform, true);
                p.partInfo = ap;
                try
                {
                    p.gameObject.SetActive(true);
                }
                catch (Exception e)
                {
                    Debug.LogError("[KSPCommunityFixes] Exception awaking part: " + e);
                }

                for (int i = 0; i < savedModules.nodes.Count; ++i)
                {
                    try
                    {
                        p.Modules[i].Load(savedModules.nodes[i]);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[KSPCommunityFixes] Exception loading partmodule " + p.modules[i].name + ": " + e);
                    }
                }

                p.prefabMass = ap.partPrefab.mass;

                p.gameObject.SetActive(false);
                
                tuple = new Tuple<Part, bool>(p, false);
                _apToPart[ap] = tuple;
                Debug.Log($"[KSPCommunityFixes] Created substitute part for {ap.name}.");
            }
            return tuple.Item1;
        }

        static IEnumerable<CodeInstruction> PartListTooltip_Setup_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            // Don't store partRef - we're setting it in Prefix.
            // (That should be done as transpiler too but my transpiler-fu is weak.)
            for (int i = 0; i < code.Count - 1; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0 && code[i + 1].opcode == OpCodes.Ldarg_0)
                {
                    code[i++] = new CodeInstruction(OpCodes.Nop);
                    code[i++] = new CodeInstruction(OpCodes.Nop);
                    code[i++] = new CodeInstruction(OpCodes.Nop);
                    code[i++] = new CodeInstruction(OpCodes.Nop);
                    code[i++] = new CodeInstruction(OpCodes.Nop);
                    break;
                }
            }

            ReplacePartPrefabCallWithPartRef(code);

            return code;
        }

        static void PartListTooltip_Setup_Prefix(PartListTooltip __instance, ref AvailablePart availablePart)
        {
            __instance.partRef = GetSubstitutePart(availablePart);
        }

        static void ReplacePartPrefabCallWithPartRef(List<CodeInstruction> code)
        {
            FieldInfo partInfoField = AccessTools.Field(typeof(PartListTooltip), nameof(PartListTooltip.partInfo));
            FieldInfo partPrefabField = AccessTools.Field(typeof(AvailablePart), nameof(AvailablePart.partPrefab));
            FieldInfo partRefField = AccessTools.Field(typeof(PartListTooltip), nameof(PartListTooltip.partRef));

            for (int i = 0; i < code.Count - 1; ++i)
            {
                if (code[i].opcode == OpCodes.Ldfld && ReferenceEquals(code[i].operand, partInfoField)
                    && code[i + 1].opcode == OpCodes.Ldfld && ReferenceEquals(code[i + 1].operand, partPrefabField))
                {
                    code[i].operand = partRefField;
                    code[i + 1] = new CodeInstruction(OpCodes.Nop);
                    break;
                }
            }
        }

        static IEnumerable<CodeInstruction> PartListTooltip_UpdateVariantText_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            ReplacePartPrefabCallWithPartRef(code);
            return code;
        }

        static bool PartListTooltip_GetUpgradedPrimaryInfo_Prefix(ref AvailablePart aP, ref int maxLines, ref string __result)
        {
            // Replace with our substitute
            Part part = GetSubstitutePart(aP);

            List<string> list = new List<string>();

            if (part.GetType().IsSubclassOf(typeof(Part)))
            {
                if (part is IModuleInfo moduleInfo)
                    list.Add(moduleInfo.GetPrimaryField() + "\n");
                else
                    list.Add(part.drawStats().Trim());
            }
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                if (part.Modules[i] is IModuleInfo moduleInfo)
                    list.Add(moduleInfo.GetPrimaryField());
            }
            for (int i = 0; i < aP.resourceInfos.Count; ++i)
            {
                AvailablePart.ResourceInfo resourceInfo = aP.resourceInfos[i];
                if (!string.IsNullOrEmpty(resourceInfo.primaryInfo))
                    list.Add(resourceInfo.primaryInfo + "\n");
            }

            var stringBuilder = new System.Text.StringBuilder();
            int max = Math.Min(maxLines, list.Count);
            for (int i = 0; i < max; ++i)
                stringBuilder.Append(list[i]);
            __result = stringBuilder.ToString().Trim();

            return false;
        }
    }

    public class ClearSubsitutePartDictOnDestroy : MonoBehaviour
    {
        private Dictionary<AvailablePart, Tuple<Part, bool>> _dict;

        public void Setup(Dictionary<AvailablePart, Tuple<Part, bool>> dict)
        {
            _dict = dict;
        }

        private void OnDestroy()
        {
            _dict.Clear();
        }
    }
}
