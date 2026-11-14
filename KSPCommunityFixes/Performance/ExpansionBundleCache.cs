using Expansions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal sealed class ExpansionBundleCache : MonoBehaviour
    {
        private sealed class Entry
        {
            internal string source;
            internal string key;
            internal string cached;
            internal long length;
            internal long stamp;
            internal long cachedLength;
            internal long cachedStamp;
            internal long manifestStamp;
            internal bool complete;
        }

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(
            Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private string cacheRoot;
        private int building;

        private void Awake()
        {
            try
            {
                cacheRoot = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KSPCommunityFixes/PluginData/DLC", Application.unityVersion);
                string root = KSPExpansionsUtils.ExpansionsGameDataPath;
                if (Directory.Exists(root))
                    foreach (string directory in Directory.GetDirectories(root))
                    {
                        string assets = Path.Combine(directory, "AssetBundles");
                        if (!Directory.Exists(assets))
                            continue;
                        foreach (string path in Directory.GetFiles(assets))
                        {
                            FileInfo file = new FileInfo(path);
                            entries.Add(Path.GetFullPath(path), new Entry
                            {
                                source = path,
                                length = file.Length,
                                stamp = file.LastWriteTimeUtc.Ticks
                            });
                        }
                    }
                StartCoroutine(PrepareAll());
            }
            catch (Exception e)
            {
                foreach (Entry entry in entries.Values)
                    entry.complete = true;
                Debug.LogWarning($"[KSPCF/ExpansionBundlePreload] Cache preparation unavailable: {e.Message}");
            }
        }

        internal static bool IsPreparing(string path)
        {
            return entries.TryGetValue(Path.GetFullPath(path), out Entry entry) && !entry.complete;
        }

        internal static string GetCachedPath(string path, byte[] originalHash)
        {
            if (entries.TryGetValue(Path.GetFullPath(path), out Entry entry) && entry.complete
                && entry.key == Hex(originalHash) && entry.cached != null && Unchanged(entry))
            {
                FileInfo cached = new FileInfo(entry.cached);
                FileInfo manifest = new FileInfo(entry.cached + ".sha256");
                if (cached.Exists && manifest.Exists && cached.Length == entry.cachedLength
                    && cached.LastWriteTimeUtc.Ticks == entry.cachedStamp && manifest.Length == 64
                    && manifest.LastWriteTimeUtc.Ticks == entry.manifestStamp)
                    return entry.cached;
            }
            return null;
        }

        private static void SetCached(Entry entry, string path)
        {
            FileInfo file = new FileInfo(path);
            entry.cachedLength = file.Length;
            entry.cachedStamp = file.LastWriteTimeUtc.Ticks;
            entry.manifestStamp = File.GetLastWriteTimeUtc(path + ".sha256").Ticks;
            entry.cached = path;
        }

        internal static byte[] ComputeHash(string path)
        {
            using (MD5 hash = MD5.Create())
            using (FileStream stream = File.OpenRead(path))
                return hash.ComputeHash(stream);
        }

        private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        private static string CacheHash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return Hex(hash.ComputeHash(stream));
        }

        private static bool Unchanged(Entry entry)
        {
            FileInfo file = new FileInfo(entry.source);
            return file.Exists && file.Length == entry.length && file.LastWriteTimeUtc.Ticks == entry.stamp;
        }

        private IEnumerator PrepareAll()
        {
            int limit = Math.Max(1, Math.Min(Environment.ProcessorCount, entries.Count));
            foreach (Entry entry in entries.Values)
            {
                while (building >= limit)
                    yield return null;
                building++;
                StartCoroutine(PrepareGuarded(entry));
            }
            while (building > 0)
                yield return null;
            Destroy(this);
        }

        private IEnumerator PrepareGuarded(Entry entry)
        {
            IEnumerator work = Prepare(entry);
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = work.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[KSPCF/ExpansionBundlePreload] Cache preparation skipped for {entry.source}: {e.Message}");
                        break;
                    }
                    if (!more)
                        break;
                    yield return work.Current;
                }
            }
            finally
            {
                (work as IDisposable)?.Dispose();
                entry.complete = true;
                building--;
            }
        }

        private IEnumerator Prepare(Entry entry)
        {
            Task<byte[]> hashTask = Task.Run(() => ComputeHash(entry.source));
            while (!hashTask.IsCompleted)
                yield return null;
            entry.key = Hex(hashTask.GetAwaiter().GetResult());
            if (!Unchanged(entry))
                yield break;

            string cached = Path.Combine(cacheRoot, entry.key + ".bundle");
            string manifest = cached + ".sha256";
            Task<bool> validation = Task.Run(() => File.Exists(cached) && File.Exists(manifest)
                && new FileInfo(manifest).Length == 64 && CacheHash(cached) == File.ReadAllText(manifest));
            while (!validation.IsCompleted)
                yield return null;
            if (validation.GetAwaiter().GetResult())
            {
                SetCached(entry, cached);
                yield break;
            }

            Directory.CreateDirectory(cacheRoot);
            long reservation = 64L * 1024 * 1024;
            foreach (Entry item in entries.Values)
                reservation = checked(reservation + item.length * 3);
            if (new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace < reservation)
                yield break;

            string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string temporaryManifest = temporary + ".sha256";
            try
            {
                var operation = AssetBundle.RecompressAssetBundleAsync(entry.source, temporary, BuildCompression.LZ4Runtime, 0, UnityEngine.ThreadPriority.High);
                while (!operation.isDone)
                    yield return null;
                if (!operation.success)
                    throw new IOException(operation.humanReadableResult);

                Task commit = Task.Run(() =>
                {
                    if (!Unchanged(entry) || Hex(ComputeHash(entry.source)) != entry.key)
                        throw new IOException("Expansion bundle changed during recompression");
                    File.WriteAllText(temporaryManifest, CacheHash(temporary));
                    if (File.Exists(cached))
                        File.Replace(temporary, cached, null);
                    else
                        File.Move(temporary, cached);
                    if (File.Exists(manifest))
                        File.Replace(temporaryManifest, manifest, null);
                    else
                        File.Move(temporaryManifest, manifest);
                });
                while (!commit.IsCompleted)
                    yield return null;
                commit.GetAwaiter().GetResult();
                SetCached(entry, cached);
            }
            finally
            {
                foreach (string path in new[] { temporary, temporaryManifest })
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
        }
    }
}
