using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KSPCommunityFixes.Library
{
    public static class DDSParser
    {
        /// <summary>
        /// Return an Unity GraphicsFormat matching the provided DXGI_FORMAT, or GraphicsFormat.None if the format isn't supported
        /// </summary>
        public static GraphicsFormat DXGIFormatToGraphicsFormat(DXGI_FORMAT dxgiFormat)
        {
            switch (dxgiFormat)
            {
                case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM: return GraphicsFormat.RGBA_DXT1_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB: return GraphicsFormat.RGBA_DXT1_SRGB;
                case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM: return GraphicsFormat.RGBA_DXT5_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB: return GraphicsFormat.RGBA_DXT5_SRGB;
                case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM: return GraphicsFormat.R_BC4_SNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM: return GraphicsFormat.R_BC4_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM: return GraphicsFormat.RG_BC5_SNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM: return GraphicsFormat.RG_BC5_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM: return GraphicsFormat.RGBA_BC7_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB: return GraphicsFormat.RGBA_BC7_SRGB;
                case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16: return GraphicsFormat.RGB_BC6H_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16: return GraphicsFormat.RGB_BC6H_UFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM: return GraphicsFormat.R16G16B16A16_UNorm;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM: return GraphicsFormat.R16G16B16A16_SNorm;
                case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT: return GraphicsFormat.R16_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT: return GraphicsFormat.R16G16_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT: return GraphicsFormat.R16G16B16A16_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT: return GraphicsFormat.R32_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT: return GraphicsFormat.R32G32_SFloat;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT: return GraphicsFormat.R32G32B32A32_SFloat;
                default: return GraphicsFormat.None;
            }
        }

        /// <summary>
        /// Return an Unity GraphicsFormat matching the provided FourCC format, or GraphicsFormat.None if the format isn't supported
        /// </summary>
        public static GraphicsFormat FourCCFormatToGraphicsFormat(FourCC fourCCFormat)
        {
            switch (fourCCFormat)
            {
                case FourCC.DXT1: return GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT1, true);
                case FourCC.DXT5: return GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT5, true);
                case FourCC.BC4U_ATI: return GraphicsFormat.R_BC4_UNorm;
                case FourCC.BC4U: return GraphicsFormat.R_BC4_UNorm;
                case FourCC.BC4S: return GraphicsFormat.R_BC4_SNorm;
                case FourCC.BC5U_ATI: return GraphicsFormat.RG_BC5_UNorm;
                case FourCC.BC5U: return GraphicsFormat.RG_BC5_UNorm;
                case FourCC.BC5S: return GraphicsFormat.RG_BC5_SNorm;
                case FourCC.R16G16B16A16_UNORM: return GraphicsFormat.R16G16B16A16_UNorm;
                case FourCC.R16G16B16A16_SNORM: return GraphicsFormat.R16G16B16A16_SNorm;
                case FourCC.R16_FLOAT: return GraphicsFormat.R16_SFloat;
                case FourCC.R16G16_FLOAT: return GraphicsFormat.R16G16_SFloat;
                case FourCC.R16G16B16A16_FLOAT: return GraphicsFormat.R16G16B16A16_SFloat;
                case FourCC.R32_FLOAT: return GraphicsFormat.R32_SFloat;
                case FourCC.R32G32_FLOAT: return GraphicsFormat.R32G32_SFloat;
                case FourCC.R32G32B32A32_FLOAT: return GraphicsFormat.R32G32B32A32_SFloat;
                default: return GraphicsFormat.None;
            }
        }

        public unsafe struct DDSLoadRequest : IDisposable
        {
            private const int DDSFileMagicNumber = 0x20534444;
            private const int DDSFileMinLength = DDS_HEADER.Length + DDSHeaderOffset;
            private const int DDSHeaderOffset = 4;
            private const int DXT10HeaderOffset = DDS_HEADER.Length + DDSHeaderOffset;

            public enum Result { NotLoaded, Loaded, ErrorInvalidFile, ErrorLoading }

            public readonly byte* data;
            public readonly int dataOffset;
            public readonly int dataLength;
            public readonly bool uploadToVRAM;
            public readonly bool keepInRAM;

            private GCHandle pinnedArrayHandle;

            private Texture2D texture;
            private Result result;
            private string error;

            public readonly bool IsValidFile => result != Result.ErrorInvalidFile;
            public readonly bool LoadRequested => result != Result.Loaded && result != Result.ErrorInvalidFile;
            public readonly bool ErrorLoading => result == Result.ErrorLoading;

            public readonly string ErrorMessage => error;

            private readonly DDS_HEADER* DDSHeaderUnsafe => (DDS_HEADER*)(data + DDSHeaderOffset);
            private readonly DDS_HEADER_DXT10* DXT10HeaderUnsafe => (DDS_HEADER_DXT10*)(data + DXT10HeaderOffset);

            public readonly Texture2D Texture => result == Result.Loaded ? texture : null;

            /// <summary>
            /// Create a request for loading a DDS file
            /// </summary>
            /// <param name="ddsFileData">The file data</param>
            /// <param name="dataOffset">The index at which the file data starts in the provided byte array</param>
            /// <param name="dataLength">The length of the file data in the provided byte array</param>
            /// <param name="keepInRAM">If set to true, a copy of the texture will be kept available in </param>
            public DDSLoadRequest(byte[] ddsFileData, int dataOffset, int dataLength, bool uploadToVRAM = true, bool keepInRAM = false)
            {
                pinnedArrayHandle = GCHandle.Alloc(ddsFileData, GCHandleType.Pinned);
                this.data = (byte*)pinnedArrayHandle.AddrOfPinnedObject();
                this.dataOffset = dataOffset;
                this.dataLength = dataLength;
                this.uploadToVRAM = uploadToVRAM;
                this.keepInRAM = keepInRAM;
                texture = null;
                result = IsValidDDSFile(ddsFileData, data, dataOffset, dataLength, out error);
            }

            public DDSLoadRequest(byte[] ddsFileData, bool uploadToVRAM = true, bool keepInRAM = false)
            {
                pinnedArrayHandle = GCHandle.Alloc(ddsFileData, GCHandleType.Pinned);
                this.data = (byte*)pinnedArrayHandle.AddrOfPinnedObject();
                this.dataOffset = 0;
                this.dataLength = ddsFileData.Length;
                this.uploadToVRAM = uploadToVRAM;
                this.keepInRAM = keepInRAM;
                texture = null;
                result = IsValidDDSFile(ddsFileData, data, dataOffset, dataLength, out error);
            }

            public DDSLoadRequest(byte* ddsFileData, int dataOffset, int dataLength, bool uploadToVRAM = true, bool keepInRAM = false)
            {
                this.data = ddsFileData;
                pinnedArrayHandle = default;
                this.dataOffset = dataOffset;
                this.dataLength = dataLength;
                this.uploadToVRAM = uploadToVRAM;
                this.keepInRAM = keepInRAM;
                texture = null;
                result = IsValidDDSFile(null, data, dataOffset, dataLength, out error);
            }

            public void Dispose()
            {
                if (pinnedArrayHandle.IsAllocated)
                    pinnedArrayHandle.Free();
            }

            private static unsafe Result IsValidDDSFile(byte[] dataArray, byte* data, int startIndex, int length, out string error)
            {
                if (dataArray != null && dataArray.Length < length + startIndex)
                {
                    error = $"ddsFileData length of {dataArray.Length} is smaller than requested length";
                    return Result.ErrorInvalidFile;
                }

                if (length < DDSFileMinLength)
                {
                    error = "Invalid DDS file (file is too small)";
                    return Result.ErrorInvalidFile;
                }

                if (*(int*)(data + startIndex) != DDSFileMagicNumber)
                {
                    error = "Invalid DDS file";
                    return Result.ErrorInvalidFile;
                }

                error = null;
                return Result.NotLoaded;
            }

            private bool SetLoadingError(string error)
            {
                result = Result.ErrorLoading;
                this.error = error;
                return false;
            }

            public unsafe bool TryLoad()
            {
                if (result != Result.NotLoaded)
                    return false;

                GraphicsFormat graphicsFormat;
                DDS_HEADER* ddsHeader = DDSHeaderUnsafe;
                DDS_HEADER_DXT10* dxt10Header;

                int textureDataOffset = dataOffset + DXT10HeaderOffset;
                int textureDataLength = dataLength - DXT10HeaderOffset;

                if (ddsHeader->HasDXT10Header)
                {
                    if (dataLength - DXT10HeaderOffset < DDS_HEADER_DXT10.Length)
                        return SetLoadingError("Invalid DXT10 header");

                    dxt10Header = DXT10HeaderUnsafe;
                    textureDataOffset += DDS_HEADER_DXT10.Length;
                    textureDataLength -= DDS_HEADER_DXT10.Length;
                    graphicsFormat = DXGIFormatToGraphicsFormat(dxt10Header->dxgiFormat);
                    if (graphicsFormat == GraphicsFormat.None)
                        return SetLoadingError($"Unsupported DXGI format: {dxt10Header->dxgiFormat}");
                }
                else
                {
                    graphicsFormat = FourCCFormatToGraphicsFormat(ddsHeader->ddspf.dwFourCC);
                    if (graphicsFormat == GraphicsFormat.None)
                        return SetLoadingError($"Unsupported DDS format: {ddsHeader->ddspf.dwFourCC}");
                }

                if (!SystemInfo.IsFormatSupported(graphicsFormat, FormatUsage.Sample))
                {
                    if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX &&
                        (graphicsFormat == GraphicsFormat.RGBA_BC7_UNorm
                         || graphicsFormat == GraphicsFormat.RGBA_BC7_SRGB
                         || graphicsFormat == GraphicsFormat.RGB_BC6H_SFloat
                         || graphicsFormat == GraphicsFormat.RGB_BC6H_UFloat))
                        return SetLoadingError($"The '{graphicsFormat}' format is not supported on MacOS");

                    return SetLoadingError($"The '{graphicsFormat}' format is not supported by your GPU or environment");
                }

                bool hasMipMaps = ddsHeader->HasFlag(DDSCAPS.MIPMAP);

                texture = new Texture2D((int)ddsHeader->dwWidth, (int)ddsHeader->dwHeight, graphicsFormat, hasMipMaps ? TextureCreationFlags.MipChain : TextureCreationFlags.None);

                if (texture.IsNullOrDestroyed())
                    return SetLoadingError("Unknown error while creating texture");

                IntPtr textureData = (IntPtr)(data + textureDataOffset);

                try
                {
                    texture.LoadRawTextureData(textureData, textureDataLength);
                    if (uploadToVRAM)
                        texture.Apply(updateMipmaps: false, makeNoLongerReadable: !keepInRAM);
                    
                }
                catch (Exception e)
                {
                    return SetLoadingError(e.Message);
                }

                result = Result.Loaded;
                return true;
            }

            public readonly bool IsKSPNormalMap()
            {
                if (!IsValidFile)
                    return false;

                DDS_HEADER* ddsHeader = DDSHeaderUnsafe;
                return ddsHeader->ddspf.HasFlag(DDPF.ATI_NORMALMAP) || ddsHeader->ddspf.HasFlag(DDPF.NVTT_NORMALMAP);
            }

            public readonly bool HasMipMaps()
            {
                if (!IsValidFile)
                    return false;

                return DDSHeaderUnsafe->HasFlag(DDSCAPS.MIPMAP);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public unsafe struct DDS_HEADER
        {
            /// <summary>
            /// Size of structure in bytes.
            /// </summary>
            public const int Length = 124;

            /// <summary>
            /// Check if a specific <see cref="DDSD"/> flag is set in <see cref="dwFlags"/>
            /// </summary>
            public bool HasFlag(DDSD flag) => (dwFlags & flag) != 0;

            /// <summary>
            /// Check if a specific <see cref="DDSCAPS"/> flag is set in <see cref="dwCaps"/>
            /// </summary>
            public bool HasFlag(DDSCAPS flag) => (dwCaps & flag) != 0;

            /// <summary>
            /// Check if a specific <see cref="DDSCAPS2"/> flag is set in <see cref="dwCaps2"/>
            /// </summary>
            public bool HasFlag(DDSCAPS2 flag) => (dwCaps2 & flag) != 0;

            public bool HasDXT10Header => ddspf.dwFourCC == FourCC.DX10;

            /// <summary>
            /// Size of structure in bytes. This member must be set to 124.
            /// </summary>
            public uint dwSize;
            /// <summary>
            /// Flags to indicate which members contain valid data.
            /// You should not rely on the DDSD_CAPS, DDSD_PIXELFORMAT, and DDSD_MIPMAPCOUNT flags 
            /// being set because some writers might not set these flags.
            /// </summary>
            public DDSD dwFlags;
            /// <summary>
            /// Surface height (in pixels).
            /// </summary>
            public uint dwHeight;
            /// <summary>
            /// Surface width (in pixels).
            /// </summary>
            public uint dwWidth;
            /// <summary>
            /// The pitch or number of bytes per scan line in an uncompressed texture; 
            /// the total number of bytes in the top level texture for a compressed texture.
            /// </summary>
            public uint dwPitchOrLinearSize;
            /// <summary>
            /// Depth of a volume texture (in pixels), otherwise unused.
            /// </summary>
            public uint dwDepth;
            /// <summary>
            /// Number of mipmap levels, otherwise unused.
            /// </summary>
            public uint dwMipMapCount;
            /// <summary>
            /// Unused
            /// </summary>
            public fixed uint dwReserved1[11];
            /// <summary>
            /// The pixel format
            /// </summary>
            public DDS_PIXELFORMAT ddspf;
            /// <summary>
            /// Specifies the complexity of the surfaces stored.
            /// </summary>
            public DDSCAPS dwCaps;
            /// <summary>
            /// Additional detail about the surfaces stored.
            /// </summary>
            public DDSCAPS2 dwCaps2;
            /// <summary>
            /// Unused.
            /// </summary>
            public uint dwCaps3;
            /// <summary>
            /// Unused.
            /// </summary>
            public uint dwCaps4;
            /// <summary>
            /// Unused.
            /// </summary>
            public uint dwReserved2;
        }

        [Flags]
        public enum DDSD : uint
        {
            /// <summary>
            /// Always required. Might not be set because of non-compliant writers.
            /// </summary>
            CAPS = 0x1u,
            /// <summary>
            /// Always required.
            /// </summary>
            HEIGHT = 0x2u,
            /// <summary>
            /// Always required.
            /// </summary>
            WIDTH = 0x4u,
            /// <summary>
            /// Set when pitch is provided for an uncompressed texture.
            /// </summary>
            PITCH = 0x8u,
            /// <summary>
            /// Always required. Might not be set because of non-compliant writers.
            /// </summary>
            PIXELFORMAT = 0x1000u,
            /// <summary>
            /// Required in a mipmapped texture. Might not be set because of non-compliant writers.
            /// </summary>
            MIPMAPCOUNT = 0x20000u,
            /// <summary>
            /// Required when pitch is provided for a compressed texture.
            /// </summary>
            LINEARSIZE = 0x80000u,
            /// <summary>
            /// Required in a depth texture.
            /// </summary>
            DEPTH = 0x800000
        }

        /// <summary>
        /// Specifies the complexity of the surfaces stored.
        /// </summary>
        [Flags]
        public enum DDSCAPS : uint
        {
            /// <summary>
            /// Optional; must be used on any file that contains more than one surface 
            /// (a mipmap, a cubic environment map, or mipmapped volume texture).
            /// Might not be set because of non-compliant writers.
            /// </summary>
            COMPLEX = 0x8u,
            /// <summary>
            /// Optional; should be used for a mipmap.
            /// </summary>
            MIPMAP = 0x400000u,
            /// <summary>
            /// Required. Might not be set because of non-compliant writers.
            /// </summary>
            TEXTURE = 0x1000u
        }

        /// <summary>
        /// Additional detail about the surfaces stored.
        /// </summary>
        [Flags]
        public enum DDSCAPS2 : uint
        {
            CUBEMAP = 0x200u,
            CUBEMAP_POSITIVEX = 0x400u,
            CUBEMAP_NEGATIVEX = 0x800u,
            CUBEMAP_POSITIVEY = 0x1000u,
            CUBEMAP_NEGATIVEY = 0x2000u,
            CUBEMAP_POSITIVEZ = 0x4000u,
            CUBEMAP_NEGATIVEZ = 0x8000u,
            CUBEMAP_VOLUME = 0x200000u
        }

        /// <summary>
        /// Surface pixel format
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct DDS_PIXELFORMAT
        {
            /// <summary>
            /// Check if a specific <see cref="DDPF"/> flag is set in <see cref="dwFlags"/>
            /// </summary>
            public bool HasFlag(DDPF flag) => (dwFlags & flag) != 0;

            /// <summary> 
            /// Size of structure in bytes. This member must be set to 32.
            /// </summary>
            public uint dwSize;
            /// <summary> 
            /// Values which indicate what type of data is in the surface.
            /// </summary>
            public DDPF dwFlags;
            /// <summary> 
            /// Four-character codes for specifying compressed or custom formats.
            /// </summary>
            public FourCC dwFourCC;
            /// <summary> 
            /// Number of bits in an RGB (possibly including alpha) format. 
            /// Valid when dwFlags includes DDPF_RGB, DDPF_LUMINANCE, or DDPF_YUV.
            /// </summary>
            public uint dwRGBBitCount;
            /// <summary>
            /// Red (or luminance or Y) mask for reading color data. 
            /// For instance, given the A8R8G8B8 format, the red mask would be 0x00ff0000.
            /// </summary>
            public uint dwRBitMask;
            /// <summary>
            /// Green (or U) mask for reading color data. 
            /// For instance, given the A8R8G8B8 format, the green mask would be 0x0000ff00.
            /// </summary>
            public uint dwGBitMask;
            /// <summary>
            /// Blue (or V) mask for reading color data. 
            /// For instance, given the A8R8G8B8 format, the blue mask would be 0x000000ff.
            /// </summary>
            public uint dwBBitMask;
            /// <summary>
            /// Alpha mask for reading alpha data. dwFlags must include DDPF_ALPHAPIXELS or DDPF_ALPHA. 
            /// For instance, given the A8R8G8B8 format, the alpha mask would be 0xff000000.
            /// </summary>
            public uint dwABitMask;
        }

        /// <summary> 
        /// Values which indicate what type of data is in the surface.
        /// </summary>
        [Flags]
        public enum DDPF : uint
        {
            /// <summary> 
            /// Texture contains alpha data; 
            /// dwRGBAlphaBitMask contains valid data.
            /// </summary>
            ALPHAPIXELS = 0x1u,
            /// <summary> 
            /// Used in some older DDS files for alpha channel only uncompressed data 
            /// (dwRGBBitCount contains the alpha channel bitcount; dwABitMask contains valid data)
            /// </summary>
            ALPHA = 0x2u,
            /// <summary> 
            /// Texture contains compressed RGB data; dwFourCC contains valid data.
            /// </summary>
            FOURCC = 0x4u,
            /// <summary>
            /// Texture contains uncompressed RGB data; 
            /// dwRGBBitCount and the RGB masks (dwRBitMask, dwGBitMask, dwBBitMask) contain valid data.
            /// </summary>
            RGB = 0x40u,
            /// <summary> 
            /// Used in some older DDS files for YUV uncompressed data 
            /// (dwRGBBitCount contains the YUV bit count; 
            /// dwRBitMask contains the Y mask, 
            /// dwGBitMask contains the U mask, 
            /// dwBBitMask contains the V mask)
            /// </summary>
            YUV = 0x200u,
            /// <summary>
            /// Used in some older DDS files for single channel color uncompressed data 
            /// (dwRGBBitCount contains the luminance channel bit count; 
            /// dwRBitMask contains the channel mask). 
            /// Can be combined with DDPF_ALPHAPIXELS for a two channel DDS file.
            /// </summary>
            LUMINANCE = 0x20000u,
            /// <summary>
            /// Non-standard : flag for normal maps (ATI Compress / AMD Compressonator)
            /// </summary>
            ATI_NORMALMAP = 0x80000u,
            /// <summary>
            /// Non-standard : flag for sRGB colorspace (Nvidia Texture Tools)
            /// </summary>
            NVTT_SRGB = 0x40000000u,
            /// <summary>
            /// Non-standard : flag for normalmap colorspace (Nvidia Texture Tools)
            /// </summary>
            NVTT_NORMALMAP = 0x80000000u
        }

        /// <summary>
        /// Four-character codes for specifying compressed or custom formats.
        /// There is no definitive list, so we only cover the ones that we can actually load in KSP.
        /// </summary>
        public enum FourCC : uint
        {
            /// <summary> Compressed 4bpp RGB or RGBA </summary>
            DXT1 = 0x31545844u,
            /// <summary> Compressed 8bpp RGBA (or normals) </summary>
            DXT5 = 0x35545844u,
            /// <summary> (actually BC4U) Single channel (R) compressed 4 bpp</summary>
            BC4U_ATI = 0x31495441u,
            /// <summary> Single channel (R) compressed 4 bpp </summary>
            BC4U = 0x55344342u,
            /// <summary> Single channel (R) compressed 4 bpp </summary>
            BC4S = 0x53344342u,
            /// <summary> (actually BC5U) 2 channels (RG) compressed 8 bpp </summary>
            BC5U_ATI = 0x32495441u,
            /// <summary> 2 channels (RG) compressed 8 bpp </summary>
            BC5U = 0x55354342u,
            /// <summary> 2 channels (RG) compressed 8 bpp </summary>
            BC5S = 0x53354342u,

            /// <summary> 4 channels (RGBA) uncompressed 64 bpp </summary>
            R16G16B16A16_UNORM = 36u,
            /// <summary> 4 channels (RGBA) uncompressed 64 bpp </summary>
            R16G16B16A16_SNORM = 110u,
            /// <summary> Floating point single channel (R) uncompressed 16 bpp </summary>
            R16_FLOAT = 111u,
            /// <summary> Floating point 2 channels (RG) uncompressed 32 bpp </summary>
            R16G16_FLOAT = 112u,
            /// <summary> Floating point 4 channels (RGBA) uncompressed 64 bpp </summary>
            R16G16B16A16_FLOAT = 113u,
            /// <summary> Floating point single channel (R) uncompressed 32 bpp </summary>
            R32_FLOAT = 114u,
            /// <summary> Floating point 2 channels (RG) uncompressed 64 bpp </summary>
            R32G32_FLOAT = 115u,
            /// <summary> Floating point 4 channels (RGBA) uncompressed 128 bpp </summary>
            R32G32B32A32_FLOAT = 116u,

            /// <summary> DXGI-specified format actually specified in DXT10 header </summary>
            DX10 = 0x30315844u,

            /// <summary> Unsupported in Unity/KSP</summary>
            DXT2 = 0x32545844u,
            /// <summary> Unsupported in Unity/KSP</summary>
            DXT3 = 0x33545844u,
            /// <summary> Unsupported in Unity/KSP</summary>
            DXT4 = 0x34545844u,
            /// <summary> Unsupported in Unity/KSP</summary>
            RGBG = 0x47424752u,
            /// <summary> Unsupported in Unity/KSP</summary>
            GRGB = 0x42475247u,
            /// <summary> Unsupported in Unity/KSP</summary>
            UYVY = 0x59565955,
            /// <summary> Unsupported in Unity/KSP</summary>
            YUY2 = 0x32595559u,
            /// <summary> Unsupported in Unity/KSP</summary>
            CxV8U8 = 117u
        }

        /// <summary>
        /// DDS header extension to handle resource arrays, 
        /// DXGI pixel formats that don't map to the legacy Microsoft DirectDraw pixel format structures, 
        /// and additional metadata.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct DDS_HEADER_DXT10
        {
            /// <summary>
            /// Size of structure in bytes.
            /// </summary>
            public const int Length = 20;

            /// <summary>
            /// The surface pixel format
            /// </summary>
            public DXGI_FORMAT dxgiFormat;
            /// <summary>
            /// Identifies the type of resource.
            /// </summary>
            public D3D10_RESOURCE_DIMENSION resourceDimension;
            /// <summary>
            /// Identifies other, less common options for resources. 
            /// </summary>
            public uint miscFlag;
            /// <summary>
            /// The number of elements in the array.
            /// For a 2D texture that is also a cube-map texture, this number represents the number of cubes.
            /// For a 3D texture, you must set this number to 1.
            /// </summary>
            public uint arraySize;
            /// <summary>
            /// Contains additional metadata (formerly was reserved). 
            /// The lower 3 bits indicate the alpha mode of the associated resource. 
            /// The upper 29 bits are reserved and are typically 0
            /// </summary>
            public uint miscFlags2;
        }

        public enum DXGI_FORMAT : uint
        {
            DXGI_FORMAT_UNKNOWN = 0u,
            DXGI_FORMAT_R32G32B32A32_TYPELESS = 1u,
            DXGI_FORMAT_R32G32B32A32_FLOAT = 2u,
            DXGI_FORMAT_R32G32B32A32_UINT = 3u,
            DXGI_FORMAT_R32G32B32A32_SINT = 4u,
            DXGI_FORMAT_R32G32B32_TYPELESS = 5u,
            DXGI_FORMAT_R32G32B32_FLOAT = 6u,
            DXGI_FORMAT_R32G32B32_UINT = 7u,
            DXGI_FORMAT_R32G32B32_SINT = 8u,
            DXGI_FORMAT_R16G16B16A16_TYPELESS = 9u,
            DXGI_FORMAT_R16G16B16A16_FLOAT = 10u,
            DXGI_FORMAT_R16G16B16A16_UNORM = 11u,
            DXGI_FORMAT_R16G16B16A16_UINT = 12u,
            DXGI_FORMAT_R16G16B16A16_SNORM = 13u,
            DXGI_FORMAT_R16G16B16A16_SINT = 14u,
            DXGI_FORMAT_R32G32_TYPELESS = 15u,
            DXGI_FORMAT_R32G32_FLOAT = 16u,
            DXGI_FORMAT_R32G32_UINT = 17u,
            DXGI_FORMAT_R32G32_SINT = 18u,
            DXGI_FORMAT_R32G8X24_TYPELESS = 19u,
            DXGI_FORMAT_D32_FLOAT_S8X24_UINT = 20u,
            DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS = 21u,
            DXGI_FORMAT_X32_TYPELESS_G8X24_UINT = 22u,
            DXGI_FORMAT_R10G10B10A2_TYPELESS = 23u,
            DXGI_FORMAT_R10G10B10A2_UNORM = 24u,
            DXGI_FORMAT_R10G10B10A2_UINT = 25u,
            DXGI_FORMAT_R11G11B10_FLOAT = 26u,
            DXGI_FORMAT_R8G8B8A8_TYPELESS = 27u,
            DXGI_FORMAT_R8G8B8A8_UNORM = 28u,
            DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29u,
            DXGI_FORMAT_R8G8B8A8_UINT = 30u,
            DXGI_FORMAT_R8G8B8A8_SNORM = 31u,
            DXGI_FORMAT_R8G8B8A8_SINT = 32u,
            DXGI_FORMAT_R16G16_TYPELESS = 33u,
            DXGI_FORMAT_R16G16_FLOAT = 34u,
            DXGI_FORMAT_R16G16_UNORM = 35u,
            DXGI_FORMAT_R16G16_UINT = 36u,
            DXGI_FORMAT_R16G16_SNORM = 37u,
            DXGI_FORMAT_R16G16_SINT = 38u,
            DXGI_FORMAT_R32_TYPELESS = 39u,
            DXGI_FORMAT_D32_FLOAT = 40u,
            DXGI_FORMAT_R32_FLOAT = 41u,
            DXGI_FORMAT_R32_UINT = 42u,
            DXGI_FORMAT_R32_SINT = 43u,
            DXGI_FORMAT_R24G8_TYPELESS = 44u,
            DXGI_FORMAT_D24_UNORM_S8_UINT = 45u,
            DXGI_FORMAT_R24_UNORM_X8_TYPELESS = 46u,
            DXGI_FORMAT_X24_TYPELESS_G8_UINT = 47u,
            DXGI_FORMAT_R8G8_TYPELESS = 48u,
            DXGI_FORMAT_R8G8_UNORM = 49u,
            DXGI_FORMAT_R8G8_UINT = 50u,
            DXGI_FORMAT_R8G8_SNORM = 51u,
            DXGI_FORMAT_R8G8_SINT = 52u,
            DXGI_FORMAT_R16_TYPELESS = 53u,
            DXGI_FORMAT_R16_FLOAT = 54u,
            DXGI_FORMAT_D16_UNORM = 55u,
            DXGI_FORMAT_R16_UNORM = 56u,
            DXGI_FORMAT_R16_UINT = 57u,
            DXGI_FORMAT_R16_SNORM = 58u,
            DXGI_FORMAT_R16_SINT = 59u,
            DXGI_FORMAT_R8_TYPELESS = 60u,
            DXGI_FORMAT_R8_UNORM = 61u,
            DXGI_FORMAT_R8_UINT = 62u,
            DXGI_FORMAT_R8_SNORM = 63u,
            DXGI_FORMAT_R8_SINT = 64u,
            DXGI_FORMAT_A8_UNORM = 65u,
            DXGI_FORMAT_R1_UNORM = 66u,
            DXGI_FORMAT_R9G9B9E5_SHAREDEXP = 67u,
            DXGI_FORMAT_R8G8_B8G8_UNORM = 68u,
            DXGI_FORMAT_G8R8_G8B8_UNORM = 69u,
            DXGI_FORMAT_BC1_TYPELESS = 70u,
            DXGI_FORMAT_BC1_UNORM = 71u,
            DXGI_FORMAT_BC1_UNORM_SRGB = 72u,
            DXGI_FORMAT_BC2_TYPELESS = 73u,
            DXGI_FORMAT_BC2_UNORM = 74u,
            DXGI_FORMAT_BC2_UNORM_SRGB = 75u,
            DXGI_FORMAT_BC3_TYPELESS = 76u,
            DXGI_FORMAT_BC3_UNORM = 77u,
            DXGI_FORMAT_BC3_UNORM_SRGB = 78u,
            DXGI_FORMAT_BC4_TYPELESS = 79u,
            DXGI_FORMAT_BC4_UNORM = 80u,
            DXGI_FORMAT_BC4_SNORM = 81u,
            DXGI_FORMAT_BC5_TYPELESS = 82u,
            DXGI_FORMAT_BC5_UNORM = 83u,
            DXGI_FORMAT_BC5_SNORM = 84u,
            DXGI_FORMAT_B5G6R5_UNORM = 85u,
            DXGI_FORMAT_B5G5R5A1_UNORM = 86u,
            DXGI_FORMAT_B8G8R8A8_UNORM = 87u,
            DXGI_FORMAT_B8G8R8X8_UNORM = 88u,
            DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89u,
            DXGI_FORMAT_B8G8R8A8_TYPELESS = 90u,
            DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91u,
            DXGI_FORMAT_B8G8R8X8_TYPELESS = 92u,
            DXGI_FORMAT_B8G8R8X8_UNORM_SRGB = 93u,
            DXGI_FORMAT_BC6H_TYPELESS = 94u,
            DXGI_FORMAT_BC6H_UF16 = 95u,
            DXGI_FORMAT_BC6H_SF16 = 96u,
            DXGI_FORMAT_BC7_TYPELESS = 97u,
            DXGI_FORMAT_BC7_UNORM = 98u,
            DXGI_FORMAT_BC7_UNORM_SRGB = 99u,
            DXGI_FORMAT_AYUV = 100u,
            DXGI_FORMAT_Y410 = 101u,
            DXGI_FORMAT_Y416 = 102u,
            DXGI_FORMAT_NV12 = 103u,
            DXGI_FORMAT_P010 = 104u,
            DXGI_FORMAT_P016 = 105u,
            DXGI_FORMAT_420_OPAQUE = 106u,
            DXGI_FORMAT_YUY2 = 107u,
            DXGI_FORMAT_Y210 = 108u,
            DXGI_FORMAT_Y216 = 109u,
            DXGI_FORMAT_NV11 = 110u,
            DXGI_FORMAT_AI44 = 111u,
            DXGI_FORMAT_IA44 = 112u,
            DXGI_FORMAT_P8 = 113u,
            DXGI_FORMAT_A8P8 = 114u,
            DXGI_FORMAT_B4G4R4A4_UNORM = 115u,
            DXGI_FORMAT_FORCE_UINT = uint.MaxValue
        }

        /// <summary>
        /// Identifies the type of resource being used.
        /// </summary>
        public enum D3D10_RESOURCE_DIMENSION : uint
        {
            /// <summary>
            /// Resource is of unknown type.
            /// </summary>
            UNKNOWN = 0,
            /// <summary>
            /// Resource is a buffer.
            /// </summary>
            BUFFER = 1,
            /// <summary>
            /// Resource is a 1D texture.
            /// </summary>
            TEXTURE1D = 2,
            /// <summary>
            /// Resource is a 2D texture.
            /// </summary>
            TEXTURE2D = 3,
            /// <summary>
            /// Resource is a 3D texture.
            /// </summary>
            TEXTURE3D = 4
        }
    }
}