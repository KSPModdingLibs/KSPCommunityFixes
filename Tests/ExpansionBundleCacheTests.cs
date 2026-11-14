using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using KSPCommunityFixes.Performance;

static class ExpansionBundleCacheTests
{
    static int checks;
    static void Check(bool value, string reason)
    {
        checks++;
        if (!value) throw new Exception(reason);
    }

    static void Restart()
    {
        ((IDictionary)typeof(ExpansionBundleCache).GetField("entries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        typeof(ExpansionBundleCache).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(new ExpansionBundleCache(), null);
    }

    static int Main()
    {
        try
        {
            string root = Path.Combine(Environment.CurrentDirectory, "cache-fixtures-" + Guid.NewGuid().ToString("N"));
            KSPUtil.ApplicationRootPath = root;
            Expansions.KSPExpansionsUtils.ExpansionsGameDataPath = Path.Combine(root, "Expansions");
            string assets = Path.Combine(Expansions.KSPExpansionsUtils.ExpansionsGameDataPath, "Pack", "AssetBundles");
            Directory.CreateDirectory(assets);
            string source = Path.Combine(assets, "test.bundle");
            byte[] contents = new byte[65536];
            new Random(123).NextBytes(contents);
            File.WriteAllBytes(source, contents);
            byte[] hash = ExpansionBundleCache.ComputeHash(source);
            Restart();
            string cached = ExpansionBundleCache.GetCachedPath(source, hash);
            Check(cached != null && File.Exists(cached), "Cold cache not published");
            Check(UnityEngine.AssetBundle.recompressions == 1, "Cold source not recompressed once");
            Check(!ExpansionBundleCache.IsPreparing(source), "Completed entry remains pending");
            Check(File.ReadAllBytes(source).Length == contents.Length, "Source length changed");
            Check(BitConverter.ToString(ExpansionBundleCache.ComputeHash(source)) == BitConverter.ToString(hash), "Source contents changed");
            Restart();
            Check(ExpansionBundleCache.GetCachedPath(source, hash) == cached, "Warm cache not reused");
            Check(UnityEngine.AssetBundle.recompressions == 1, "Warm cache recompressed");
            Check(ExpansionBundleCache.GetCachedPath(source, new byte[16]) == null, "Wrong original hash accepted");
            File.SetLastWriteTimeUtc(cached, File.GetLastWriteTimeUtc(cached).AddSeconds(5));
            Check(ExpansionBundleCache.GetCachedPath(source, hash) == null, "Changed cache metadata accepted");
            Restart();
            Check(ExpansionBundleCache.GetCachedPath(source, hash) == cached, "Valid checksum not revalidated");
            File.WriteAllText(cached, "damaged");
            Restart();
            Check(UnityEngine.AssetBundle.recompressions == 2, "Damaged cached contents not rebuilt");
            Check(ExpansionBundleCache.GetCachedPath(source, hash) != null, "Rebuilt cache unavailable");
            File.WriteAllText(cached + ".sha256", new string('0', 64));
            Restart();
            Check(UnityEngine.AssetBundle.recompressions == 3, "Invalid manifest not rebuilt");
            DateTime originalTime = File.GetLastWriteTimeUtc(source);
            contents[0] ^= 1;
            File.WriteAllBytes(source, contents);
            File.SetLastWriteTimeUtc(source, originalTime);
            byte[] changedHash = ExpansionBundleCache.ComputeHash(source);
            Check(ExpansionBundleCache.GetCachedPath(source, changedHash) == null, "Same-size/timestamp source edit accepted");
            Restart();
            Check(ExpansionBundleCache.GetCachedPath(source, changedHash) != cached, "Source content did not change cache key");
            UnityEngine.Application.unityVersion = "other-engine";
            Restart();
            Check(ExpansionBundleCache.GetCachedPath(source, changedHash) != cached, "Engine version not namespaced");
            UnityEngine.Application.unityVersion = "failing-engine";
            UnityEngine.AssetBundle.fail = true;
            Restart();
            Check(!ExpansionBundleCache.IsPreparing(source), "Failure leaves loader waiting");
            Check(ExpansionBundleCache.GetCachedPath(source, changedHash) == null, "Failed cache accepted");
            Check(ExpansionBundleCache.GetCachedPath(source + ".missing", hash) == null, "Unknown bundle accepted");
            Check(Directory.GetFiles(root, "*.tmp*", SearchOption.AllDirectories).Length == 0, "Staging files leaked");
            Console.WriteLine("Expansion cache: " + checks + " checks passed; production helper with simulated Unity recompression, filesystem/hash/task behavior exercised.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

static class KSPUtil { public static string ApplicationRootPath; }
namespace Expansions { static class KSPExpansionsUtils { public static string ExpansionsGameDataPath; } }
sealed class KSPAddon : Attribute
{
    public enum Startup { Instantly }
    public KSPAddon(Startup startup, bool once) { }
}
namespace UnityEngine
{
    class MonoBehaviour
    {
        protected object StartCoroutine(IEnumerator routine)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (routine.MoveNext())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException();
                Thread.Sleep(1);
            }
            return null;
        }
        protected static void Destroy(object value) { }
    }
    static class Application { public static string unityVersion = "test-engine"; }
    static class Debug { public static void LogWarning(string message) { } }
    enum ThreadPriority { High }
    struct BuildCompression { public static BuildCompression LZ4Runtime = new BuildCompression(); }
    class AssetBundle
    {
        public static int recompressions;
        public static bool fail;
        public static Operation RecompressAssetBundleAsync(string source, string output, BuildCompression compression, int crc, ThreadPriority priority)
        {
            recompressions++;
            if (fail) throw new IOException("Simulated recompression failure");
            File.Copy(source, output);
            return new Operation();
        }
    }
    class Operation
    {
        public bool isDone = true;
        public bool success = true;
        public string humanReadableResult = "";
    }
}
