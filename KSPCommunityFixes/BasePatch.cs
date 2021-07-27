using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    public abstract class BasePatch
    {
        private static readonly string pluginData = "PluginData";
        private static readonly Version versionMinDefault = new Version(1, 12, 0);
        private static readonly Version versionMaxDefault = new Version(1, 12, 99);

        public static void Patch<T>(bool checkConfigEnabled = true) where T : BasePatch
        {
            Type patchType = typeof(T);

            if (checkConfigEnabled && !KSPCommunityFixes.enabledPatches.Contains(patchType.Name))
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} not applied (disabled in Settings.cfg)");
                return;
            }

            T patch = (T)Activator.CreateInstance(patchType);

            if (!patch.IsVersionValid)
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} isn't applicable to this KSP version.");
                return;
            }

            try
            {
                patch.ApplyHarmonyPatch();
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} applied.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[KSPCommunityFixes] Patch {patchType.Name} not applied : {e}");
                return;
            }

            KSPCommunityFixes.patchInstances.Add(patchType, patch);
            patch.LoadData();
        }

        protected enum PatchMethodType
        {
            Prefix,
            Postfix,
            Transpiler,
            Finalizer
        }

        protected struct PatchInfo
        {
            public PatchMethodType patchMethodType;
            public MethodInfo originalMethod;
            public MethodInfo patchMethod;

            public PatchInfo(PatchMethodType patchMethodType, MethodInfo originalMethod, Type patchType)
            {
                this.patchMethodType = patchMethodType;
                this.originalMethod = originalMethod;
                string patchMethodName = originalMethod.DeclaringType.Name + "_" + originalMethod.Name + "_" + patchMethodType;
                patchMethod = AccessTools.Method(patchType, patchMethodName);
            }
        }

        protected abstract void ApplyPatches(ref List<PatchInfo> patches);

        protected virtual Version VersionMin => versionMinDefault;
        protected virtual Version VersionMax => versionMaxDefault;

        public bool IsVersionValid => KSPCommunityFixes.KspVersion >= VersionMin && KSPCommunityFixes.KspVersion <= VersionMax;

        private void ApplyHarmonyPatch()
        {
            List<PatchInfo> patches = new List<PatchInfo>();
            ApplyPatches(ref patches);

            foreach (PatchInfo patch in patches)
            {
                switch (patch.patchMethodType)
                {
                    case PatchMethodType.Prefix: KSPCommunityFixes.Harmony.Patch(patch.originalMethod, new HarmonyMethod(patch.patchMethod)); break;
                    case PatchMethodType.Postfix: KSPCommunityFixes.Harmony.Patch(patch.originalMethod, null, new HarmonyMethod(patch.patchMethod)); break;
                    case PatchMethodType.Transpiler: KSPCommunityFixes.Harmony.Patch(patch.originalMethod, null, null, new HarmonyMethod(patch.patchMethod)); break;
                    case PatchMethodType.Finalizer: KSPCommunityFixes.Harmony.Patch(patch.originalMethod, null, null, null, new HarmonyMethod(patch.patchMethod)); break;
                }
            }
        }

        private void LoadData()
        {
            string patchName = GetType().Name;
            string path = Path.Combine(KSPCommunityFixes.ModPath, pluginData, patchName + ".cfg");

            if (!File.Exists(path))
            {
                return;
            }

            ConfigNode node = ConfigNode.Load(path);

            if (node?.nodes[0] != null)
            {
                OnLoadData(node.nodes[0]);
            }
        }

        protected virtual void OnLoadData(ConfigNode node) { }

        public static void SaveData<T>(ConfigNode node) where T : BasePatch
        {
            string patchName = typeof(T).Name;
            string pluginDataPath = Path.Combine(KSPCommunityFixes.ModPath, pluginData);

            if (!Directory.Exists(pluginDataPath))
            {
                Directory.CreateDirectory(pluginDataPath);
            }

            ConfigNode topNode = new ConfigNode();
            topNode.AddNode(patchName, node);
            topNode.Save(Path.Combine(pluginDataPath, patchName + ".cfg"));
        }
    }
}
