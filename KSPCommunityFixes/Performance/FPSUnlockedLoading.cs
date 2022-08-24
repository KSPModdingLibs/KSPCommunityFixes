using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class FPSUnlockedLoading : MonoBehaviour
    {
        private static float minFrameTime = 1f / 60f;

        private static Harmony harmony;

        void Awake()
        {
            harmony = new Harmony(nameof(FPSUnlockedLoading));
            Harmony.DEBUG = true;

            MethodInfo m_PartLoader_StartLoad = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.StartLoad));
            MethodInfo t_PartLoader_StartLoad = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(PartLoader_StartLoad_Transpiler));
            harmony.Patch(m_PartLoader_StartLoad, null, null, new HarmonyMethod(t_PartLoader_StartLoad));

            PatchStartCoroutineInEnumerator(AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileParts)));
            PatchStartCoroutineInEnumerator(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.SetupDragCubeCoroutine), new[] { typeof(Part) }));
            PatchStartCoroutineInEnumerator(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.RenderDragCubesCoroutine)));
        }

        IEnumerator Start()
        {
            yield return null;


        }

        static void PatchStartCoroutineInEnumerator(MethodInfo enumerator)
        {
            MethodInfo t_StartCoroutinePassThroughTranspiler = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(StartCoroutinePassThroughTranspiler));
            harmony.Patch(AccessTools.EnumeratorMoveNext(enumerator), null, null, new HarmonyMethod(t_StartCoroutinePassThroughTranspiler));
        }

        static IEnumerable<CodeInstruction> StartCoroutinePassThroughTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_StartCoroutine = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] {typeof(IEnumerator)});
            MethodInfo m_StartCoroutinePassThrough = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(StartCoroutinePassThrough));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_StartCoroutine))
                {
                    code[i].operand = m_StartCoroutinePassThrough;
                }
            }

            return code;
        }

        static object StartCoroutinePassThrough(object instance, IEnumerator enumerator)
        {
            return enumerator;
        }

        static IEnumerable<CodeInstruction> PartLoader_StartLoad_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_PartLoader_CompileAll = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileAll));
            MethodInfo m_PartLoader_CompileAll_Modded = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(PartLoader_CompileAll));
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            bool valid = false;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_PartLoader_CompileAll))
                {
                    code[i].operand = m_PartLoader_CompileAll_Modded;
                    for (int j = i - 1; j >= i - 4; j--)
                    {
                        if (code[j].opcode == OpCodes.Ldarg_0 && code[j - 1].opcode == OpCodes.Ldarg_0)
                        {
                            code[j].opcode = OpCodes.Nop;
                            valid = true;
                            break;
                        }
                    }
                    break;
                }
            }

            if (!valid)
                throw new Exception("PartLoader_StartLoad_Transpiler : transpiler patch failed");

            return code;
        }

        static IEnumerator PartLoader_CompileAll()
        {
            PartLoader instance = PartLoader.Instance;

            if (instance._recompile)
            {
                instance.ClearAll();
            }
            instance.progressTitle = "";
            instance.progressFraction = 0f;
            for (int i = 0; i < instance.initialPartsLength; i++)
            {
                AvailablePart availablePart = new AvailablePart(instance.parts[i]);
                availablePart.partPrefab.gameObject.SetActive(value: false);
                availablePart.partPrefab = Instantiate(availablePart.partPrefab);
                availablePart.partPrefab.transform.parent = instance.transform;
                availablePart.partPrefab.gameObject.SetActive(value: false);
                if (availablePart.partPrefab.fxGroups != null)
                {
                    for (int j = 0; j < availablePart.partPrefab.fxGroups.Count; j++)
                    {
                        if (availablePart.partPrefab.fxGroups[j].maxVisualPower == 0f)
                        {
                            availablePart.partPrefab.fxGroups[j].maxVisualPower = 1f;
                        }
                    }
                }
                if ((bool)FlightGlobals.fetch)
                {
                    FlightGlobals.PersistentLoadedPartIds.Remove(availablePart.partPrefab.persistentId);
                }
                if (availablePart.iconPrefab != null)
                {
                    availablePart.iconPrefab = Instantiate(availablePart.iconPrefab);
                    availablePart.iconPrefab.transform.parent = instance.transform;
                    availablePart.iconPrefab.name = availablePart.partPrefab.name + " icon";
                    availablePart.iconPrefab.gameObject.SetActive(value: false);
                }
                instance.loadedParts.Add(availablePart);
            }
            UrlDir.UrlConfig[] configs = GameDatabase.Instance.GetConfigs("PART");
            UrlDir.UrlConfig[] allPropNodes = GameDatabase.Instance.GetConfigs("PROP");
            UrlDir.UrlConfig[] allSpaceNodes = GameDatabase.Instance.GetConfigs("INTERNAL");
            UrlDir.UrlConfig[] configs2 = GameDatabase.Instance.GetConfigs("VARIANTTHEME");
            int num = configs.Length + allPropNodes.Length + allSpaceNodes.Length;
            instance.progressDelta = 1f / (float)num;
            instance.InitializePartDatabase();
            instance.APFinderByIcon.Clear();
            instance.APFinderByName.Clear();
            instance.CompileVariantThemes(configs2);

            //yield return StartCoroutine(CompileParts(configs));
            //yield return StartCoroutine(CompileInternalProps(allPropNodes));
            //yield return StartCoroutine(CompileInternalSpaces(allSpaceNodes));

            IEnumerator compilePartsEnumerator = FrameUnlockedCoroutine(instance.CompileParts(configs));
            while (compilePartsEnumerator.MoveNext())
                yield return null;

            IEnumerator compileInternalPropsEnumerator = FrameUnlockedCoroutine(instance.CompileInternalProps(allPropNodes));
            while (compileInternalPropsEnumerator.MoveNext())
                yield return null;

            IEnumerator compileInternalSpacesEnumerator = FrameUnlockedCoroutine(instance.CompileInternalSpaces(allSpaceNodes));
            while (compileInternalSpacesEnumerator.MoveNext())
                yield return null;


            // float nextFrameTime = Time.realtimeSinceStartup + minFrameTime;

            //IEnumerator compilePartsEnumerator = instance.CompileParts(configs);
            //while (compilePartsEnumerator.MoveNext())
            //{
            //    if (Time.realtimeSinceStartup > nextFrameTime)
            //    {
            //        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
            //        yield return null;
            //    }
            //    else
            //    {
            //        Thread.Sleep(0);
            //    }
            //}

            //IEnumerator compileInternalPropsEnumerator = instance.CompileInternalProps(allPropNodes);
            //while (compileInternalPropsEnumerator.MoveNext())
            //{
            //    if (Time.realtimeSinceStartup > nextFrameTime)
            //    {
            //        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
            //        yield return null;
            //    }
            //    else
            //    {
            //        Thread.Sleep(0);
            //    }
            //}

            //IEnumerator compileInternalSpacesEnumerator = instance.CompileInternalSpaces(allSpaceNodes);
            //while (compileInternalSpacesEnumerator.MoveNext())
            //{
            //    if (Time.realtimeSinceStartup > nextFrameTime)
            //    {
            //        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
            //        yield return null;
            //    }
            //    else
            //    {
            //        Thread.Sleep(0);
            //    }
            //}

            instance.SavePartDatabase();
            instance._recompile = false;
            PartUpgradeManager.Handler.LinkUpgrades();
            GameEvents.OnUpgradesLinked.Fire();
            instance.isReady = true;
            GameEvents.OnPartLoaderLoaded.Fire();
        }

        static IEnumerator FrameUnlockedCoroutine(IEnumerator coroutine)
        {
            float nextFrameTime = Time.realtimeSinceStartup + minFrameTime;

            Stack<IEnumerator> enumerators = new Stack<IEnumerator>();
            enumerators.Push(coroutine);

            while (enumerators.TryPop(out IEnumerator currentEnumerator))
            {
                while (currentEnumerator.MoveNext())
                {
                    if (currentEnumerator.Current is IEnumerator nestedCoroutine)
                    {
                        enumerators.Push(currentEnumerator);
                        currentEnumerator = nestedCoroutine;
                        continue;
                    }

                    if (Time.realtimeSinceStartup > nextFrameTime)
                    {
                        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                        yield return null;
                    }
                    else
                    {
                        Thread.Sleep(0);
                    }
                }
            }
        }
    }
}
