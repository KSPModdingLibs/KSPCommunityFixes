#if DEBUG
using System.Collections;
using System.Text;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// DEBUG-only self-test: builds a mesh bundle in memory, loads it via
    /// <c>AssetBundle.LoadFromMemoryAsync</c> + a per-name <c>LoadAssetAsync</c>, and verifies the
    /// loaded mesh renders and reads back identically to the source. Runs once at startup and logs a
    /// <c>[MeshSelfTest]</c> PASS/FAIL sentinel. This validates the mesh serialization in the real
    /// Unity runtime before the model pipeline is built on top of it.
    /// <para>The mesh name here is deliberately <b>non-canonical</b> (mixed case + backslash) and is
    /// used verbatim as both the source blob name and the <c>LoadAssetAsync</c> query. Unity
    /// canonicalizes the query, and <see cref="MeshBundleBuilder.Canonicalize"/> canonicalizes the
    /// stored container key, so the load only succeeds if that canonicalization is applied — this is
    /// the regression guard for the container-key canonicalization fix.</para>
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class MeshBundleSelfTest : MonoBehaviour
    {
        const string Tag = "[MeshSelfTest]";

        // Deliberately non-canonical (uppercase + backslash) to exercise container-key
        // canonicalization: Unity lowercases/forward-slashes the LoadAssetAsync query to
        // "meshselftest/quad#0", which must match the canonicalized stored key.
        const string MeshName = "MeshSelfTest\\Quad#0";

        IEnumerator Start()
        {
            Mesh src = BuildSourceQuad();
            MeshBlob blob = MeshBlobBuilder.FromMesh(src, MeshName);
            byte[] bytes = MeshBundleBuilder.BuildMany(new[] { blob });
            Debug.Log($"{Tag} built bundle: {bytes.Length} bytes, src verts={src.vertexCount}");

            AssetBundleCreateRequest createReq = AssetBundle.LoadFromMemoryAsync(bytes);
            yield return createReq;
            AssetBundle bundle = createReq.assetBundle;
            if (bundle == null)
            {
                Debug.LogError($"{Tag} FAIL: LoadFromMemoryAsync returned a null bundle");
                yield break;
            }

            AssetBundleRequest loadReq = bundle.LoadAssetAsync<Mesh>(MeshName);
            yield return loadReq;
            Mesh loaded = loadReq.asset as Mesh;
            if (loaded == null)
            {
                Debug.LogError($"{Tag} FAIL: LoadAssetAsync<Mesh>(\"{MeshName}\") returned null " +
                               "(container-key lookup or deserialization failed)");
                bundle.Unload(true);
                yield break;
            }

            var sb = new StringBuilder();
            bool ok = Compare(src, loaded, sb);
            ok &= CheckMetrics(blob, loaded, sb);

            // Exercise the render path: put it on a GameObject with a renderer (no exception == ok).
            bool rendered = true;
            try
            {
                var go = new GameObject("MeshSelfTestRender");
                go.AddComponent<MeshFilter>().sharedMesh = loaded;
                go.AddComponent<MeshRenderer>();
                go.SetActive(false);
                Destroy(go);
            }
            catch (System.Exception e)
            {
                rendered = false;
                sb.Append($"\n  render setup threw: {e.GetType().Name}: {e.Message}");
            }

            if (ok && rendered && loaded.isReadable)
                Debug.Log($"{Tag} PASS: loaded readable mesh round-trips (verts={loaded.vertexCount}, " +
                          $"subMeshes={loaded.subMeshCount}){sb}");
            else
                Debug.LogError($"{Tag} FAIL: ok={ok} rendered={rendered} readable={loaded.isReadable}{sb}");

            bundle.Unload(false);
        }

        static Mesh BuildSourceQuad()
        {
            var mesh = new Mesh { name = "src_quad" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
            });
            mesh.SetNormals(new System.Collections.Generic.List<Vector3>
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            });
            mesh.SetTangents(new System.Collections.Generic.List<Vector4>
            {
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
            });
            mesh.colors32 = new[]
            {
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255),
                new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 128),
            };
            mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            });
            mesh.SetUVs(1, new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Verifies the baked UV distribution metric survives serialization. GetUVDistributionMetric is a
        // pure getter of the stored m_MeshMetrics, so the loaded mesh must read back the value baked into
        // the blob. The source quad is a unit square (each triangle's object area 0.5); UV0 covers the
        // full [0,1] square (UV area 0.5 -> ratio 1) and UV1 a [0,0.5] square (UV area 0.125 -> ratio 4),
        // so the metrics are exactly 1 and 4.
        static bool CheckMetrics(MeshBlob blob, Mesh loaded, StringBuilder sb)
        {
            bool ok = CheckMetric("uv0", 1f, blob.MeshMetric0, loaded.GetUVDistributionMetric(0), sb);
            ok &= CheckMetric("uv1", 4f, blob.MeshMetric1, loaded.GetUVDistributionMetric(1), sb);
            return ok;
        }

        static bool CheckMetric(string n, float expected, float baked, float readBack, StringBuilder sb)
        {
            bool ok = true;
            if (Mathf.Abs(baked - expected) > 1e-4f)
            { ok = false; sb.Append($"\n  metric {n} baked {baked} != expected {expected}"); }
            if (Mathf.Abs(readBack - baked) > 1e-6f)
            { ok = false; sb.Append($"\n  metric {n} read-back {readBack} != baked {baked}"); }
            return ok;
        }

        static bool Compare(Mesh a, Mesh b, StringBuilder sb)
        {
            bool ok = true;
            if (a.vertexCount != b.vertexCount) { ok = false; sb.Append($"\n  vertexCount {a.vertexCount} != {b.vertexCount}"); return ok; }
            if (a.subMeshCount != b.subMeshCount) { ok = false; sb.Append($"\n  subMeshCount {a.subMeshCount} != {b.subMeshCount}"); }
            ok &= CmpV3("vertices", a.vertices, b.vertices, sb);
            ok &= CmpV3("normals", a.normals, b.normals, sb);
            ok &= CmpV4("tangents", a.tangents, b.tangents, sb);
            ok &= CmpV2("uv", a.uv, b.uv, sb);
            ok &= CmpV2("uv2", a.uv2, b.uv2, sb);
            ok &= CmpC32("colors32", a.colors32, b.colors32, sb);
            int subs = Mathf.Min(a.subMeshCount, b.subMeshCount);
            for (int s = 0; s < subs; ++s)
                ok &= CmpInt($"triangles[{s}]", a.GetTriangles(s), b.GetTriangles(s), sb);
            return ok;
        }

        static bool CmpV3(string n, Vector3[] a, Vector3[] b, StringBuilder sb)
        {
            if (a.Length != b.Length) { sb.Append($"\n  {n} len {a.Length} != {b.Length}"); return false; }
            for (int i = 0; i < a.Length; ++i)
                if ((a[i] - b[i]).sqrMagnitude > 1e-10f) { sb.Append($"\n  {n}[{i}] {a[i]} != {b[i]}"); return false; }
            return true;
        }

        static bool CmpV4(string n, Vector4[] a, Vector4[] b, StringBuilder sb)
        {
            if (a.Length != b.Length) { sb.Append($"\n  {n} len {a.Length} != {b.Length}"); return false; }
            for (int i = 0; i < a.Length; ++i)
                if ((a[i] - b[i]).sqrMagnitude > 1e-10f) { sb.Append($"\n  {n}[{i}] {a[i]} != {b[i]}"); return false; }
            return true;
        }

        static bool CmpV2(string n, Vector2[] a, Vector2[] b, StringBuilder sb)
        {
            if (a.Length != b.Length) { sb.Append($"\n  {n} len {a.Length} != {b.Length}"); return false; }
            for (int i = 0; i < a.Length; ++i)
                if ((a[i] - b[i]).sqrMagnitude > 1e-10f) { sb.Append($"\n  {n}[{i}] {a[i]} != {b[i]}"); return false; }
            return true;
        }

        static bool CmpC32(string n, Color32[] a, Color32[] b, StringBuilder sb)
        {
            if (a.Length != b.Length) { sb.Append($"\n  {n} len {a.Length} != {b.Length}"); return false; }
            for (int i = 0; i < a.Length; ++i)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                { sb.Append($"\n  {n}[{i}] ({a[i].r},{a[i].g},{a[i].b},{a[i].a}) != ({b[i].r},{b[i].g},{b[i].b},{b[i].a})"); return false; }
            return true;
        }

        static bool CmpInt(string n, int[] a, int[] b, StringBuilder sb)
        {
            if (a.Length != b.Length) { sb.Append($"\n  {n} len {a.Length} != {b.Length}"); return false; }
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) { sb.Append($"\n  {n}[{i}] {a[i]} != {b[i]}"); return false; }
            return true;
        }
    }
}
#endif
