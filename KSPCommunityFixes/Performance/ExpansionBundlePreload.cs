using Expansions;
using Expansions.Missions;
using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using static Expansions.BundleLoader;
using static Expansions.ExpansionsLoader;

namespace KSPCommunityFixes.Performance
{
    public class ExpansionBundlePreload : BasePatch
    {
        class AssetBundlePreloadInfo
        {
            public byte[] hash;
            public AssetBundle bundle;
        }

        static bool ExpansionsLoaderStarted = false;

        // This hasn't been verified with anything lower than 1.12.5
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override bool CanApplyPatch(out string reason)
        {
            reason = null;
            if (QoL.OptionalMakingHistoryDLCFeatures.isMHDisabledFromConfig)
                return true;
            if (QoL.OptionalMakingHistoryDLCFeatures.isMHEnabled)
                return true;

            reason = "Making History features disabled by OptionalMakingHistoryDLCFeatures patch";
            return false;
        }

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(ExpansionsLoader), "StartLoad");
        }

        protected override void OnPatchApplied()
        {
            var instance = ExpansionsLoader.Instance;
            if (instance is null)
                return;

            instance.StartCoroutine(LoadExpansionsV2(instance));
        }

        static void ExpansionsLoader_StartLoad_Override(ExpansionsLoader _)
        {
            ExpansionsLoaderStarted = true;
        }

        struct InitializeInfo
        {
            public CoroutineSemaphore semaphore;
            public Coroutine coroutine;
            public string file;
        }

        static IEnumerator LoadExpansionsV2(ExpansionsLoader loader)
        {
            // Wait one frame for patching to finish so that we don't contribute to patch time.
            yield return null;

            loader.progressTitle = Localizer.Format("#autoLOC_8003147");
            loader.progressFraction = 0f;

            float startTime;

            if (Directory.Exists(KSPExpansionsUtils.ExpansionsGameDataPath))
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(KSPExpansionsUtils.ExpansionsGameDataPath);
                FileInfo[] expansionFiles = directoryInfo.GetFiles("*" + expansionsMasterExtension, SearchOption.AllDirectories);
                loader.progressDelta = 1f / expansionFiles.Length;

                var infos = new List<InitializeInfo>(expansionFiles.Length);
                foreach (var file in expansionFiles)
                {
                    var semaphore = new CoroutineSemaphore();
                    var coroutine = loader.StartCoroutine(InitializeExpansionV2(loader, file.FullName, semaphore));

                    infos.Add(new InitializeInfo
                    {
                        semaphore = semaphore,
                        coroutine = coroutine,
                        file = file.Name,
                    });
                }

                yield return new WaitUntil(() => ExpansionsLoaderStarted);
                startTime = Time.realtimeSinceStartup;

                foreach (var info in infos)
                {
                    info.semaphore.Set();
                    loader.progressFraction += loader.progressDelta;

                    yield return info.coroutine;
                }
            }
            else
            {
                loader.progressFraction = 1f;
                yield return new WaitUntil(() => ExpansionsLoaderStarted);
                startTime = Time.realtimeSinceStartup;
            }

            MissionsUtils.InitialiseAdjusterTypes();
            loader.progressTitle = Localizer.Format("#autoLOC_8003148");
            loader.progressFraction = 1f;
            yield return null;
            loader.isReady = true;
            GameEvents.OnExpansionSystemLoaded.Fire();
            Debug.Log("ExpansionsLoader: Expansions loaded in " + (Time.realtimeSinceStartup - startTime).ToString("F3") + "s");
        }

        static IEnumerator InitializeExpansionV2(
            ExpansionsLoader loader,
            string expansionFile,
            CoroutineSemaphore ready
        )
        {
            if (!loader.InitPublicKeyCryptoProvider(out var verifier))
            {
                yield return ready;
                Debug.LogError("Unable to configure CryptoSigner.\nBreaking from Expansion Initializer for " + expansionFile);
                yield break;
            }

            loader.progressTitle = Localizer.Format("#autoLOC_8003149", Path.GetFileNameWithoutExtension(expansionFile));

            var expansionInfo = new AssetBundlePreloadInfo();
            yield return LoadExpansionBundleAsyncV2(expansionInfo, expansionFile);

            byte[] hashBytes = expansionInfo.hash;
            AssetBundle expansionSOBundle = expansionInfo.bundle;
            if (expansionSOBundle == null)
            {
                yield return ready;
                string message = Localizer.Format("#autoLOC_8004231", expansionFile);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;
            }

            string[] allAssetNames = expansionSOBundle.GetAllAssetNames();
            if (allAssetNames?.Length != 1)
            {
                yield return ready;
                string message = Localizer.Format("#autoLOC_8004232", expansionFile, allAssetNames.Length);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;

            }

            AssetBundleRequest requestSO = expansionSOBundle.LoadAssetAsync<ExpansionSO>(allAssetNames[0]);
            yield return requestSO;

            ExpansionSO masterBundleSO = requestSO.asset as ExpansionSO;
            if (masterBundleSO == null)
            {
                yield return ready;
                string message = Localizer.Format("#autoLOC_8004233", expansionFile);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;
            }

            loader.progressTitle = Localizer.Format("#autoLOC_8003150", masterBundleSO.DisplayName);
            string signature = File.ReadAllText(Path.GetDirectoryName(expansionFile) + "/signature");
            if (!loader.VerifyHashSignature(verifier, hashBytes, signature))
            {
                yield return ready;
                string message = Localizer.Format("#autoLOC_8004234", masterBundleSO.DisplayName);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;
            }

            bool isSupported = false;
            foreach (var expansion in loader.supportedExpansions)
            {
                if (expansion.expansionName != masterBundleSO.DisplayName)
                    continue;

                var minVersion = new Version(expansion.minimumVersion);
                var maxVersion = new Version(expansion.maximumVersion);
                var bundleVersion = new Version(masterBundleSO.Version);

                if (bundleVersion < minVersion || bundleVersion > maxVersion)
                {
                    yield return ready;
                    string message = Localizer.Format(
                        "#autoLOC_8004235",
                        masterBundleSO.DisplayName,
                        bundleVersion.ToString(),
                        minVersion.ToString(),
                        maxVersion.ToString()
                    );
                    Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                    loader.expansionsThatFailedToLoad.Add(message);
                    yield break;
                }

                isSupported = true;
                break;
            }

            if (!isSupported)
            {
                yield return ready;
                string message = Localizer.Format("#autoLOC_8004236", masterBundleSO.DisplayName);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;
            }

            var requiredKspVersion = new Version(masterBundleSO.KSPVersion);
            var actualKspVersion = new Version(VersioningBase.GetVersionString());
            if (actualKspVersion < requiredKspVersion)
            {
                yield return ready;
                string message = Localizer.Format(
                    "#autoLOC_8004237",
                    masterBundleSO.DisplayName,
                    requiredKspVersion.ToString(),
                    actualKspVersion.ToString()
                );
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
                yield break;
            }

            var folderPath = Path.Combine(
                KSPExpansionsUtils.ExpansionsGameDataPath,
                masterBundleSO.FolderName,
                "AssetBundles"
            );

            // Load all the asset bundles before processing them.
            var infos = new List<AssetBundlePreloadInfo>();
            var coros = new List<Coroutine>();
            foreach (var bundleSO in masterBundleSO.Bundles)
            {
                var bundleName = bundleSO.name;
                var info = new AssetBundlePreloadInfo();
                var coro = loader.StartCoroutine(LoadExpansionBundleAsyncV2(info, Path.Combine(folderPath, bundleName)));

                infos.Add(info);
                coros.Add(coro);
            }

            yield return ready;

            bool allBundlesVerified = true;
            for (int i = 0; i < masterBundleSO.Bundles.Count; ++i)
            {
                loader.progressTitle = Localizer.Format("#autoLOC_8003151", masterBundleSO.DisplayName, i + 1, masterBundleSO.Bundles.Count);

                yield return coros[i];
                var bundle = masterBundleSO.Bundles[i];
                var info = infos[i];

                LoadPreloadedAssetBundleV2(bundle.name, folderPath, info);

                if (!loader.VerifyHashSignature(hashBytes: info.hash, verifier: verifier, signature: bundle.fileSignature))
                {
                    Debug.LogError("Expansion Bundle [" + masterBundleSO.Bundles[i].name + "] not able to be verified");
                    allBundlesVerified = false;
                }
            }

            if (!allBundlesVerified)
            {
                string message = Localizer.Format("#autoLOC_8004238", masterBundleSO.DisplayName);
                Debug.LogError(message + "\nBreaking from Expansion Initializer!");
                loader.expansionsThatFailedToLoad.Add(message);
            }
            else
            {
                var info = new ExpansionInfo(expansionFile, masterBundleSO, isInstalled: true);
                loader.progressTitle = Localizer.Format("#autoLOC_8003150", masterBundleSO.DisplayName) + " SquadExpansion/" + masterBundleSO.FolderName;
                expansionsInfo.Add(masterBundleSO.FolderName, info);
            }
        }

        static void LoadPreloadedAssetBundleV2(
            string bundleName,
            string folderPath,
            AssetBundlePreloadInfo info
        )
        {
            if (IsBundleLoaded(bundleName))
            {
                Debug.Log(bundleName + " already loaded - skipping...");
                return;
            }

            byte[] hash = info.hash;
            AssetBundle bundle = info.bundle;

            if (bundle == null)
            {
                Debug.LogError("Failed to load asset bundle from " + folderPath + bundleName);
                return;
            }

            string[] allAssetNames = bundle.GetAllAssetNames();
            if (allAssetNames.Length != 0)
                Debug.Log("Assets:");

            for (int i = 0; i < allAssetNames.Length; i++)
            {
                Debug.Log(Path.GetFileName(allAssetNames[i]));
                ABAssetInfo value = new ABAssetInfo(allAssetNames[i], bundleName, isScene: false);
                loadedAssets.Add(allAssetNames[i], value);
            }

            string[] allScenePaths = bundle.GetAllScenePaths();
            if (allScenePaths.Length != 0)
            {
                Debug.Log("Scenes:");
            }

            for (int j = 0; j < allScenePaths.Length; j++)
            {
                Debug.Log(Path.GetFileName(allScenePaths[j]));
                ABAssetInfo value2 = new ABAssetInfo(allScenePaths[j], bundleName, isScene: true);
                loadedAssets.Add(allScenePaths[j], value2);
            }

            loadedBundles.Add(bundle.name, new ABInfo(bundle, folderPath + bundleName, hash));
        }

        static IEnumerator LoadExpansionBundleAsyncV2(
             AssetBundlePreloadInfo info,
             string path
         )
        {
            var request = AssetBundle.LoadFromFileAsync(path);
            var task = Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(path);
                return new MD5CryptoServiceProvider().ComputeHash(bytes);
            });

            yield return new WaitUntil(() => task.IsCompleted);

            try
            {
                info.hash = task.Result;
            }
            catch (Exception e)
            {
                Debug.LogError($"ExpansionLoader: failed to read expansion file {path}");
                Debug.LogException(e);
                yield break;
            }

            yield return request;
            info.bundle = request.assetBundle;
        }

        class CoroutineSemaphore : CustomYieldInstruction
        {
            bool ready = false;

            public override bool keepWaiting => !ready;

            public void Set()
            {
                ready = true;
            }
        }
    }
}
