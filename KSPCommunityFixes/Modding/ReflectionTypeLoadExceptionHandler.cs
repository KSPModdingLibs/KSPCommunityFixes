using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using KSP.Localization;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Modding
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class ReflectionTypeLoadExceptionHandler : MonoBehaviour
    {
        private static string LOC_KSPCFWarning = "KSPCommunityFixes warning";
        private static string LOC_PluginsLoadFailed = "The following plugin(s) failed to load :";
        private static string LOC_F_PluginLoadFailed_name_in_location = "<<1>> in <<2>>";
        private static string LOC_PluginLoadFailed_missingDep = "Load failed due to missing dependencies";

        private class FailedAssembly
        {
            public string errorMessage;
            public string guiMessage;

            public FailedAssembly(Assembly assembly)
            {
                string assemblyName;
                string assemblyLocation;
                List<string> missingDependencies = new List<string>();

                try
                {
                    assemblyName = assembly.GetName().Name;
                    assemblyLocation = assembly.Location.Remove(0, kspRootPathLength);

                    Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                    List<string> loadedAssemblyNames = new List<string>(loadedAssemblies.Length);
                    foreach (Assembly loadedAssembly in loadedAssemblies)
                        loadedAssemblyNames.Add(new AssemblyName(loadedAssembly.FullName).Name);

                    foreach (AssemblyName referencedAssembly in assembly.GetReferencedAssemblies())
                    {
                        if (!loadedAssemblyNames.Contains(referencedAssembly.Name))
                        {
                            missingDependencies.Add(referencedAssembly.Name);
                        }
                    }
                }
                catch
                {
                    assemblyName = assembly.FullName;
                    assemblyLocation = assembly.Location;
                }

                errorMessage = $"[KSPCF] A ReflectionTypeLoadException thrown by Assembly.GetTypes() has been handled by KSP Community Fixes." +
                               $"\nThis is usually harmless, but indicates that the \"{assemblyName}\" plugin failed to load (location: \"{assemblyLocation}\")";

                guiMessage = Localizer.Format(LOC_F_PluginLoadFailed_name_in_location, $"<b><color=orange>{assemblyName}</color></b>", $"\"{assemblyLocation}\"");

                if (missingDependencies.Count > 0)
                {
                    errorMessage += $"\nIt happened because \"{assemblyName}\" is missing the following dependencies : ";
                    guiMessage += $"\n{LOC_PluginLoadFailed_missingDep} : ";
                    for (int i = 0; i < missingDependencies.Count; i++)
                    {
                        if (i > 0)
                        {
                            errorMessage += ", ";
                            guiMessage += ", ";
                        }

                        errorMessage += "\"" + missingDependencies[i] + "\"";
                        guiMessage += "<b><color=orange>" + missingDependencies[i] + "</color></b>";
                    }
                }
            }
        }

        private static int mainThreadId;
        private static int kspRootPathLength;

        private static GUIStyle labelStyle;

        private static Dictionary<Assembly, FailedAssembly> failedAssemblies = new Dictionary<Assembly, FailedAssembly>();

        private static bool logStackTrace = true;

        private void Awake()
        {
            Harmony harmony = new Harmony("ReflectionTypeLoadExceptionHandler");
            harmony.Patch(
                AccessTools.Method(typeof(Assembly), nameof(Assembly.GetTypes), Array.Empty<Type>()),
                new HarmonyMethod(AccessTools.Method(typeof(ReflectionTypeLoadExceptionHandler), nameof(Assembly_GetTypes_Prefix))));

            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            kspRootPathLength = Path.GetFullPath(KSPUtil.ApplicationRootPath).Length;

            labelStyle = new GUIStyle(HighLogic.Skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = (int)(14f * GameSettings.UI_SCALE)
            };
        }

        // make sure we trigger all ReflectionTypeLoadException, even if someone loaded an assembly manually during Awake() or Start()
        private IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            
            logStackTrace = false;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                assembly.GetTypes();
            logStackTrace = true;
        }

        private void OnGUI()
        {
            if (failedAssemblies.Count == 0)
                return;

            GUILayout.BeginArea(new Rect(5, 0, 1000, 1000));
            GUILayout.BeginVertical();
            GUILayout.Label($"<b><color=orange>{LOC_KSPCFWarning}</color></b>", labelStyle);
            GUILayout.Label(LOC_PluginsLoadFailed, labelStyle);

            foreach (FailedAssembly assembly in failedAssemblies.Values)
                GUILayout.Label(assembly.guiMessage, labelStyle);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        static bool Assembly_GetTypes_Prefix(Assembly __instance, out Type[] __result)
        {
            try
            {
                __result = __instance.GetTypes(false);
            }
            catch (ReflectionTypeLoadException e)
            {
                LogReflectionTypeLoadExceptionInfo(__instance);

                int loadedTypesCount = e.Types.Length;
                if (loadedTypesCount > 0)
                {
                    List<Type> typeList = new List<Type>(loadedTypesCount);
                    foreach (Type loadedType in e.Types)
                        if (loadedType != null)
                            typeList.Add(loadedType);

                    __result = typeList.ToArray();
                }
                else
                {
                    __result = Type.EmptyTypes;
                }
            }

            return false;
        }

        static void LogReflectionTypeLoadExceptionInfo(Assembly assembly)
        {
            // do not log if called from a thread that isn't the unity main thread
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId)
                return;

            if (!failedAssemblies.TryGetValue(assembly, out FailedAssembly failedAssembly))
            {
                failedAssembly = new FailedAssembly(assembly);
                failedAssemblies.Add(assembly, failedAssembly);
            }

            if (logStackTrace)
                Debug.LogWarning($"{failedAssembly.errorMessage}\nStacktrace:\n{new StackTrace(3, false)}");
            else
                Debug.LogWarning(failedAssembly.errorMessage);
        }
    }
}
