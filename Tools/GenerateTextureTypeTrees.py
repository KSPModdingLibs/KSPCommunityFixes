#!/usr/bin/env python3
# One-time developer tool. NOT part of the build.
#
# Regenerates KSPCommunityFixes/Performance/TextureBundle/TextureTypeTrees.bin, the
# embedded artifact that supplies the invariant Unity type-tree bytes spliced into the
# in-memory asset bundles KSPCFFastLoader builds to stream DDS textures from disk.
#
# It extracts the type entries for the classes we emit (Texture2D = 28, AssetBundle = 142)
# from a proven, Unity-generated reference bundle -- the sibling KSPTextureLoader project's
# typetrees.bundle (itself produced offline from Unity's class database). Only these two
# top-level entries are needed: each carries its complete nested field tree (StreamingInfo,
# GLTextureSettings, m_Container/AssetInfo/PPtr, etc.) inline.
#
# Source bundle is an uncompressed UnityFS archive (serialized file format 21, Unity
# 2019.4.18f1). If KSP ever moves to a different Unity version, regenerate from a matching
# reference bundle.
#
# Usage:  python Tools/GenerateTextureTypeTrees.py [path-to-typetrees.bundle]
#
# Output format of TextureTypeTrees.bin (all little-endian):
#   magic   : 4 bytes  "KCTT"
#   version : u32      = 1
#   unityVersionLen : i32
#   unityVersion    : UTF-8 bytes (no terminator)
#   count   : i32
#   per entry: classId i32, length i32, <length> raw type-entry bytes

import os
import struct
import sys

# The classes whose type entries we emit into generated bundles.
WANTED = [28, 142]  # Texture2D, AssetBundle

DEFAULT_SOURCE = os.path.join(
    os.path.dirname(__file__), "..", "..",
    "KSPTextureLoader", "src", "KSPTextureLoader", "Format", "Bundle", "typetrees.bundle",
)
OUTPUT = os.path.join(
    os.path.dirname(__file__), "..",
    "KSPCommunityFixes", "Library", "TextureBundle", "TextureTypeTrees.bin",
)


class Cursor:
    def __init__(self, buf, pos=0, big_endian=True):
        self.b = buf
        self.pos = pos
        self.be = big_endian

    def u(self, n):
        v = int.from_bytes(self.b[self.pos:self.pos + n], "big" if self.be else "little")
        self.pos += n
        return v

    def i(self, n):
        v = int.from_bytes(self.b[self.pos:self.pos + n], "big" if self.be else "little", signed=True)
        self.pos += n
        return v

    def cstr(self):
        start = self.pos
        while self.b[self.pos] != 0:
            self.pos += 1
        v = self.b[start:self.pos].decode("utf-8")
        self.pos += 1
        return v

    def skip(self, n):
        self.pos += n


def extract(bundle):
    c = Cursor(bundle, big_endian=True)

    # --- UnityFS container header ---
    sig = c.cstr()
    if sig != "UnityFS":
        raise ValueError(f"not a UnityFS bundle (signature {sig!r})")
    c.u(4)              # format version (7)
    c.cstr()            # player min version
    c.cstr()            # engine revision
    c.i(8)              # total size
    c.u(4)              # blocks-info compressed size
    c.u(4)              # blocks-info uncompressed size
    flags = c.u(4)
    if (flags & 0x3F) != 0:
        raise ValueError("reference bundle must have uncompressed blocks-info")
    if c.pos % 16:      # bundle format 7 aligns the header to 16 bytes
        c.skip(16 - (c.pos % 16))

    # --- blocks-info directory ---
    c.skip(16)          # data hash
    block_count = c.i(4)
    for _ in range(block_count):
        c.u(4)          # uncompressed size
        c.u(4)          # compressed size
        block_flags = c.u(2)
        if block_flags & 0x3F:
            raise ValueError("reference bundle block must be uncompressed")
    node_count = c.i(4)
    nodes = []
    for _ in range(node_count):
        off = c.i(8)
        size = c.i(8)
        nflags = c.u(4)
        c.cstr()        # node path
        nodes.append((off, size, nflags))
    block_data_start = c.pos

    sf_nodes = [n for n in nodes if n[2] & 0x4]
    if len(sf_nodes) != 1:
        raise ValueError(f"expected exactly one serialized-file node, found {len(sf_nodes)}")
    off, size, _ = sf_nodes[0]
    sf = bundle[block_data_start + off: block_data_start + off + size]

    # --- serialized file header (big-endian) ---
    s = Cursor(sf, big_endian=True)
    s.u(4)              # metadata size
    s.u(4)              # file size
    version = s.u(4)
    if version != 21:
        raise ValueError(f"expected serialized file format 21, got {version}")
    s.u(4)              # data offset
    endian = s.u(1)
    s.skip(3)

    # --- metadata ---
    s.be = (endian == 1)
    unity_version = s.cstr()
    s.i(4)              # target platform
    enable_type_tree = s.u(1)
    if not enable_type_tree:
        raise ValueError("reference bundle must have the type tree enabled")

    type_count = s.i(4)
    entries = {}
    for _ in range(type_count):
        start = s.pos
        class_id = s.i(4)
        s.u(1)          # is stripped type
        s.i(2)          # script type index
        if class_id == 114:
            s.skip(16)  # script id (MonoBehaviour only)
        s.skip(16)      # old type hash
        node_n = s.i(4)
        str_buf = s.i(4)
        s.skip(node_n * 32)     # type-tree nodes (32 bytes each for format 21)
        s.skip(str_buf)         # string buffer
        dep_n = s.i(4)
        s.skip(dep_n * 4)       # type dependencies
        entries[class_id] = sf[start:s.pos]

    return unity_version, entries


def main():
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SOURCE
    source = os.path.abspath(source)
    if not os.path.exists(source):
        sys.exit(f"source bundle not found: {source}")

    with open(source, "rb") as f:
        bundle = f.read()

    unity_version, entries = extract(bundle)

    missing = [cid for cid in WANTED if cid not in entries]
    if missing:
        sys.exit(f"reference bundle is missing type entries for classes {missing}")

    out = bytearray()
    out += b"KCTT"
    out += struct.pack("<I", 1)                                  # format version
    uv = unity_version.encode("utf-8")
    out += struct.pack("<i", len(uv)) + uv
    out += struct.pack("<i", len(WANTED))
    for cid in WANTED:
        blob = entries[cid]
        out += struct.pack("<ii", cid, len(blob)) + blob

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "wb") as f:
        f.write(out)

    print(f"source        : {source}")
    print(f"unity version : {unity_version}")
    for cid in WANTED:
        print(f"class {cid:<4}    : {len(entries[cid])} bytes")
    print(f"wrote         : {os.path.abspath(OUTPUT)} ({len(out)} bytes)")


if __name__ == "__main__":
    main()
