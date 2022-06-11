using HarmonyLib;
using MultipleModuleInPartAPI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes
{
    // Overview :
    // When a ProtoPartSnapshot is loaded to a Part, we compare the protomodule and loaded module lists to detect
    // a configuration change. If a configuration change is detected, our "module reconnection" logic works under
    // the assumption that even though a module index might have changed, the nth protomodule of a specific type
    // is still the nth module of that type in the modified config. The stock reconnection logic doesn't handle
    // correctly that case (multiple modules of the same type), causing all protomodules of the same type to be
    // loaded in the first found module of that type, causing the persisted state of those modules to be either lost 
    // or worst, applied to the wrong module instance. There is a similar issue with ShipConstruct loading, so
    // we also patch the ShipConstruct.LoadShip() main overload.

    // Note that what we are doing isn't entirely fail-proof either, but it will massively reduce the probablibilty
    // of ending with a borked save / ship. Specifically, it will mess things up if the config change is inserting
    // or removing a module that exists in multiple occurences, and isn't the last module of that type.
    // There is no way to handle that case without requiring manually defining a per-part persisted unique ID for
    // modules that can exist in multiple occurence.

    public class ModuleIndexingMismatch : BasePatch
    {
        private const string VALUENAME_MODULEPARTCONFIGID = "modulePartConfigId";
        private static readonly HashSet<string> multiModules = new HashSet<string>();

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            // ConfigurePart() was added in 1.11, by stripping a portion of the 1.10 ProtoPartSnapshot.Load()
            // method in a separate method.
            if (KSPCommunityFixes.KspVersion < new Version(1, 11, 0))
            {
                patches.Add(new PatchInfo(
                    PatchMethodType.Transpiler,
                    AccessTools.Method(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.Load)),
                    this));
            }
            else
            {
                patches.Add(new PatchInfo(
                    PatchMethodType.Transpiler,
                    AccessTools.Method(typeof(ProtoPartSnapshot), "ConfigurePart"),
                    this));
            }

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ShipConstruct), "LoadShip", new Type[] { typeof(ConfigNode), typeof(uint), typeof(bool), typeof(string).MakeByRefType() }),
                this));

            Type partModuleType = typeof(PartModule);
            Type multiModuleType = typeof(IMultipleModuleInPart);

            // note : do not use AppDomain.CurrentDomain.GetAssemblies(), as in case some plugin was missing
            // a reference, that missing reference assembly will be included even though it is invalid, leading
            // to a ReflectionTypeLoadException when doing anything with it such as calling GetTypes()
            foreach (AssemblyLoader.LoadedAssembly loadedAssembly in AssemblyLoader.loadedAssemblies)
            {
                foreach (Type type in loadedAssembly.assembly.GetTypes())
                {
                    if (multiModuleType.IsAssignableFrom(type) && partModuleType.IsAssignableFrom(type))
                    {
                        multiModules.Add(type.Name);
                    }
                }
            }

            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
        }

        private void OnPartLoaderLoaded()
        {
            HashSet<string> moduleConfigIds = new HashSet<string>(10);
            int multiModuleCount;
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                moduleConfigIds.Clear();
                multiModuleCount = 0;
                foreach (PartModule module in availablePart.partPrefab.Modules)
                {
                    if (module is IMultipleModuleInPart multiModule)
                    {
                        string moduleId = multiModule.ModulePartConfigId;

                        if (string.IsNullOrEmpty(moduleId))
                        {
                            if (multiModuleCount != 0)
                            {
                                Debug.LogWarning($"There are multiple instances of {module.moduleName} in {availablePart.name}, but the instance n°{multiModuleCount + 1} has no {nameof(IMultipleModuleInPart.ModulePartConfigId)} defined");
                                Debug.LogWarning($"The module risk being mismatched when loading its persisted state following a config change...");
                            }
                        }
                        else if (moduleConfigIds.Contains(moduleId))
                        {
                            Debug.LogWarning($"A module {module.moduleName} in {availablePart.name} has the same {nameof(IMultipleModuleInPart.ModulePartConfigId)} ({moduleId}) as another module.");
                            Debug.LogWarning($"The module risk being mismatched when loading its persisted state following a config change...");
                        }
                        else
                        {
                            moduleConfigIds.Add(moduleId);
                        }

                        multiModuleCount++;
                    }
                }
            }
        }


        // This works by replacing the original loop iterating on the protopart protomodules by a call to our LoadModules method.
        // It does the same thing, but verify and pre-compute the indexes before calling the ProtoPartModuleSnapshot.Load() method.
        // The portion of code being replaced is :
        // int moduleIndex = 0;
        // int i = 0;
        // for (int count = modules.Count; i < count; i++)
        //      modules[i].Load(partToConfigure, ref moduleIndex);
        static IEnumerable<CodeInstruction> ProtoPartSnapshot_Load_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo Part_OnLoad = AccessTools.Method(typeof(Part), nameof(Part.OnLoad), new[] { typeof(ConfigNode) });
            FieldInfo ProtoPartSnapshot_partRef = AccessTools.Field(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.partRef));
            FieldInfo ProtoPartSnapshot_resources = AccessTools.Field(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.resources));
            MethodInfo LoadModules = AccessTools.Method(typeof(ModuleIndexingMismatch), nameof(ModuleIndexingMismatch.LoadModules));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count - 4; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, Part_OnLoad) && code[i + 1].opcode == OpCodes.Ldc_I4_0)
                {
                    code[i + 1].opcode = OpCodes.Ldarg_0;
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    code.Insert(i + 3, new CodeInstruction(OpCodes.Ldfld, ProtoPartSnapshot_partRef));
                    code.Insert(i + 4, new CodeInstruction(OpCodes.Call, LoadModules));

                    // remove the original module loading loop (end with Blt_S)
                    bool end = false;
                    for (int j = i + 5; j < code.Count; j++)
                    {
                        if (code[j].opcode == OpCodes.Blt_S)
                            end = true;

                        // safety check : if this match we are way too far and something is very wrong
                        if (ReferenceEquals(code[j].operand, ProtoPartSnapshot_resources))
                            return instructions;

                        code[j].opcode = OpCodes.Nop;
                        code[j].operand = null;

                        if (end)
                            break;
                    }

                    break;
                }
            }

            return code;
        }

        static IEnumerable<CodeInstruction> ProtoPartSnapshot_ConfigurePart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo Part_OnLoad = AccessTools.Method(typeof(Part), nameof(Part.OnLoad), new[] { typeof(ConfigNode) });
            FieldInfo ProtoPartSnapshot_partRef = AccessTools.Field(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.partRef));
            FieldInfo ProtoPartSnapshot_resources = AccessTools.Field(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.resources));
            MethodInfo LoadModules = AccessTools.Method(typeof(ModuleIndexingMismatch), nameof(ModuleIndexingMismatch.LoadModules));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count - 4; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, Part_OnLoad) && code[i + 1].opcode == OpCodes.Ldc_I4_0)
                {
                    code[i + 1].opcode = OpCodes.Ldarg_0;
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_2));
                    code.Insert(i + 3, new CodeInstruction(OpCodes.Call, LoadModules));

                    // remove the original module loading loop (end with Blt_S)
                    bool end = false;
                    for (int j = i + 4; j < code.Count; j++)
                    {
                        if (code[j].opcode == OpCodes.Blt_S)
                            end = true;

                        // safety check : if this match we are way too far and something is very wrong
                        if (ReferenceEquals(code[j].operand, ProtoPartSnapshot_resources))
                            return instructions;

                        code[j].opcode = OpCodes.Nop;
                        code[j].operand = null;

                        if (end)
                            break;
                    }

                    break;
                }
            }

            return code;
        }

        static void LoadModules(ProtoPartSnapshot protoPart, Part part)
        {
            int protoModuleCount = protoPart.modules.Count;
            int partModuleCount = part.Modules.Count;
            bool inSync = protoModuleCount == partModuleCount;

            if (inSync)
            {
                for (int i = 0; i < protoModuleCount; i++)
                {
                    if (part.Modules[i].moduleName != protoPart.modules[i].moduleName)
                    {
                        inSync = false;
                        break;
                    }
                }
            }

            if (inSync)
            {
                for (int i = 0; i < protoModuleCount; i++)
                {
                    int moduleIndex = i;

                    if (part.Modules[i] is IMultipleModuleInPart multiModule)
                    {
                        string protoModuleId = protoPart.modules[i].moduleValues.GetValue(VALUENAME_MODULEPARTCONFIGID);
                        if (!string.IsNullOrEmpty(protoModuleId) && multiModule.ModulePartConfigId != protoModuleId)
                        {
                            moduleIndex = -1;
                            for (int j = 0; j < partModuleCount; j++)
                            {
                                if (part.Modules[j] is IMultipleModuleInPart otherMultiModule && otherMultiModule.ModulePartConfigId == protoModuleId)
                                {
                                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoPart.modules[i].moduleName}\" with {VALUENAME_MODULEPARTCONFIGID}={protoModuleId} at index [{i}] moved to index [{j}]");
                                    moduleIndex = j;
                                    break;
                                }
                            }

                            if (moduleIndex == -1)
                            {
                                Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoPart.modules[i].moduleName}\" with {VALUENAME_MODULEPARTCONFIGID}={protoModuleId} at index [{i}] has been removed, no matching module in the part config");
                                continue;
                            }
                        }

                    }

                    protoPart.modules[i].Load(part, ref moduleIndex);
                }
                return;
            }

            Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Part \"{protoPart.partName}\" configuration has changed. Synchronizing persisted modules...");
            ProtoPartModuleSnapshot[] foundModules = new ProtoPartModuleSnapshot[partModuleCount];

            for (int i = 0; i < protoModuleCount; i++)
            {
                ProtoPartModuleSnapshot protoModule = protoPart.modules[i];

                if (multiModules.Contains(protoModule.moduleName))
                {
                    string protoModuleId = protoModule.moduleValues.GetValue(VALUENAME_MODULEPARTCONFIGID);

                    if (!string.IsNullOrEmpty(protoModuleId))
                    {
                        bool multiFound = false;
                        for (int j = 0; j < partModuleCount; j++)
                        {
                            if (part.Modules[j] is IMultipleModuleInPart multiModule && multiModule.ModulePartConfigId == protoModuleId)
                            {
                                int moduleIndex = j;
                                protoModule.Load(part, ref moduleIndex);
                                foundModules[j] = protoModule;

                                if (i != j)
                                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoModule.moduleName}\" with {VALUENAME_MODULEPARTCONFIGID}={protoModuleId} at index [{i}] moved to index [{j}]");

                                multiFound = true;
                                break;
                            }
                        }

                        if (!multiFound)
                            Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoModule.moduleName}\" with {VALUENAME_MODULEPARTCONFIGID}={protoModuleId} at index [{i}] has been removed, no matching module in the part config");
                    }
                }


                int protoIndexInType = 0;
                foreach (ProtoPartModuleSnapshot otherppms in protoPart.modules)
                {
                    if (otherppms.moduleName == protoModule.moduleName)
                    {
                        if (otherppms == protoModule)
                            break;

                        protoIndexInType++;
                    }
                }

                int prefabIndexInType = 0;
                bool found = false;
                for (int j = 0; j < partModuleCount; j++)
                {
                    if (part.Modules[j].moduleName == protoModule.moduleName)
                    {
                        if (prefabIndexInType == protoIndexInType)
                        {
                            int moduleIndex = j;
                            protoModule.Load(part, ref moduleIndex);
                            foundModules[j] = protoModule;

                            if (i != j)
                                Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoModule.moduleName}\" at index [{i}] moved to index [{j}]");

                            found = true;
                            break;
                        }

                        prefabIndexInType++;
                    }
                }

                if (!found)
                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{protoModule.moduleName}\" at index [{i}] has been removed, no matching module in the part config");
            }

            for (int i = 0; i < partModuleCount; i++)
            {
                if (foundModules[i] == null)
                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Module \"{part.Modules[i].moduleName}\" at index [{i}] doesn't exist in the persisted part, a new instance will be created");
            }
        }

        static IEnumerable<CodeInstruction> ShipConstruct_LoadShip_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo Part_LoadModule = AccessTools.Method(typeof(Part), nameof(Part.LoadModule));
            MethodInfo LoadShipModuleNodes = AccessTools.Method(typeof(ModuleIndexingMismatch), nameof(ModuleIndexingMismatch.LoadShipModuleNodes));
            MethodInfo ConfigNode_get_nodes = AccessTools.PropertyGetter(typeof(ConfigNode), nameof(ConfigNode.nodes));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            // first, remove the original module load call
            bool originalFound = false;
            for (int i = 0; i < code.Count - 6; i++)
            {
                //// part.LoadModule(configNode2, ref moduleIndex);
                // ldloc.s 6
                // ldloc.3
                // ldloca.s 39
                // callvirt instance class PartModule Part::LoadModule(class ConfigNode, int32&)
                // dup
                // pop
                // pop
                if (code[i].opcode == OpCodes.Ldloc_S
                    && code[i + 1].opcode == OpCodes.Ldloc_3
                    && code[i + 2].opcode == OpCodes.Ldloca_S
                    && code[i + 3].opcode == OpCodes.Callvirt && ReferenceEquals(code[i + 3].operand, Part_LoadModule)
                    && code[i + 4].opcode == OpCodes.Dup
                    && code[i + 5].opcode == OpCodes.Pop
                    && code[i + 6].opcode == OpCodes.Pop)
                {
                    originalFound = true;
                    for (int j = i; j < i + 7; j++)
                    {
                        code[j].opcode = OpCodes.Nop;
                        code[j].operand = null;
                    }
                    break;
                }
            }

            if (!originalFound)
            {
                Debug.LogError($"Error applying ModuleIndexingMismatch patch : couldn't find Part.LoadModule() call in ShipConstruct.LoadShip()");
                return instructions;
            }

            // then, insert our own module loading call before the original part nodes parsing loop
            for (int i = 0; i < code.Count - 6; i++)
            {
                //// int moduleIndex = 0;
                // ldc.i4.0 NULL
                // stloc.s 39(System.Int32)
                //// int m = 0;
                // ldc.i4.0 NULL
                // stloc.s 45(System.Int32)
                //// int count5 = configNode.nodes.Count
                // ldloc.2 NULL
                // callvirt ConfigNodeList ConfigNode::get_nodes()
                // dup NULL
                // pop NULL
                // callvirt System.Int32 ConfigNodeList::get_Count()

                if (code[i].opcode == OpCodes.Ldc_I4_0
                && code[i + 1].opcode == OpCodes.Stloc_S
                && code[i + 2].opcode == OpCodes.Ldc_I4_0
                && code[i + 3].opcode == OpCodes.Stloc_S
                && code[i + 4].opcode == OpCodes.Ldloc_2
                && code[i + 5].opcode == OpCodes.Callvirt && ReferenceEquals(code[i + 5].operand, ConfigNode_get_nodes))
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Ldloc_S, 6)); // ldloc.s 6 is the Part local variable
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldloc_2)); // Ldloc_2 is the part ConfigNode variable
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, LoadShipModuleNodes));
                    break;
                }
            }

            return code;
        }

        private static readonly List<ConfigNode> currentModuleNodes = new List<ConfigNode>();

        static void LoadShipModuleNodes(Part part, ConfigNode partNode)
        {
            currentModuleNodes.Clear();
            foreach (ConfigNode compNode in partNode.nodes)
                if (compNode.name == "MODULE")
                    currentModuleNodes.Add(compNode);

            int nodeModuleCount = currentModuleNodes.Count;

            string[] nodeModuleNames = new string[nodeModuleCount];
            for (int i = 0; i < nodeModuleCount; i++)
                nodeModuleNames[i] = currentModuleNodes[i].GetValue("name");

            int partModuleCount = part.Modules.Count;
            bool inSync = nodeModuleCount == partModuleCount;

            if (inSync)
            {
                for (int i = 0; i < nodeModuleCount; i++)
                {
                    if (part.Modules[i].moduleName != nodeModuleNames[i])
                    {
                        inSync = false;
                        break;
                    }
                }
            }

            if (inSync)
            {
                for (int i = 0; i < nodeModuleCount; i++)
                {
                    int moduleIndex = i;

                    if (part.Modules[i] is IMultipleModuleInPart multiModule)
                    {
                        string nodeModuleId = currentModuleNodes[i].GetValue(VALUENAME_MODULEPARTCONFIGID);
                        if (!string.IsNullOrEmpty(nodeModuleId) && multiModule.ModulePartConfigId != nodeModuleId)
                        {
                            moduleIndex = -1;
                            for (int j = 0; j < partModuleCount; j++)
                            {
                                if (part.Modules[j] is IMultipleModuleInPart otherMultiModule && otherMultiModule.ModulePartConfigId == nodeModuleId)
                                {
                                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleNames[i]}\" with {VALUENAME_MODULEPARTCONFIGID}={nodeModuleId} at index [{i}] moved to index [{j}]");
                                    moduleIndex = j;
                                    break;
                                }
                            }

                            if (moduleIndex == -1)
                            {
                                Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleNames[i]}\" with {VALUENAME_MODULEPARTCONFIGID}={nodeModuleId} at index [{i}] has been removed, no matching module in the part config");
                                continue;
                            }
                        }

                    }

                    part.LoadModule(currentModuleNodes[i], ref moduleIndex);
                }
                return;
            }

            Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Part \"{part.partInfo.name}\" configuration has changed. Synchronizing persisted modules...");
            ConfigNode[] foundModules = new ConfigNode[partModuleCount];

            for (int i = 0; i < nodeModuleCount; i++)
            {
                ConfigNode nodeModule = currentModuleNodes[i];
                string nodeModuleName = nodeModuleNames[i];

                if (multiModules.Contains(nodeModuleName))
                {
                    string nodeModuleId = nodeModule.GetValue(VALUENAME_MODULEPARTCONFIGID);

                    if (!string.IsNullOrEmpty(nodeModuleId))
                    {
                        bool multiFound = false;
                        for (int j = 0; j < partModuleCount; j++)
                        {
                            if (part.Modules[j] is IMultipleModuleInPart multiModule && multiModule.ModulePartConfigId == nodeModuleId)
                            {
                                int moduleIndex = j;
                                part.LoadModule(nodeModule, ref moduleIndex);
                                foundModules[j] = nodeModule;

                                if (i != j)
                                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleName}\" with {VALUENAME_MODULEPARTCONFIGID}={nodeModuleId} at index [{i}] moved to index [{j}]");

                                multiFound = true;
                                break;
                            }
                        }

                        if (!multiFound)
                            Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleName}\" with {VALUENAME_MODULEPARTCONFIGID}={nodeModuleId} at index [{i}] has been removed, no matching module in the part config");
                    }
                }


                int nodeIndexInType = 0;
                for (int j = 0; j < nodeModuleCount; j++)
                {
                    if (nodeModuleNames[j] == nodeModuleName)
                    {
                        if (currentModuleNodes[j] == nodeModule)
                            break;

                        nodeIndexInType++;
                    }
                }

                int prefabIndexInType = 0;
                bool found = false;
                for (int j = 0; j < partModuleCount; j++)
                {
                    if (part.Modules[j].moduleName == nodeModuleName)
                    {
                        if (prefabIndexInType == nodeIndexInType)
                        {
                            int moduleIndex = j;
                            part.LoadModule(nodeModule, ref moduleIndex);
                            foundModules[j] = nodeModule;

                            if (i != j)
                                Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleName}\" at index [{i}] moved to index [{j}]");

                            found = true;
                            break;
                        }

                        prefabIndexInType++;
                    }
                }

                if (!found)
                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Persisted module \"{nodeModuleName}\" at index [{i}] has been removed, no matching module in the part config");
            }

            for (int i = 0; i < partModuleCount; i++)
            {
                if (foundModules[i] == null)
                    Debug.LogWarning($"[KSPCF:ModuleIndexingMismatch] Module \"{part.Modules[i].moduleName}\" at index [{i}] doesn't exist in the persisted part, a new instance will be created");
            }
        }
    }
}
