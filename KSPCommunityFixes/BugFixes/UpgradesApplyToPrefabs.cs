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
        private static readonly Dictionary<AvailablePart, Part> _apToPart = new Dictionary<AvailablePart, Part>();

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartListTooltip), "Setup", new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "Setup", new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartListTooltip), "CreateExtendedInfo"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartListTooltip), "UpdateVariantText"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "GetUpgradedPrimaryInfo"),
                this));
        }

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

        static IEnumerable<CodeInstruction> PartListTooltip_CreateExtendedInfo_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            ReplacePartPrefabCallWithPartRef(code);
            return code;
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
