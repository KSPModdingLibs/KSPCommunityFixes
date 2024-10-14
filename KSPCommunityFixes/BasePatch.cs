using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static MonoMod.Utils.Cil.CecilILGenerator;
using MethodBody = Mono.Cecil.Cil.MethodBody;

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

    /// <summary>
    /// When applied to an override patch method, the patch method body will be transpiled into patched method body,
    /// even in a debug configuration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal class TranspileInDebugAttribute : Attribute
    {
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

            bool patchApplied;
            try
            {
                patchApplied = patch.ApplyHarmonyPatch();
                
            }
            catch (Exception e)
            {
                patchApplied = false;
                Debug.LogException(e);
            }

            if (!patchApplied)
            {
                Debug.LogError($"[KSPCommunityFixes] Patch {patchType.Name} applied, error applying patch");
                KSPCommunityFixes.enabledPatches.Remove(patchType.Name);
                return;
            }
            else
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} applied.");
                KSPCommunityFixes.patchInstances.Add(patchType, patch);
            }

            
            patch.LoadData();
        }

        protected enum PatchMethodType
        {
            Prefix,
            Postfix,
            Transpiler,
            Finalizer,
            ReversePatch,
            /// <summary>
            /// The patch method body will be directly transpiled into the patched method body. <para/>
            /// This is functionally similar as a "return false" prefix, but avoid some call overhead with prefixes.<para/>
            /// It also mean the patched method cannot be transpiled by another patch, and that prefixes for that method will work as usual (which isn't the case with "return false" prefixes).<para/>
            /// The patch method must be static, it must have the same return type and the same arguments in the same order as the patched method.<para/>
            /// When the patched method is an instance method, the patch method must have the instance as an extra first argument (the name the argument doesn't matter).<para/>
            /// When building in a debug configuration, the patch method will be called from the transpiler, allowing to put breakpoints on it.<para/>
            /// In a release configuration, or if the patch has the [TranspileInDebug] attribute, the patch method is not called and can't be debugged.<para/> 
            /// </summary>
            Override
        }

        protected class PatchInfo
        {
            public PatchMethodType patchType;
            public MethodBase patchedMethod;
            public string patchMethodName;
            public MethodInfo patchMethod;
            public int patchPriority;
            public bool debugOverride = false;

            /// <summary>
            /// Define a harmony patch
            /// </summary>
            /// <param name="patchType">Patch type</param>
            /// <param name="patchedMethod">Method to be patched</param>
            /// <param name="patchClass">Class where the patch method is implemented. Usually "this"</param>
            /// <param name="patchMethodName">if null, will default to a method with the name "ClassToPatch_MethodToPatch_PatchType"</param>
            public PatchInfo(PatchMethodType patchType, MethodBase patchedMethod, string patchMethodName = null, int patchPriority = Priority.Normal)
            {
                this.patchType = patchType;
                this.patchedMethod = patchedMethod;
                this.patchMethodName = patchMethodName;
                this.patchPriority = patchPriority;


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

        private bool ApplyHarmonyPatch()
        {
            List<PatchInfo> patches = new List<PatchInfo>();
            ApplyPatches(patches);

            bool error = false;
            for (int i = 0; i < patches.Count; i++)
            {
                PatchInfo patch = patches[i];

                if (patch.patchedMethod == null)
                {
                    error = true;
                    Debug.LogWarning($"[{GetType()}] Target method not for found for {patch.patchType} patch at index {i}");
                    continue;
                }

                if (patch.patchMethodName == null)
                    patch.patchMethodName = patch.patchedMethod.DeclaringType.Name + "_" + patch.patchedMethod.Name + "_" + patch.patchType;

                patch.patchMethod = AccessTools.Method(GetType(), patch.patchMethodName);

                if (patch.patchMethod == null)
                {
                    error = true;
                    Debug.LogWarning($"[{GetType()}] Patch {patch.patchMethodName} implementation method not found for patched method {patch.patchedMethod.FullDescription()}");
                    continue;
                }

                if (patch.patchType == PatchMethodType.Override)
                {
#if DEBUG
                    patch.debugOverride = Attribute.GetCustomAttributes(patch.patchMethod, typeof(TranspileInDebugAttribute), false).Length == 0;
#endif
                    if (!overrides.TryAdd(patch.patchedMethod, patch))
                    {
                        error = true;
                        MethodInfo existingPatch = overrides[patch.patchedMethod].patchMethod;
                        Debug.LogWarning($"[{GetType()}] Override for method '{patch.patchedMethod.FullDescription()}' wasn't applied because it has already been overriden by patch '{existingPatch.DeclaringType.FullName}'");
                        continue;
                    }
                }
            }

            if (error)
                return false;

            foreach (PatchInfo patch in patches)
            {
                switch (patch.patchType)
                {
                    case PatchMethodType.Prefix:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchMethodType.Postfix:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchMethodType.Transpiler:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchMethodType.Finalizer:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchMethodType.ReversePatch:
                        KSPCommunityFixes.Harmony.CreateReversePatcher(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchMethodType.Override:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, new HarmonyMethod(overrideTranspiler, patch.patchPriority));
                        break;
                }
            }

            return true;
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
        protected virtual void OnLoadData(ConfigNode node)
        {
        }

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

        private static readonly MethodInfo overrideTranspiler = AccessTools.Method(typeof(BasePatch), nameof(OverrideTranspiler));

        /// <summary> key : patched method, value : replacement method </summary>
        private static Dictionary<MethodBase, PatchInfo> overrides = new Dictionary<MethodBase, PatchInfo>();

        private static IEnumerable<CodeInstruction> OverrideTranspiler(IEnumerable<CodeInstruction> _, ILGenerator patchGen, MethodBase __originalMethod)
        {
            if (!overrides.TryGetValue(__originalMethod, out PatchInfo patch))
                throw new Exception($"Could not find override method for patched method {__originalMethod.FullDescription()}");

            if (patch.debugOverride)
            {
                int instanceArg = __originalMethod.IsStatic ? 0 : 1;
                int paramCount = instanceArg + __originalMethod.GetParameters().Length;
                CodeInstruction[] debugIl = new CodeInstruction[paramCount + 2];

                int i;
                for (i = 0; i < paramCount; i++)
                {
                    switch (i)
                    {
                        case 0: debugIl[0] = new CodeInstruction(OpCodes.Ldarg_0); break;
                        case 1: debugIl[1] = new CodeInstruction(OpCodes.Ldarg_1); break;
                        case 2: debugIl[2] = new CodeInstruction(OpCodes.Ldarg_2); break;
                        case 3: debugIl[3] = new CodeInstruction(OpCodes.Ldarg_3); break;
                        default: debugIl[i] = new CodeInstruction(OpCodes.Ldarg_S, (byte)i); break;
                    }
                }

                debugIl[i++] = new CodeInstruction(OpCodes.Call, patch.patchMethod);
                debugIl[i] = new CodeInstruction(OpCodes.Ret);
                return debugIl;
            }

            List<CodeInstruction> il = PatchProcessor.GetOriginalInstructions(patch.patchMethod, out ILGenerator overrideGen);

            CecilILGenerator patchCecilGen = patchGen.GetProxiedShim<CecilILGenerator>();
            CecilILGenerator overrideCecilGen = overrideGen.GetProxiedShim<CecilILGenerator>();

            patchCecilGen._LabelInfos.Clear();
            foreach (KeyValuePair<Label, LabelInfo> item in overrideCecilGen._LabelInfos)
                patchCecilGen._LabelInfos.Add(item.Key, item.Value);

            patchCecilGen._Variables.Clear();
            foreach (KeyValuePair<LocalBuilder, VariableDefinition> item in overrideCecilGen._Variables)
                patchCecilGen._Variables.Add(item.Key, item.Value);

            patchCecilGen._LabelsToMark.Clear();
            patchCecilGen._LabelsToMark.AddRange(overrideCecilGen._LabelsToMark);

            patchCecilGen._ExceptionHandlersToMark.Clear();
            patchCecilGen._ExceptionHandlersToMark.AddRange(overrideCecilGen._ExceptionHandlersToMark);

            patchCecilGen._ExceptionHandlers.Clear();
            ExceptionHandlerChain[] exceptionHandlerChains = overrideCecilGen._ExceptionHandlers.ToArray();
            for (int i = exceptionHandlerChains.Length; i-- > 0;)
                patchCecilGen._ExceptionHandlers.Push(exceptionHandlerChains[i]);

            patchCecilGen._ILOffset = overrideCecilGen._ILOffset;
            patchCecilGen.labelCounter = overrideCecilGen.labelCounter;

            MethodBody patchBody = patchCecilGen.IL.Body;
            MethodBody overrideBody = overrideCecilGen.IL.Body;

            patchBody.ExceptionHandlers.Clear();
            patchBody.Variables.Clear();
            patchBody.ExceptionHandlers.AddRange(overrideBody.ExceptionHandlers);
            patchBody.Variables.AddRange(overrideBody.Variables);

            return il;
        }

        public static void XValueArgTest6(double a, double test, double c, double d, double e, double f)
        {
            XValueArgTest6(a, test, c, d, e, f);
        }

        public static void XValueArgTestLong(double a, double test, double c, double d, double e, double f, double g, double k, double z, double t, double p, double et, double po, double ty, double pp, double ks)
        {
            XValueArgTestLong(a, test, c, d, e, f, g, k, z, t, p, et, po, ty, pp, ks);
        }

        public static void XRefArgTest(double a, double test)
        {
            XRefArgTest(a, test);
        }

        public static void XOutValueArgTest(double a, out double test)
        {
            XOutValueArgTest(a,out test);
        }

        public static void XRefValueArgTest(double a, ref double test)
        {
            XRefValueArgTest(a,ref test);
        }

        public static void XOutRefArgTest(double a, out string test)
        {
            XOutRefArgTest(a,out test);
        }

        public static void XRefRefArgTest(double a, ref string test)
        {
            XRefRefArgTest(a,ref test);
        }

        public static void XParamArgTest(params double[] args)
        {
            XParamArgTest(args);
        }
    }
}
