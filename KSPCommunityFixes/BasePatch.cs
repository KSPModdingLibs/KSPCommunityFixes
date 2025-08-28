//#define FORCE_OVERRIDE
//#define DEBUG_OVERRIDE

using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Utils.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static MonoMod.Utils.Cil.CecilILGenerator;
using Label = System.Reflection.Emit.Label;
using OpCode = System.Reflection.Emit.OpCode;
using Version = System.Version;

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

    /// <summary>
    /// When applied to a class derived from <see cref="BasePatch"/>, the patch won't be automatically applied.<para/>
    /// To apply the patch, call <see cref="BasePatch.Patch"/>. Note that if that call happens before ModuleManager
    /// has patched the configs (ie, before part compilation), <see cref="BasePatch.IgnoreConfig"/> must be overriden
    /// to return <see langword="true"/>, or the patch won't be applied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal class ManualPatchAttribute : Attribute
    {
    }

    public abstract class BasePatch
    {
        public static readonly string pluginData = "PluginData";
        private static readonly Version versionMinDefault = new Version(1, 12, 0);
        private static readonly Version versionMaxDefault = new Version(1, 12, 99);

        private List<PatchInfo> patches = new List<PatchInfo>(4);

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
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} not applied (not enabled in Settings.cfg)");
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
                Debug.LogError($"[KSPCommunityFixes] Patch {patchType.Name} not applied, error applying patch");
                KSPCommunityFixes.enabledPatches.Remove(patchType.Name);
                return;
            }
            else
            {
                Debug.Log($"[KSPCommunityFixes] Patch {patchType.Name} applied.");
                KSPCommunityFixes.patchInstances.Add(patchType, patch);
            }

            
            patch.LoadData();
            patch.OnPatchApplied();
        }

        protected enum PatchType
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
            /// When the patched method is an instance method, the patch method must have the instance as an extra first argument (the name of the argument doesn't matter).<para/>
            /// When building in a debug configuration, the patch method will be called from the transpiler, allowing to put breakpoints on it.<para/>
            /// In a release configuration, or if the patch has the [TranspileInDebug] attribute, the patch method is not called and can't be debugged.<para/> 
            /// </summary>
            Override
        }

        protected class PatchInfo
        {
            public PatchType patchType;
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
            public PatchInfo(PatchType patchType, MethodBase patchedMethod, string patchMethodName = null, int patchPriority = Priority.Normal)
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
        protected abstract void ApplyPatches();

        /// <summary>
        /// Override this to define the min KSP version for witch this patch applies. Defaults to KSP 1.12.0
        /// </summary>
        protected virtual Version VersionMin => versionMinDefault;

        /// <summary>
        /// Override this to define the max KSP version for witch this patch applies. Defaults to KSP 1.12.99
        /// </summary>
        protected virtual Version VersionMax => versionMaxDefault;

        protected void AddPatch(PatchType patchType, MethodBase patchedMethod, string patchMethodName = null, int patchPriority = Priority.Normal)
        {
            patches.Add(new PatchInfo(patchType, patchedMethod, patchMethodName, patchPriority));
        }

        protected void AddPatch(PatchType patchType, Type patchedMethodType, string patchedMethodName, string patchMethodName = null, int patchPriority = Priority.Normal)
        {
            patches.Add(new PatchInfo(patchType, AccessTools.Method(patchedMethodType, patchedMethodName), patchMethodName, patchPriority));
        }

        protected void AddPatch(PatchType patchType, Type patchedMethodType, string patchedMethodName, Type[] patchedMethodArgs, string patchMethodName = null, int patchPriority = Priority.Normal)
        {
            patches.Add(new PatchInfo(patchType, AccessTools.Method(patchedMethodType, patchedMethodName, patchedMethodArgs), patchMethodName, patchPriority));
        }

        public bool IsVersionValid => KSPCommunityFixes.KspVersion >= VersionMin && KSPCommunityFixes.KspVersion <= VersionMax;

        private bool ApplyHarmonyPatch()
        {
            ApplyPatches();

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

                if (patch.patchType == PatchType.Override)
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
                    case PatchType.Prefix:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchType.Postfix:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchType.Transpiler:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchType.Finalizer:
                        KSPCommunityFixes.Harmony.Patch(patch.patchedMethod, null, null, null, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchType.ReversePatch:
                        KSPCommunityFixes.Harmony.CreateReversePatcher(patch.patchedMethod, new HarmonyMethod(patch.patchMethod, patch.patchPriority));
                        break;
                    case PatchType.Override:
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
        /// Called after a the patch has been applied during loading, if the patch has an associated cfg.
        /// Get custom data that was saved using SaveData()
        /// </summary>
        protected virtual void OnLoadData(ConfigNode node) { }

        /// <summary>
        /// Called after the patch has been applied and OnLoadData has been called
        /// </summary>
        protected virtual void OnPatchApplied() { }

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
        private static readonly Dictionary<MethodBase, PatchInfo> overrides = new Dictionary<MethodBase, PatchInfo>();

        private static IEnumerable<CodeInstruction> OverrideTranspiler(IEnumerable<CodeInstruction> _, ILGenerator patchGen, MethodBase __originalMethod)
        {
            if (!overrides.TryGetValue(__originalMethod, out PatchInfo patch))
                throw new Exception($"Could not find override method for patched method {__originalMethod.FullDescription()}");

            // When we want to be able to debug the override method, we generate a transpiler calling it :
#if !FORCE_OVERRIDE
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
#endif

            // Otherwise, we want to return all instructions of the override method.
            // We call an Harmony library method (GetOriginalInstructions) that return the IL and a matching ILGenerator for
            // our override. This is the same method Harmony use internally to weave patches together.
            // But the difficulty here is that we need correctly defined variables and labels in the IlGenerator instance
            // Harmony will use (and that is passed as an argument), so we need to copy all that stuff from the IlGenerator
            // for our override method to the ILGenertor instance that Harmony will actually use.
            // This gets quite complicated because the ILGenerator isn't actually an ILGenerator from reflection, but a runtime
            // generated replacement from MonoMod. Long story short, copying all the stuff require accessing all that internal
            // MonoMod generator stuff, which we publicize for convenience.
            // This however mean that this is very likely to break on updating MonoMod/Harmony, and as a matter of fact, will
            // definitely break if we start using Harmony 2.3+, which is based on a new major version of MonoMod where all this
            // stuff has been heavily refactored.
            List<CodeInstruction> overrideIl = PatchProcessor.GetOriginalInstructions(patch.patchMethod, out ILGenerator overrideGen);
            PatchProcessor.GetOriginalInstructions(__originalMethod, out ILGenerator originalGen);

            CecilILGenerator patchCecilGen = patchGen.GetProxiedShim<CecilILGenerator>();
            CecilILGenerator overrideCecilGen = overrideGen.GetProxiedShim<CecilILGenerator>();
            CecilILGenerator originalCecilGen = originalGen.GetProxiedShim<CecilILGenerator>();

            int[] modifiedOverrideLabelIndices = new int[overrideCecilGen.labelCounter];
            foreach (KeyValuePair<Label, LabelInfo> item in overrideCecilGen._LabelInfos)
            {
                Label newLabel = patchGen.DefineLabel();
                modifiedOverrideLabelIndices[item.Key.label] = newLabel.label;
            }

            int originalVariableCount = originalCecilGen._Variables.Count;
            int variableOffset = patchCecilGen._Variables.Count;
            Dictionary<int, int> modifiedOverrideVariableIndexes = new Dictionary<int, int>(overrideCecilGen._Variables.Count);

            LocalBuilder[] patchLocalBuildersByLocalIndex = new LocalBuilder[patchCecilGen._Variables.Count];
            foreach (LocalBuilder key in patchCecilGen._Variables.Keys)
                patchLocalBuildersByLocalIndex[key.position] = key;

            foreach (KeyValuePair<LocalBuilder, VariableDefinition> variableDef in overrideCecilGen._Variables)
            {
                int currentIndex = variableDef.Value.index;
                
                if (currentIndex < originalVariableCount)
                {
                    // if override variable index is within the range of the original variables, keep the index
                    // but update the variable in the patch generator.
                    LocalBuilder patchVariableKey = patchLocalBuildersByLocalIndex[currentIndex];
                    LocalBuilder overrideLocalBuilder = variableDef.Key;
                    patchVariableKey.position = overrideLocalBuilder.position;
                    patchVariableKey.type = overrideLocalBuilder.type;
                    patchVariableKey.is_pinned = overrideLocalBuilder.is_pinned;
                    patchCecilGen._Variables[patchVariableKey] = variableDef.Value;
                    patchCecilGen.IL.Body.Variables[currentIndex] = variableDef.Value;
                }
                else
                {
                    // else, update the index to the next unused, and add the variable to the patch generator
                    int newIndex = variableOffset + modifiedOverrideVariableIndexes.Count;
                    LocalBuilder newBuilderRef = patchGen.DeclareLocal(variableDef.Key.LocalType, variableDef.Key.IsPinned);
                    if (currentIndex != newBuilderRef.position)
                        modifiedOverrideVariableIndexes.Add(currentIndex, newIndex);
                }
            }

            for (int i = overrideIl.Count; i-- > 0;)
            {
                CodeInstruction instruction = overrideIl[i];
                for (int j = instruction.labels.Count; j-- > 0;)
                {
                    Label jumpLocation = instruction.labels[j];
                    jumpLocation.label = modifiedOverrideLabelIndices[jumpLocation.label];
                    instruction.labels[j] = jumpLocation;
                }

                if (instruction.operand is Label jumpTo)
                {
                    jumpTo.label = modifiedOverrideLabelIndices[jumpTo.label];
                    instruction.operand = jumpTo;
                }

                if (instruction.opcode == OpCodes.Switch && instruction.operand is Label[] jumpTable)
                    for (int j = jumpTable.Length; j-- > 0;)
                        jumpTable[j].label = modifiedOverrideLabelIndices[jumpTable[j].label];

                OpCode opCode = instruction.opcode;
                if (opCode == OpCodes.Stloc || opCode == OpCodes.Stloc_S)
                {
                    int currentVariableIdx;
                    if (instruction.operand is LocalBuilder localBuilder)
                        currentVariableIdx = localBuilder.LocalIndex;
                    else
                        currentVariableIdx = (int)instruction.operand;

                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(currentVariableIdx, currentVariableIdx);

                    if (newIndex > byte.MaxValue && opCode == OpCodes.Stloc_S)
                        instruction.opcode = OpCodes.Stloc;

                    if (opCode == OpCodes.Stloc_S)
                        instruction.operand = (byte)newIndex;
                    else
                        instruction.operand = newIndex;
                }
                else if (opCode == OpCodes.Stloc_0)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(0, 0);
                    SetStlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Stloc_1)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(1, 1);
                    SetStlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Stloc_2)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(2, 2);
                    SetStlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Stloc_3)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(3, 3);
                    SetStlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Ldloca || opCode == OpCodes.Ldloca_S || opCode == OpCodes.Ldloc || opCode == OpCodes.Ldloc_S)
                {
                    int currentVariableIdx;
                    if (instruction.operand is LocalBuilder localBuilder)
                        currentVariableIdx = localBuilder.LocalIndex;
                    else
                        currentVariableIdx = (int)instruction.operand;

                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(currentVariableIdx, currentVariableIdx);

                    if (newIndex > byte.MaxValue)
                    {
                        if (opCode == OpCodes.Ldloca_S)
                            instruction.opcode = OpCodes.Ldloca;
                        else if (opCode == OpCodes.Stloc_S)
                            instruction.opcode = OpCodes.Stloc;
                    }

                    if (opCode == OpCodes.Stloc_S || opCode == OpCodes.Ldloca_S)
                        instruction.operand = (byte)newIndex;
                    else
                        instruction.operand = newIndex;
                }
                else if (opCode == OpCodes.Ldloc_0)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(0, 0);
                    SetLdlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Ldloc_1)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(1, 1);
                    SetLdlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Ldloc_2)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(2, 2);
                    SetLdlocForIndex(instruction, newIndex);
                }
                else if (opCode == OpCodes.Ldloc_3)
                {
                    int newIndex = modifiedOverrideVariableIndexes.GetValueOrDefault(3, 3);
                    SetLdlocForIndex(instruction, newIndex);
                }
            }

            return overrideIl;
        }

        private static void SetStlocForIndex(CodeInstruction instruction, int varIndex)
        {
            if (varIndex == 0)
            {
                instruction.opcode = OpCodes.Stloc_0;
            }
            else if (varIndex == 1)
            {
                instruction.opcode = OpCodes.Stloc_1;
            }
            else if (varIndex == 2)
            {
                instruction.opcode = OpCodes.Stloc_2;
            }
            else if (varIndex == 3)
            {
                instruction.opcode = OpCodes.Stloc_3;
            }
            else if (varIndex < byte.MaxValue)
            {
                instruction.opcode = OpCodes.Stloc_S;
                instruction.operand = (byte)varIndex;
            }
            else
            {
                instruction.opcode = OpCodes.Stloc;
                instruction.operand = varIndex;
            }
        }

        private static void SetLdlocForIndex(CodeInstruction instruction, int varIndex)
        {
            if (varIndex == 0)
            {
                instruction.opcode = OpCodes.Ldloc_0;
            }
            else if (varIndex == 1)
            {
                instruction.opcode = OpCodes.Ldloc_1;
            }
            else if (varIndex == 2)
            {
                instruction.opcode = OpCodes.Ldloc_2;
            }
            else if (varIndex == 3)
            {
                instruction.opcode = OpCodes.Ldloc_3;
            }
            else if (varIndex < byte.MaxValue)
            {
                instruction.opcode = OpCodes.Ldloc_S;
                instruction.operand = (byte)varIndex;
            }
            else
            {
                instruction.opcode = OpCodes.Ldloc;
                instruction.operand = varIndex;
            }
        }
    }

#if DEBUG_OVERRIDE
    public class TranspilerTest : BasePatch
    {
        protected override bool IgnoreConfig => true;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(TranspilerTest), nameof(Method1));
            AddPatch(PatchType.Postfix, typeof(TranspilerTest), nameof(Method1));
            AddPatch(PatchType.Override, typeof(TranspilerTest), nameof(Method1));
        }

        protected override void OnPatchApplied()
        {
            ResultVar result = Method1();
            Debug.Log($"Method1 call result : {result.result}");
        }

        private InstanceField testVarInstance = new InstanceField() {result = "instanceFieldValue"};

        public ResultVar Method1()
        {
            Debug.Log("Hello from original");

            if (testVarInstance.result == string.Empty)
                return null;

            try
            {
                LocalVarOrig localVar = new LocalVarOrig();
                throw new Exception();
            }
            catch (Exception e)
            {
                Debug.Log("Catched exception from original");
            }

            if (testVarInstance.result == string.Empty)
                return null;

            if (testVarInstance.result == string.Empty)
                return null;

            try
            {
                LocalVarOrig localVar = new LocalVarOrig();
                throw new Exception();
            }
            catch (Exception e)
            {
                Debug.Log("Catched exception from original");
            }

            return new ResultVar() { result = "origResult" };
        }

        [TranspileInDebug]
        public static ResultVar TranspilerTest_Method1_Override(TranspilerTest __instance)
        {
            if (__instance.testVarInstance != null)
                Debug.Log("Hello from override 1");

            try
            {
                LocalVarOverride localVar = new LocalVarOverride();
                throw new Exception();
            }
            catch (Exception e)
            {
                Debug.Log("Catched exception from override");
            }

            return new ResultVar() { result = "overrideResult" };
        }

        public static bool TranspilerTest_Method1_Prefix(TranspilerTest __instance, out StateVar __state, ref ResultVar __result)
        {
            Debug.Log("Hello from prefix 1");
            __result = new ResultVar() { result = "prefixResult" };
            __state = new StateVar();
            __state.state = "good state !";
            return true;
        }

        public static void TranspilerTest_Method1_Postfix(TranspilerTest __instance, InstanceField ___testVarInstance, StateVar __state, ref ResultVar __result)
        {
            Debug.Log($"Hello from postfix 1 : {__state.state}");
            __result.result += "modifiedByPostfix";
        }

        public static IEnumerable<CodeInstruction> TranspilerTest_Method1_Transpiler(IEnumerable<CodeInstruction> original, ILGenerator ilGen)
        {
            Label label = ilGen.DefineLabel();
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TranspilerTest), nameof(Throw))).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TranspilerTest), nameof(Log))).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock));
            yield return new CodeInstruction(OpCodes.Leave_S, label).WithBlocks(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            yield return new CodeInstruction(OpCodes.Ret).WithLabels(label);
        }

        public static void Log() => Debug.Log("Exception catched !");
        public static void Throw() => throw new Exception();

        public class InstanceField { public string result; }
        public class ResultVar { public string result; }
        public class LocalVarOrig { }
        public class LocalVarPrefix { }
        public class LocalVarPostfix { }
        public class LocalVarOverride { }
        public class StateVar { public string state; }
    }
#endif
}
