using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using static GameDatabase;
using static UrlDir;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class FPSUnlockedLoading : MonoBehaviour
    {
        private static float minFrameTime = 1f / 30f;

        private static Harmony harmony;

        void Awake()
        {
            harmony = new Harmony(nameof(FPSUnlockedLoading));
            Harmony.DEBUG = true;

            MethodInfo m_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.SetupMainLoaders));
            MethodInfo t_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(GameDatabase_SetupMainLoaders_Postfix));
            harmony.Patch(m_GameDatabase_SetupMainLoaders, null, new HarmonyMethod(t_GameDatabase_SetupMainLoaders));

            // GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix
            MethodInfo m_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.LoadAssetBundleObjects)));
            MethodInfo t_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.Method(typeof(FPSUnlockedLoading), nameof(GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix));
            harmony.Patch(m_GameDatabase_LoadAssetBundleObjects_MoveNext, new HarmonyMethod(t_GameDatabase_LoadAssetBundleObjects_MoveNext));

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

        static void GameDatabase_SetupMainLoaders_Postfix(GameDatabase __instance, List<UrlDir.ConfigFileType> __result)
        {
            __instance.StartCoroutine(GameDatabase_LoadObjects(__result));
            loadObjectsInProgress = true;
            throw new Exception("Terminating stock GameDatabase.LoadObjects() coroutine");
        }

        static bool loadObjectsInProgress = false;

        static IEnumerator GameDatabase_LoadObjects(List<UrlDir.ConfigFileType> configFileTypes)
        {
            Debug.Log("[KSPCF] Starting custom GameDatabase_LoadObjects()");
            GameDatabase instance = GameDatabase.Instance;

            instance._root = new UrlDir(instance.urlConfig.ToArray(), configFileTypes.ToArray());
            instance.translateLoadedNodes();
            int audioCount = 0;
            int textureCount = 0;
            int modelCount = 0;
            foreach (UrlDir.UrlFile audioFile in instance.root.GetFiles(UrlDir.FileType.Audio))
            {
                if (audioFile != null)
                {
                    audioCount++;
                }
            }
            foreach (UrlDir.UrlFile textureFile in instance.root.GetFiles(UrlDir.FileType.Texture))
            {
                if (textureFile != null)
                {
                    textureCount++;
                }
            }
            foreach (UrlDir.UrlFile modelFile in instance.root.GetFiles(UrlDir.FileType.Model))
            {
                if (modelFile != null)
                {
                    modelCount++;
                }
            }
            float delta = 1f / (float)(audioCount + textureCount + modelCount);
            instance.progressFraction = 0f;
            bool loadTextures = true;
            bool loadAudio = true;
            bool loadParts = true;
            float nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
            foreach (UrlDir.UrlFile audioFile in instance.root.GetFiles(UrlDir.FileType.Audio))
            {
                if (instance.ExistsAudioClip(audioFile.url))
                {
                    if (!(audioFile.fileTime > instance.lastLoadTime))
                    {
                        continue;
                    }
                    UnityEngine.Debug.Log("Load(Audio): " + audioFile.url + " OUT OF DATE");
                    instance.RemoveAudioClip(audioFile.url);
                    PartLoader.Instance.Recompile = true;
                }
                instance.progressTitle = audioFile.url;
                UnityEngine.Debug.Log("Load(Audio): " + audioFile.url);
                foreach (DatabaseLoader<AudioClip> loader3 in instance.loadersAudio)
                {
                    if (loader3.extensions.Contains(audioFile.fileExtension))
                    {
                        if (loadAudio)
                        {
                            IEnumerator loaderEnumerator = loader3.Load(audioFile, new FileInfo(audioFile.fullPath));
                            while (loaderEnumerator.MoveNext())
                            {
                                if (Time.realtimeSinceStartup > nextFrameTime)
                                {
                                    nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                                    yield return null;
                                }
                            }
                        }
                        if (loader3.successful)
                        {
                            loader3.obj.name = audioFile.url;
                            instance.databaseAudio.Add(loader3.obj);
                            instance.databaseAudioFiles.Add(audioFile);
                        }
                    }
                }
                instance.progressFraction += delta;
            }
            foreach (UrlDir.UrlFile file3 in instance.root.GetFiles(UrlDir.FileType.Texture))
            {
                if (instance.ExistsTexture(file3.url))
                {
                    if (!(file3.fileTime > instance.lastLoadTime))
                    {
                        continue;
                    }
                    UnityEngine.Debug.Log("Load(Texture): " + file3.url + " OUT OF DATE");
                    instance.RemoveTexture(file3.url);
                    PartLoader.Instance.Recompile = true;
                }
                instance.progressTitle = file3.url;
                UnityEngine.Debug.Log("Load(Texture): " + file3.url);
                foreach (DatabaseLoader<TextureInfo> loader2 in instance.loadersTexture)
                {
                    if (loader2.extensions.Contains(file3.fileExtension))
                    {
                        instance._recompileModels = true;
                        if (loadTextures)
                        {
                            IEnumerator loaderEnumerator = loader2.Load(file3, new FileInfo(file3.fullPath));
                            while (loaderEnumerator.MoveNext())
                            {
                                if (Time.realtimeSinceStartup > nextFrameTime)
                                {
                                    nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                                    yield return null;
                                }
                            }
                        }
                        if (loader2.successful)
                        {
                            loader2.obj.name = file3.url;
                            loader2.obj.texture.name = file3.url;
                            instance.databaseTexture.Add(loader2.obj);
                        }
                    }
                }
                instance.progressFraction += delta;
            }
            if (instance._recompileModels)
            {
                foreach (GameObject item in instance.databaseModel)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
                instance.databaseModel.Clear();
            }
            foreach (UrlDir.UrlFile file3 in instance.root.GetFiles(UrlDir.FileType.Model))
            {
                if (instance.ExistsModel(file3.url))
                {
                    if (!(file3.fileTime > instance.lastLoadTime))
                    {
                        continue;
                    }
                    UnityEngine.Debug.Log("Load(Model): " + file3.url + " OUT OF DATE");
                    instance.RemoveModel(file3.url);
                    PartLoader.Instance.Recompile = true;
                }
                instance.progressTitle = file3.url;
                UnityEngine.Debug.Log("Load(Model): " + file3.url);
                foreach (DatabaseLoader<GameObject> loader in instance.loadersModel)
                {
                    if (loader.extensions.Contains(file3.fileExtension))
                    {
                        if (loadParts)
                        {
                            IEnumerator loaderEnumerator = loader.Load(file3, new FileInfo(file3.fullPath));
                            while (loaderEnumerator.MoveNext())
                            {
                                if (Time.realtimeSinceStartup > nextFrameTime)
                                {
                                    nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                                    yield return null;
                                }
                            }
                        }
                        if (loader.successful)
                        {
                            GameObject obj = loader.obj;
                            obj.transform.name = file3.url;
                            obj.transform.parent = instance.transform;
                            obj.transform.localPosition = Vector3.zero;
                            obj.transform.localRotation = Quaternion.identity;
                            obj.SetActive(value: false);
                            instance.databaseModel.Add(obj);
                            instance.databaseModelFiles.Add(file3);
                        }
                    }
                }
                instance.progressFraction += delta;
            }
            instance.lastLoadTime = KSPUtil.SystemDateTime.DateTimeNow();
            instance.progressFraction = 1f;
            loadObjectsInProgress = false;

            Debug.Log("[KSPCF] Finished custom GameDatabase_LoadObjects()");
        }

        static bool GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix(object __instance, ref bool __result)
        {
            if (loadObjectsInProgress)
            {
                FieldInfo f_current = AccessTools.GetDeclaredFields(__instance.GetType()).First(p => p.Name.Contains("current"));
                f_current.SetValue(__instance, null);
                __result = true;
                return false;
            }

            return true;
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
                    if (Time.realtimeSinceStartup > nextFrameTime)
                    {
                        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                        yield return null;
                    }

                    if (currentEnumerator.Current is IEnumerator nestedCoroutine)
                    {
                        enumerators.Push(currentEnumerator);
                        currentEnumerator = nestedCoroutine;
                        continue;
                    }
                }
            }
        }
    }
}
