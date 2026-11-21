using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Contracts;
using Expansions.Missions.Runtime;
using Strategies;

namespace KSPCommunityFixes.BugFixes
{
    // Some stock ScenarioModules only start a coroutine in OnLoad(), and read their state from the
    // save a frame later, when that coroutine resumes. On a scene load that frame can last several
    // seconds. Their OnSave() doesn't check for this, so a game saved in the meantime gets the empty
    // state the module has before loading. Until the load has completed, this patch saves a copy of
    // the node that is waiting to be loaded instead.
    internal class ScenarioModuleEarlySave : BasePatch
    {
        private class SavedEntries
        {
            public readonly string[] nodes;
            public readonly string[] values;

            public SavedEntries(string[] nodes, string[] values)
            {
                this.nodes = nodes;
                this.values = values;
            }
        }

        // The patched types, and what each type's OnSave() writes. Each type must implement OnLoad()
        // by starting a coroutine from a private OnLoadRoutine(ConfigNode) method. The "name" and
        // "scene" values are written by ScenarioModule.Save() and must not be listed.
        static readonly Dictionary<Type, SavedEntries> patchedTypes = new Dictionary<Type, SavedEntries>
        {
            { typeof(ContractSystem), new SavedEntries(new[] { "WEIGHTS", "CONTRACTS" }, new[] { "update", "version" }) },
            { typeof(StrategySystem), new SavedEntries(new[] { "STRATEGIES" }, new string[0]) },
            { typeof(MissionSystem), new SavedEntries(new[] { "MISSIONS" }, new[] { "cameraLockOptions", "cameraLockMode" }) },
        };

        // The node each module was given to load and hasn't read yet. The nodes are the ones held by
        // the ProtoScenarioModules, don't modify them.
        static readonly Dictionary<ScenarioModule, ConfigNode> pendingNodes = new Dictionary<ScenarioModule, ConfigNode>();

        protected override void ApplyPatches()
        {
            foreach (Type type in patchedTypes.Keys)
            {
                AddPatch(PatchType.Postfix, type, "OnLoadRoutine", nameof(ScenarioModule_OnLoadRoutine_Postfix));
                AddPatch(PatchType.Prefix, type, nameof(ScenarioModule.OnSave), nameof(ScenarioModule_OnSave_Prefix));
            }
        }

        static void ScenarioModule_OnLoadRoutine_Postfix(ScenarioModule __instance, object[] __args, ref IEnumerator __result)
        {
            RemoveDestroyedModules();
            ConfigNode node = (ConfigNode)__args[0];
            pendingNodes[__instance] = node;
            __result = TrackLoad(__instance, node, __result);
        }

        static IEnumerator TrackLoad(ScenarioModule instance, ConfigNode node, IEnumerator loadRoutine)
        {
            try
            {
                while (loadRoutine.MoveNext())
                    yield return loadRoutine.Current;
            }
            finally
            {
                // Also reached when the load throws, in which case the node may have been partially
                // consumed and is no longer usable.
                if (pendingNodes.TryGetValue(instance, out ConfigNode pendingNode) && ReferenceEquals(pendingNode, node))
                    pendingNodes.Remove(instance);
            }
        }

        // A module destroyed before its load has completed stops the coroutine without running its
        // finally block.
        static void RemoveDestroyedModules()
        {
            List<ScenarioModule> destroyed = null;
            foreach (ScenarioModule module in pendingNodes.Keys)
                if (module.IsNullOrDestroyed())
                    (destroyed ??= new List<ScenarioModule>()).Add(module);

            if (destroyed != null)
                foreach (ScenarioModule module in destroyed)
                    pendingNodes.Remove(module);
        }

        static bool ScenarioModule_OnSave_Prefix(ScenarioModule __instance, object[] __args, MethodBase __originalMethod)
        {
            if (!pendingNodes.TryGetValue(__instance, out ConfigNode pendingNode))
                return true;

            SavedEntries saved = patchedTypes[__originalMethod.DeclaringType];
            ConfigNode gameNode = (ConfigNode)__args[0];

            // The nodes must be copies, the load is still going to read the originals.
            foreach (string name in saved.nodes)
            {
                ConfigNode node = pendingNode.GetNode(name);
                if (node != null)
                    gameNode.AddNode(node.CreateCopy());
            }

            foreach (string name in saved.values)
            {
                string value = pendingNode.GetValue(name);
                if (value != null)
                    gameNode.AddValue(name, value);
            }

            return false;
        }
    }
}
