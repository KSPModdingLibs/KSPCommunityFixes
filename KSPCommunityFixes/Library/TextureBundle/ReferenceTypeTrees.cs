using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// The verbatim serialized type-tree entries for the classes the texture-bundle writer emits
    /// (<c>Texture2D</c> and <c>AssetBundle</c>), loaded once from the embedded
    /// <c>TextureTypeTrees.bin</c> artifact. <see cref="SerializedFileWriter"/> copies each entry
    /// straight into the bundles it generates and enables the type tree, so Unity deserializes the
    /// objects from the embedded tree rather than its compiled-in layout.
    /// </summary>
    /// <remarks>
    /// The artifact is produced offline (see <c>Tools/GenerateTextureTypeTrees.py</c>) by
    /// extracting the two type entries from a Unity-generated reference bundle, so it needs no
    /// runtime bundle parsing. Its format (all little-endian) is: magic "KCTT", u32 version, an
    /// int32-length-prefixed UTF-8 unity version string, an int32 entry count, then per entry an
    /// int32 class id, an int32 byte length and that many raw type-entry bytes.
    /// </remarks>
    internal static class ReferenceTypeTrees
    {
        const string ResourceSuffix = "TextureTypeTrees.bin";
        static readonly byte[] Magic = { (byte)'K', (byte)'C', (byte)'T', (byte)'T' };
        const uint SupportedVersion = 1;

        sealed class Data
        {
            public readonly Dictionary<int, byte[]> ByClassId = new Dictionary<int, byte[]>();
            public string UnityVersion;
        }

        static readonly Lazy<Data> data = new Lazy<Data>(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The engine version string of the reference artifact (e.g. "2019.4.18f1").</summary>
        public static string UnityVersion => data.Value.UnityVersion;

        /// <summary>The verbatim type-entry bytes for a class, to copy into a generated file's type list.</summary>
        public static byte[] TypeEntry(int classId)
        {
            if (data.Value.ByClassId.TryGetValue(classId, out var entry))
                return entry;
            throw new InvalidOperationException(
                $"the embedded texture type-tree artifact has no type tree for class {classId}"
            );
        }

        static Data Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = null;
            foreach (var candidate in asm.GetManifestResourceNames())
                if (candidate.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = candidate;
                    break;
                }
            if (name is null)
                throw new InvalidOperationException(
                    $"embedded texture type-tree artifact ('{ResourceSuffix}') not found"
                );

            using (var stream = asm.GetManifestResourceStream(name))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false))
            {
                var magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length
                    || magic[0] != Magic[0] || magic[1] != Magic[1]
                    || magic[2] != Magic[2] || magic[3] != Magic[3])
                    throw new InvalidDataException("texture type-tree artifact has a bad magic");

                uint version = reader.ReadUInt32();
                if (version != SupportedVersion)
                    throw new InvalidDataException(
                        $"texture type-tree artifact version {version} is unsupported"
                    );

                var result = new Data();

                int unityVersionLength = reader.ReadInt32();
                result.UnityVersion = Encoding.UTF8.GetString(reader.ReadBytes(unityVersionLength));

                int count = reader.ReadInt32();
                for (int i = 0; i < count; ++i)
                {
                    int classId = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    result.ByClassId[classId] = reader.ReadBytes(length);
                }

                return result;
            }
        }
    }
}
