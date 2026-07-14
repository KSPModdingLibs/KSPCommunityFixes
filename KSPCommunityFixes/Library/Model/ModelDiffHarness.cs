#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using KSPCommunityFixes.Library;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// DEBUG-only semantic-parity gate for the background model-loading pipeline. For a sample of real
    /// <c>.mu</c> models it builds the SAME model two ways from the SAME on-disk bytes:
    /// <list type="bullet">
    /// <item><b>Oracle</b>: <see cref="MuParser.Parse"/> (the original main-thread parser that builds
    /// <c>UnityEngine.Object</c>s directly).</item>
    /// <item><b>New path</b>: <see cref="MuModelCompiler"/> -> <see cref="MeshBundleBuilder"/> ->
    /// <c>AssetBundle.LoadFromMemoryAsync</c> -> per-mesh <c>LoadAssetAsync</c> -> replay of the compiled
    /// <see cref="IModelInstruction"/> list.</item>
    /// </list>
    /// It then diffs the two GameObject hierarchies (structure + transforms + component types) and every
    /// mesh (geometry, submeshes, bounds, and — the key skinned check — bind poses, bone weights, and the
    /// resolved <c>SkinnedMeshRenderer.bones</c>). This is the parity proof required before
    /// <see cref="MuParser"/> is deleted. Results are grep-friendly under the <c>[ModelDiff]</c> sentinel.
    /// <para>Runs at the main menu (GameDatabase loaded, so textures/shaders resolve for the new path's
    /// material instructions, and the <c>.mu</c> files are on disk). DEBUG-only, so it never ships.</para>
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal sealed class ModelDiffHarness : MonoBehaviour
    {
        const string Tag = "[ModelDiff]";

        // ---- Tweakables --------------------------------------------------------------------------

        /// <summary>How many models to spread-sample across the whole (optionally filtered) .mu set.
        /// Forced complex/skinned models (see <see cref="ComplexKeywords"/>) are added ON TOP of this.</summary>
        const int SampleCount = 60;

        /// <summary>Optional case-insensitive url-substring filter. When non-empty, only .mu files whose
        /// url contains it are considered (e.g. "Squad/Parts/Aero" or a single part name to focus a run).
        /// Null/empty = whole database.</summary>
        const string UrlFilter = null;

        /// <summary>Any .mu whose url contains one of these (case-insensitive) is force-included regardless
        /// of the spread sample — these bias toward skinned/animated/complex parts, which exercise the
        /// riskiest parts of the new path (skinned channels, bones, animation, many submeshes).</summary>
        static readonly string[] ComplexKeywords =
        {
            "landingleg", "landinggear", "gearbay", "robot", "hinge", "rotor", "piston", "drill",
            "claw", "solar", "panel", "radiator", "kerbaleva", "kerbalgirl", "serenity",
        };

        // ---- Epsilons ----------------------------------------------------------------------------

        // Geometry attributes round-trip through float32 with identical bits on both paths, so these are
        // effectively exact-equality checks with a tiny safety margin. Bounds get a looser tolerance
        // because the oracle uses Unity's RecalculateBounds while the new path computes its own AABB.
        const float VecEpsSq = 1e-8f;   // squared per-vertex vector tolerance (pos/normal/tangent/uv)
        const float QuatEps = 1e-6f;    // 1 - |dot| tolerance for localRotation
        const float MatEps = 1e-5f;     // per-element bind-pose matrix tolerance
        const float WeightEps = 1e-5f;  // per-vertex bone weight tolerance
        const float BoundsEps = 1e-2f;  // absolute floor for the (relative) mesh-bounds tolerance

        // The component types whose PRESENCE (and count) must match per node. SkinnedMeshRenderer and
        // MeshRenderer are distinct concrete types (both derive from Renderer, neither from the other), so
        // each is queried separately and unambiguously.
        static readonly (string Name, Type Type)[] Tracked =
        {
            ("MeshFilter", typeof(MeshFilter)),
            ("MeshRenderer", typeof(MeshRenderer)),
            ("SkinnedMeshRenderer", typeof(SkinnedMeshRenderer)),
            ("MeshCollider", typeof(MeshCollider)),
            ("BoxCollider", typeof(BoxCollider)),
            ("SphereCollider", typeof(SphereCollider)),
            ("CapsuleCollider", typeof(CapsuleCollider)),
            ("WheelCollider", typeof(WheelCollider)),
            ("Light", typeof(Light)),
            ("Camera", typeof(Camera)),
            ("KSPParticleEmitter", typeof(KSPParticleEmitter)),
            ("Animation", typeof(Animation)),
        };

        IEnumerator Start()
        {
            GameDatabase gdb = GameDatabase.Instance;
            if (gdb == null || gdb.root == null)
            {
                Debug.LogError($"{Tag} GameDatabase not available; aborting.");
                yield break;
            }

            // 1) Collect candidate .mu files (optionally filtered).
            var all = new List<UrlDir.UrlFile>();
            foreach (UrlDir.UrlFile f in gdb.root.AllFiles)
            {
                if (f == null || f.fileExtension != "mu")
                    continue;
                if (!string.IsNullOrEmpty(UrlFilter) && f.url.IndexOf(UrlFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                all.Add(f);
            }

            if (all.Count == 0)
            {
                Debug.LogWarning($"{Tag} no .mu models found (filter='{UrlFilter}'); nothing to do.");
                yield break;
            }

            // Spread-sample every Nth, then force-include complex/skinned parts on top (deduped by url).
            var selected = new List<UrlDir.UrlFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int step = Mathf.Max(1, all.Count / Mathf.Max(1, SampleCount));
            for (int i = 0; i < all.Count && selected.Count < SampleCount; i += step)
                if (seen.Add(all[i].url))
                    selected.Add(all[i]);

            int sampled = selected.Count;
            int forced = 0;
            for (int i = 0; i < all.Count; i++)
                if (ContainsKeyword(all[i].url) && seen.Add(all[i].url))
                {
                    selected.Add(all[i]);
                    forced++;
                }

            Debug.Log($"{Tag} START: {selected.Count} models ({sampled} sampled + {forced} forced complex) " +
                      $"of {all.Count} .mu files. SampleCount={SampleCount}, filter='{UrlFilter}'.");

            var compiler = new MuModelCompiler();
            var failures = new List<string>();
            int passed = 0;
            int total = 0;

            foreach (UrlDir.UrlFile file in selected)
            {
                total++;
                string url = file.url;

                GameObject a = null;
                GameObject b = null;
                AssetBundle bundle = null;
                CompiledModel cm = null;
                UnityEngine.Object[] locals = null;
                byte[] bytes;
                byte[] bundleBytes = null;
                string fail = null;
                string mismatch = null;

                // Phase A (synchronous, may throw): read bytes, build the oracle, compile, bake bundle
                // bytes. Kept out of the yield path so it can be guarded by try/catch (C# forbids yield
                // inside a try that has a catch).
                try
                {
                    bytes = System.IO.File.ReadAllBytes(file.fullPath);
                    a = MuParser.Parse(file.parent.url, bytes, bytes.Length);
                    cm = compiler.Compile(file.url, file.parent.url, bytes, bytes.Length);
                    if (cm.Failed)
                        fail = "compile failed: " + cm.FailureMessage;
                    else
                        // Null when the model has no static meshes at all (nothing to bundle).
                        bundleBytes = MeshBundleBuilder.BuildMany(cm.Blobs);
                }
                catch (Exception e)
                {
                    fail = "exception (build): " + e.GetType().Name + ": " + e.Message;
                }

                // Phase B (async): load the mesh bundle. Outside try/catch (contains a yield).
                if (fail == null && bundleBytes != null)
                {
                    AssetBundleCreateRequest createReq = AssetBundle.LoadFromMemoryAsync(bundleBytes);
                    yield return createReq;
                    bundle = createReq.assetBundle;
                    if (bundle == null)
                        fail = "AssetBundle.LoadFromMemoryAsync returned a null bundle";
                }

                // Phase C (async): place each loaded mesh into its locals slot.
                if (fail == null)
                {
                    locals = new UnityEngine.Object[cm.LocalCount];
                    if (bundle != null)
                    {
                        MeshBinding[] bindings = cm.Bindings;
                        for (int i = 0; i < bindings.Length; i++)
                        {
                            AssetBundleRequest lr = bundle.LoadAssetAsync<Mesh>(bindings[i].CanonicalName);
                            yield return lr;
                            locals[bindings[i].Slot] = lr.asset;
                        }
                    }
                }

                // Phase D (synchronous, may throw): replay the instructions and diff a vs b.
                if (fail == null)
                {
                    try
                    {
                        IModelInstruction[] ins = cm.Instructions;
                        for (int i = 0; i < ins.Length; i++)
                            ins[i].Execute(locals);
                        b = locals.Length > 0 ? locals[0] as GameObject : null;
                        mismatch = DiffModels(a, b);
                    }
                    catch (Exception e)
                    {
                        fail = "exception (replay/diff): " + e.GetType().Name + ": " + e.Message +
                               "\n" + e.StackTrace;
                    }
                }

                // Surface any diagnostics the compiler buffered off-thread (main thread only).
                if (cm != null)
                    cm.FlushLogs();

                if (fail == null && mismatch == null)
                {
                    passed++;
                    Debug.Log($"{Tag} PASS {url}");
                }
                else
                {
                    string reason = fail ?? mismatch;
                    failures.Add(url + ": " + reason);
                    Debug.LogError($"{Tag} FAIL {url}: {reason}");
                }

                // Cleanup. b's meshes are owned by the bundle and freed by Unload(true); the oracle's
                // meshes are standalone Unity objects that would otherwise leak, so destroy them too.
                if (a != null)
                {
                    DestroyOracleMeshes(a);
                    UnityEngine.Object.Destroy(a);
                }
                if (b != null)
                    UnityEngine.Object.Destroy(b);
                // A partial tree left behind by an exception mid-replay (b never assigned) is still rooted
                // at locals[0]; destroy it so it doesn't leak.
                else if (locals != null && locals.Length > 0 && locals[0] is GameObject partial)
                    UnityEngine.Object.Destroy(partial);
                if (bundle != null)
                    bundle.Unload(true);

                yield return null; // spread the work across frames to avoid a long main-menu hitch
            }

            MuParser.ReleaseBuffers();

            Debug.Log($"{Tag} SUMMARY: {passed}/{total} passed");
            if (failures.Count > 0)
            {
                Debug.LogError($"{Tag} {failures.Count} FAILED:");
                for (int i = 0; i < failures.Count; i++)
                    Debug.LogError($"{Tag}   {failures[i]}");
            }
        }

        static bool ContainsKeyword(string url)
        {
            for (int i = 0; i < ComplexKeywords.Length; i++)
                if (url.IndexOf(ComplexKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // ---- Diff: hierarchy then meshes ---------------------------------------------------------

        /// <summary>Returns the FIRST mismatch between the oracle tree <paramref name="a"/> and the new-path
        /// tree <paramref name="b"/>, or null if they are semantically identical. Hierarchy divergence is
        /// reported first (mesh matching is meaningless once structure diverges).</summary>
        static string DiffModels(GameObject a, GameObject b)
        {
            if (a == null && b == null) return "both roots null (nothing built)";
            if (a == null) return "oracle (MuParser) produced a null root";
            if (b == null) return "new path produced a null root";

            var pairs = new List<NodePair>();
            string hm = DfsMatch(a.transform, b.transform, a.transform.name, pairs);
            if (hm != null)
                return "hierarchy: " + hm;

            // Structure matches, so the i-th node of each tree is the same logical node; compare meshes.
            for (int i = 0; i < pairs.Count; i++)
            {
                NodePair p = pairs[i];
                string m;

                m = CompareMeshPair(p.Path + " MeshFilter.sharedMesh",
                    Sm(p.A.GetComponent<MeshFilter>()), Sm(p.B.GetComponent<MeshFilter>()));
                if (m != null) return "mesh: " + m;

                m = CompareMeshPair(p.Path + " MeshCollider.sharedMesh",
                    Sm(p.A.GetComponent<MeshCollider>()), Sm(p.B.GetComponent<MeshCollider>()));
                if (m != null) return "mesh: " + m;

                SkinnedMeshRenderer sa = p.A.GetComponent<SkinnedMeshRenderer>();
                SkinnedMeshRenderer sb = p.B.GetComponent<SkinnedMeshRenderer>();
                if (sa != null || sb != null)
                {
                    m = CompareMeshPair(p.Path + " SkinnedMeshRenderer.sharedMesh",
                        sa != null ? sa.sharedMesh : null, sb != null ? sb.sharedMesh : null);
                    if (m != null) return "mesh: " + m;

                    m = CompareBones(p.Path, sa, sb);
                    if (m != null) return "skin: " + m;
                }
            }

            return null;
        }

        /// <summary>Parallel DFS. Verifies name, local TRS, and the tracked component-type multiset at each
        /// node, and that child counts match, recording each matched (a,b) node pair for the mesh pass.
        /// Returns the first structural mismatch (with a path), or null.</summary>
        static string DfsMatch(Transform a, Transform b, string path, List<NodePair> pairs)
        {
            if (a.name != b.name)
                return $"{path}: name '{a.name}' != '{b.name}'";
            if ((a.localPosition - b.localPosition).sqrMagnitude > VecEpsSq)
                return $"{path}: localPosition {S(a.localPosition)} != {S(b.localPosition)}";
            if (1f - Mathf.Abs(Quaternion.Dot(a.localRotation, b.localRotation)) > QuatEps)
                return $"{path}: localRotation {S(a.localRotation)} != {S(b.localRotation)}";
            if ((a.localScale - b.localScale).sqrMagnitude > VecEpsSq)
                return $"{path}: localScale {S(a.localScale)} != {S(b.localScale)}";

            string sigA = ComponentSig(a.gameObject);
            string sigB = ComponentSig(b.gameObject);
            if (sigA != sigB)
                return $"{path}: components [{sigA}] != [{sigB}]";

            pairs.Add(new NodePair(a, b, path));

            if (a.childCount != b.childCount)
                return $"{path}: childCount {a.childCount} != {b.childCount}";

            for (int i = 0; i < a.childCount; i++)
            {
                Transform ca = a.GetChild(i);
                Transform cb = b.GetChild(i);
                string m = DfsMatch(ca, cb, path + "/" + ca.name, pairs);
                if (m != null)
                    return m;
            }

            return null;
        }

        static string ComponentSig(GameObject go)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Tracked.Length; i++)
            {
                int count = go.GetComponents(Tracked[i].Type).Length;
                if (count > 0)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(Tracked[i].Name).Append(':').Append(count);
                }
            }
            return sb.ToString();
        }

        static Mesh Sm(MeshFilter c) => c != null ? c.sharedMesh : null;
        static Mesh Sm(MeshCollider c) => c != null ? c.sharedMesh : null;

        /// <summary>Compares a matched pair of meshes, handling the null cases. Returns null when both are
        /// absent or both compare equal; otherwise the first mismatch (prefixed with <paramref name="prefix"/>).</summary>
        static string CompareMeshPair(string prefix, Mesh a, Mesh b)
        {
            bool aNull = a == null;
            bool bNull = b == null;
            if (aNull && bNull) return null;
            if (aNull != bNull)
                return $"{prefix}: present on oracle={!aNull}, on new={!bNull}";
            string m = CompareMesh(a, b);
            return m == null ? null : prefix + ": " + m;
        }

        /// <summary>Field-by-field mesh comparison in a fixed order; returns the first differing field
        /// (name + index + values) or null. Bind poses / bone weights are compared whenever EITHER mesh
        /// carries them — that is the load-bearing skinned check (equality proves Unity deserialized the
        /// BlendWeights/BlendIndices channels 12/13 correctly).</summary>
        static string CompareMesh(Mesh a, Mesh b)
        {
            if (a.vertexCount != b.vertexCount)
                return $"vertexCount {a.vertexCount} != {b.vertexCount}";

            string m;
            if ((m = CmpV3("vertices", a.vertices, b.vertices)) != null) return m;
            if ((m = CmpV3("normals", a.normals, b.normals)) != null) return m;
            if ((m = CmpV4("tangents", a.tangents, b.tangents)) != null) return m;
            if ((m = CmpC32("colors32", a.colors32, b.colors32)) != null) return m;
            if ((m = CmpV2("uv", a.uv, b.uv)) != null) return m;
            if ((m = CmpV2("uv2", a.uv2, b.uv2)) != null) return m;

            if (a.subMeshCount != b.subMeshCount)
                return $"subMeshCount {a.subMeshCount} != {b.subMeshCount}";
            for (int s = 0; s < a.subMeshCount; s++)
                if ((m = CmpInt($"triangles[{s}]", a.GetTriangles(s), b.GetTriangles(s))) != null) return m;

            // Looser: oracle uses RecalculateBounds, new path computes its own AABB from the same verts.
            if ((m = CmpBounds("bounds", a.bounds, b.bounds)) != null) return m;

            Matrix4x4[] abp = a.bindposes;
            Matrix4x4[] bbp = b.bindposes;
            if (abp.Length > 0 || bbp.Length > 0)
                if ((m = CmpMat("bindposes", abp, bbp)) != null) return m;

            BoneWeight[] abw = a.boneWeights;
            BoneWeight[] bbw = b.boneWeights;
            if (abw.Length > 0 || bbw.Length > 0)
                if ((m = CmpBoneWeights("boneWeights", abw, bbw)) != null) return m;

            return null;
        }

        /// <summary>Compares the resolved <c>SkinnedMeshRenderer.bones</c> (count + bound transform names in
        /// order) — confirms ResolveBones/AffectSkinnedMeshRenderersBones bound the same bones.</summary>
        static string CompareBones(string path, SkinnedMeshRenderer a, SkinnedMeshRenderer b)
        {
            Transform[] ba = a != null ? a.bones : Array.Empty<Transform>();
            Transform[] bb = b != null ? b.bones : Array.Empty<Transform>();
            if (ba.Length != bb.Length)
                return $"{path} SMR.bones length {ba.Length} != {bb.Length}";
            for (int i = 0; i < ba.Length; i++)
            {
                string na = ba[i] != null ? ba[i].name : "<null>";
                string nb = bb[i] != null ? bb[i].name : "<null>";
                if (na != nb)
                    return $"{path} SMR.bones[{i}] '{na}' != '{nb}'";
            }
            return null;
        }

        // ---- Field comparison helpers (return the first mismatch string, or null) ----------------

        static string CmpV3(string n, Vector3[] a, Vector3[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if ((a[i] - b[i]).sqrMagnitude > VecEpsSq) return $"{n}[{i}] {S(a[i])} != {S(b[i])}";
            return null;
        }

        static string CmpV4(string n, Vector4[] a, Vector4[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if ((a[i] - b[i]).sqrMagnitude > VecEpsSq) return $"{n}[{i}] {S(a[i])} != {S(b[i])}";
            return null;
        }

        static string CmpV2(string n, Vector2[] a, Vector2[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if ((a[i] - b[i]).sqrMagnitude > VecEpsSq) return $"{n}[{i}] {S(a[i])} != {S(b[i])}";
            return null;
        }

        static string CmpC32(string n, Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                    return $"{n}[{i}] ({a[i].r},{a[i].g},{a[i].b},{a[i].a}) != ({b[i].r},{b[i].g},{b[i].b},{b[i].a})";
            return null;
        }

        static string CmpInt(string n, int[] a, int[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return $"{n}[{i}] {a[i]} != {b[i]}";
            return null;
        }

        static string CmpMat(string n, Matrix4x4[] a, Matrix4x4[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                for (int e = 0; e < 16; e++)
                    if (Mathf.Abs(a[i][e] - b[i][e]) > MatEps)
                        return $"{n}[{i}].e{e} {a[i][e]:R} != {b[i][e]:R}";
            return null;
        }

        static string CmpBoneWeights(string n, BoneWeight[] a, BoneWeight[] b)
        {
            if (a.Length != b.Length) return $"{n} length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
            {
                BoneWeight x = a[i];
                BoneWeight y = b[i];
                if (x.boneIndex0 != y.boneIndex0 || x.boneIndex1 != y.boneIndex1 ||
                    x.boneIndex2 != y.boneIndex2 || x.boneIndex3 != y.boneIndex3)
                    return $"{n}[{i}] indices ({x.boneIndex0},{x.boneIndex1},{x.boneIndex2},{x.boneIndex3}) != " +
                           $"({y.boneIndex0},{y.boneIndex1},{y.boneIndex2},{y.boneIndex3})";
                if (Mathf.Abs(x.weight0 - y.weight0) > WeightEps || Mathf.Abs(x.weight1 - y.weight1) > WeightEps ||
                    Mathf.Abs(x.weight2 - y.weight2) > WeightEps || Mathf.Abs(x.weight3 - y.weight3) > WeightEps)
                    return $"{n}[{i}] weights ({x.weight0:R},{x.weight1:R},{x.weight2:R},{x.weight3:R}) != " +
                           $"({y.weight0:R},{y.weight1:R},{y.weight2:R},{y.weight3:R})";
            }
            return null;
        }

        static string CmpBounds(string n, Bounds a, Bounds b)
        {
            if (!ApproxLooseV3(a.center, b.center))
                return $"{n}.center {S(a.center)} != {S(b.center)} (loose eps {BoundsEps:R})";
            if (!ApproxLooseV3(a.size, b.size))
                return $"{n}.size {S(a.size)} != {S(b.size)} (loose eps {BoundsEps:R})";
            return null;
        }

        // Relative-aware loose tolerance so far-from-origin bounds don't false-positive on float rounding.
        static bool ApproxLoose(float x, float y)
        {
            float tol = Mathf.Max(BoundsEps, 1e-4f * Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
            return Mathf.Abs(x - y) <= tol;
        }

        static bool ApproxLooseV3(Vector3 a, Vector3 b) =>
            ApproxLoose(a.x, b.x) && ApproxLoose(a.y, b.y) && ApproxLoose(a.z, b.z);

        // ---- Formatting / cleanup ---------------------------------------------------------------

        static string S(Vector2 v) => $"({v.x:F5},{v.y:F5})";
        static string S(Vector3 v) => $"({v.x:F5},{v.y:F5},{v.z:F5})";
        static string S(Vector4 v) => $"({v.x:F5},{v.y:F5},{v.z:F5},{v.w:F5})";
        static string S(Quaternion q) => $"({q.x:F5},{q.y:F5},{q.z:F5},{q.w:F5})";

        static void DestroyOracleMeshes(GameObject root)
        {
            var meshes = new HashSet<Mesh>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
            foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null) meshes.Add(mc.sharedMesh);
            foreach (SkinnedMeshRenderer sr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sr.sharedMesh != null) meshes.Add(sr.sharedMesh);
            foreach (Mesh m in meshes)
                UnityEngine.Object.Destroy(m);
        }

        /// <summary>A structurally-matched pair of nodes (same logical transform in each tree) plus its
        /// diagnostic path, collected during <see cref="DfsMatch"/> for the mesh comparison pass.</summary>
        private readonly struct NodePair
        {
            public readonly Transform A;
            public readonly Transform B;
            public readonly string Path;

            public NodePair(Transform a, Transform b, string path)
            {
                A = a;
                B = b;
                Path = path;
            }
        }
    }
}
#endif
