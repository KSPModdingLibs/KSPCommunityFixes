using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PatchPriority : Attribute
    {
        private int order;

        public int Order
        {
            get => order;
            set => order = value;
        }

        public PatchPriority()
        {
            order = 0;
        }
    }

    public abstract class BasePatch
    {
        public static readonly string pluginData = "PluginData";
        private static readonly Version versionMinDefault = new Version(1, 12, 0);
        private static readonly Version versionMaxDefault = new Version(1, 12, 99);

        public static void Patch(Type patchType)
        {
            BasePatch patch = (BasePatch)Activator.CreateInstance(patchType);

            if (!patch.CanApplyPatch(out string reason))
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} not applied ({reason})");
                KSPCommunityFixes.enabledPatches.Remove(patchType.Name);
                return;
            }

            if (!patch.IgnoreConfig && !KSPCommunityFixes.enabledPatches.Contains(patchType.Name))
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} not applied (disabled in Settings.cfg)");
                return;
            }

            if (!patch.IsVersionValid)
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} isn't applicable to this KSP version.");
                KSPCommunityFixes.enabledPatches.Remove(patchType.Name);
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
                KSPCommunityFixes.enabledPatches.Remove(patchType.Name);
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
            Finalizer,
            ReversePatch
        }

        protected class PatchInfo
        {
            public PatchMethodType patchType;
            public MethodBase patchedMethod;
            public MethodInfo patchMethod;
            public int patchPriority;

            /// <summary>
            /// Define a harmony patch
            /// </summary>
            /// <param name="patchType">Patch type</param>
            /// <param name="patchedMethod">Method to be patched</param>
            /// <param name="patchClass">Class where the patch method is implemented. Usually "this"</param>
            /// <param name="patchMethodName">if null, will default to a method with the name "ClassToPatch_MethodToPatch_PatchType"</param>
            public PatchInfo(PatchMethodType patchType, MethodBase patchedMethod, BasePatch patchClass, string patchMethodName = null, int patchPriority = Priority.Normal)
            {
                if (patchedMethod == null)
                    throw new Exception($"{patchType} target method not found in {patchClass}");

                this.patchType = patchType;
                this.patchedMethod = patchedMethod;
                this.patchPriority = patchPriority;

                if (patchMethodName == null)
                    patchMethodName = this.patchedMethod.DeclaringType.Name + "_" + this.patchedMethod.Name + "_" + this.patchType;

                patchMethod = AccessTools.Method(patchClass.GetType(), patchMethodName);

                if (patchMethod == null)
                    throw new Exception($"{patchMethodName} implementation method not found in {patchClass}");
            }
        }

        /// <summary>
        /// Called during loading, after ModuleManager has patched the game DataBase, and before part compilation.
        /// Add PatchInfo entries to the patches list to appply harmony patches
        /// </summary>
        /// <param name="patches"></param>
        protected abstract void ApplyPatches(List<PatchInfo> patches);

        /// <summary>
        /// Override this to define the min KSP version for witch this patch applies. Defaults to KSP 1.12.0
        /// </summary>
        protected virtual Version VersionMin => versionMinDefault;

        /// <summary>
        /// Override this to define the max KSP version for witch this patch applies. Defaults to KSP 1.12.99
        /// </summary>
        protected virtual Version VersionMax => versionMaxDefault;

        public bool IsVersionValid => KSPCommunityFixes.KspVersion >= VersionMin && KSPCommunityFixes.KspVersion <= VersionMax;

        private void ApplyHarmonyPatch()
        {
            List<PatchInfo> patches = new List<PatchInfo>();
            ApplyPatches(patches);

            foreach (PatchInfo patch in patches)
            {
                switch (patch.patchType)
                {
                    case PatchMethodType.Prefix: KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority)); break;
                    case PatchMethodType.Postfix: KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority)); break;
                    case PatchMethodType.Transpiler: KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority)); break;
                    case PatchMethodType.Finalizer: KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority)); break;
                    case PatchMethodType.ReversePatch: KSPCommunityFixes.Harmony.CreateReversePatcher(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority)); break;
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

        /// <summary>
        /// Override this to add custom checks for applying a patch or not
        /// </summary>
        /// <param name="reason">string printed in the log if the patch isn't applied</param>
        protected virtual bool CanApplyPatch(out string reason)
        {
            reason = null;
            return true;
        }

        /// <summary>
        /// Override to true to have this patch always applied regardless of the Settings.cfg flags
        /// </summary>
        protected virtual bool IgnoreConfig => false;

        /// <summary>
        /// Called after a the patch has been applied during loading
        /// Get custom data that was saved using SaveData()
        /// </summary>
        protected virtual void OnLoadData(ConfigNode node) { }

        /// <summary>
        /// Call this to save custom patch-specific data that will be reloaded on the next KSP launch (see OnLoadData())
        /// </summary>
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
