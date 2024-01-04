﻿//#define BIOME_CONVERSION_VALIDATE
//#define DEBUG_BIOME_MISMATCH
//#define WITH_STOCK_BILINEAR_BUG
//#define BIOME_FLIGHTDEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace KSPCommunityFixes.Performance
{
    /// <summary>
    /// Reimplemementation of the stock biome map class (CBAttributeMapSO), fixing a bug and massively
    /// improving performance (around x17 on average, up to x30 on bodies with a large amount of biomes) when
    /// calling the GetAtt() method.
    /// Notable changes are :<br/>
    /// - Storing directly the Attribute index in the byte array (instead of a color), reducing memory usage
    /// to 8Bpp instead of 24Bpp, and making the array a lookup table instead of having to compare colors.<br/>
    /// - Fixed a bug in the stock bilinear interpolation, causing incorrect results in a specific direction
    /// on biome transitions.
    /// </summary>
    public class KSPCFFastBiomeMap : CBAttributeMapSO
    {
        private static readonly double lessThanOneDouble = StaticHelpers.BitDecrement(1.0);

        /// <summary>
        /// Return the biome definition at the given position defined in normalized [0, 1] texture coordinates,
        /// performing bilinear sampling at biomes intersections.
        /// </summary>
        private MapAttribute GetPixelBiome(double x, double y)
        {
            GetBilinearCoordinates(x, y, _width, _height, out int minX, out int maxX, out int minY, out int maxY, out double midX, out double midY);

            // Get the 4 closest pixels for bilinear interpolation
            byte b00 = _data[minX + minY * _rowWidth];
            byte b10 = _data[maxX + minY * _rowWidth];
            byte b01 = _data[minX + maxY * _rowWidth];
            byte b11 = _data[maxX + maxY * _rowWidth];

            byte biomeIndex;

            // if all 4 pixels are the same, we don't need to interpolate
            if (b00 == b10 && b00 == b01 && b00 == b11)
            {
                biomeIndex = b00;
            }
            else
            {
                // Cast to double once
                double d00 = b00;
                double d10 = b10;
                double d01 = b01;
                double d11 = b11;

                // Get bilinear value
                double d20 = d00 + (d10 - d00) * midX;
                double d21 = d01 + (d11 - d01) * midX;
                double bilinear = d20 + (d21 - d20) * midY;

                // Amongst the 4 candidates, find the closest one to the bilinear value
                biomeIndex = b00;
                double bestMagnitude = DiffMagnitude(d00, bilinear);
                double candidateMagnitude = DiffMagnitude(d10, bilinear);
                if (candidateMagnitude < bestMagnitude)
                {
                    bestMagnitude = candidateMagnitude;
                    biomeIndex = b10;
                }

                candidateMagnitude = DiffMagnitude(d01, bilinear);
                if (candidateMagnitude < bestMagnitude)
                {
                    bestMagnitude = candidateMagnitude;
                    biomeIndex = b01;
                }

                // Note : the equivalent code in the stock implementation has a bug causing
                // the last sampled pixel to be ignored, resulting in non-interpolated results
                // in a specific direction. Getting the same results as the stock implementation
                // can be achieved by commenting this last check
#if !WITH_STOCK_BILINEAR_BUG
                candidateMagnitude = DiffMagnitude(d11, bilinear);
                if (candidateMagnitude < bestMagnitude)
                {
                    biomeIndex = b11;
                }
#endif
            }

            return Attributes[biomeIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DiffMagnitude(double valA, double valB)
        {
            double diff = valA - valB;
            return diff * diff;
        }

        private const double HalfPI = Math.PI / 2.0;
        private const double DoublePI = Math.PI * 2.0;
        private const double InversePI = 1.0 / Math.PI;
        private const double InverseDoublePI = 1.0 / (2.0 * Math.PI);

        public override MapAttribute GetAtt(double lat, double lon)
        {
            // Transform lat/lon into normalized texture coordinates
            lon -= HalfPI;
            if (lon < 0.0)
                lon += DoublePI;
            lon %= DoublePI;
            double x = 1.0 - lon * InverseDoublePI;

            double y = lat * InversePI + 0.5;

            return GetPixelBiome(x, y);
        }

        public override Color GetPixelColor(double x, double y)
        {
            return GetPixelBiome(x, y).mapColor;
        }

        public override Color GetPixelColor(float x, float y)
        {
            return GetPixelBiome(x, y).mapColor;
        }

        public override Color GetPixelColor(int x, int y)
        {
            return Attributes[_data[x + y * _rowWidth]].mapColor;
        }

        public override Color GetPixelColor32(double x, double y)
        {
            return GetPixelBiome(x, y).mapColor;
        }

        public override Color GetPixelColor32(float x, float y)
        {
            return GetPixelBiome(x, y).mapColor;
        }

        public override Color32 GetPixelColor32(int x, int y)
        {
            return Attributes[_data[x + y * _rowWidth]].mapColor;
        }

        public override void CreateMap(MapDepth depth, string name, int width, int height)
        {
            _name = name;
            _width = width;
            _height = height;
            _bpp = 1;
            _rowWidth = _width;
            _data = new byte[_width * _height];
            _isCompiled = true;
        }

        /// <summary>
        /// Create a "compiled" biome map from a texture. Attributes **must** be populated prior to calling this.
        /// Note that the depth param is ignored, as we always encode biomes in a 1 Bpp array.
        /// </summary>
        public override void CreateMap(MapDepth depth, Texture2D tex)
        {
            _name = tex.name;
            _width = tex.width;
            _height = tex.height;
            _bpp = 1;
            _rowWidth = _width;
            _isCompiled = true;

            if (Attributes == null || Attributes.Length == 0)
                throw new Exception("Attributes must be populated before creating the map from a texture !");

            int biomeCount = Attributes.Length;
            RGBA32[] biomeColors = new RGBA32[biomeCount];

            for (int i = biomeCount; i-- > 0;)
                biomeColors[i] = Attributes[i].mapColor;

            int badPixelsCount = 0;
            int size = _height * _width;

            Color32[] colorData = tex.GetPixels32();
            _data = new byte[size];

            Parallel.For(0, _width, x =>
            {
                for (int y = _height; y-- > 0;)
                {
                    int biomeIndex = -1;
                    RGBA32 pixelColor = colorData[x + y * _width];
                    pixelColor.ClearAlpha();

                    for (int i = biomeCount; i-- > 0;)
                    {
                        if (biomeColors[i] == pixelColor)
                        {
                            biomeIndex = i;
                            break;
                        }
                    }

                    if (biomeIndex == -1)
                    {
                        Interlocked.Increment(ref badPixelsCount);
                        biomeIndex = GetBiomeIndexFromTexture((double)x / _width, (double)y / _height, biomeColors, colorData, _width, _height, nonExactThreshold);
                    }

                    _data[x + y * _width] = (byte)biomeIndex;
                }
            });

            if (badPixelsCount > 0)
            {
                Debug.LogWarning($"[KSPCF/FastBiomeMap] Loading {_name} : {badPixelsCount} ({(badPixelsCount / (float)size):P3}) pixels not matching a biome color, check the biome texture and biome definitions.");
            }
        }

        private static int GetBiomeIndexFromTexture(double x, double y, RGBA32[] biomeColors, Color32[] colorData, int width, int height, float nonExactThreshold)
        {
            GetBilinearCoordinates(x, y, width, height, out int minX, out int maxX, out int minY, out int maxY, out double midX, out double midY);

            // Get 4 samples pixels for bilinear interpolation
            RGBA32 c00 = GetRGBA32AtTextureCoords(minX, minY, colorData, width);
            RGBA32 c10 = GetRGBA32AtTextureCoords(maxX, minY, colorData, width);
            RGBA32 c01 = GetRGBA32AtTextureCoords(minX, maxY, colorData, width);
            RGBA32 c11 = GetRGBA32AtTextureCoords(maxX, maxY, colorData, width);

            return GetBiomeIndexStockBilinearSampling(biomeColors, c00, c10, c01, c11, midX, midY, GetIntNonExactThreshold(nonExactThreshold));
        }

        /// <summary>
        /// Convert a 3 Bpp stock biome map encoding rgb colors into a 1 Bpp biome map encoding biome indices.
        /// </summary>
        public bool CopyFromMap(CBAttributeMapSO fromMap)
        {
            if (fromMap._bpp != 3)
                return false;

            Attributes = fromMap.Attributes;
            _name = fromMap._name;
            _width = fromMap._width;
            _height = fromMap._height;
            _bpp = 1;
            _rowWidth = _width;
            _isCompiled = true;

            int biomeCount = Attributes.Length;
            RGBA32[] biomeColors = new RGBA32[biomeCount];

            for (int i = biomeCount; i-- > 0;)
                biomeColors[i] = Attributes[i].mapColor;

            int badPixelsCount = 0;
            int size = _height * _width;
            int fromDataRowWidth = fromMap._rowWidth;
            byte[] fromData = fromMap._data;
            _data = new byte[size];

            Parallel.For(0, _width, x =>
            {
                for (int y = _height; y-- > 0;)
                {
                    int fromDataIndex = x * 3 + y * fromDataRowWidth;
                    RGBA32 pixelColor = new RGBA32(fromData[fromDataIndex], fromData[fromDataIndex + 1], fromData[fromDataIndex + 2]);

                    int biomeIndex = -1;
                    for (int i = biomeCount; i-- > 0;)
                    {
                        if (biomeColors[i] == pixelColor)
                        {
                            biomeIndex = i;
                            break;
                        }
                    }

                    if (biomeIndex == -1)
                    {
                        Interlocked.Increment(ref badPixelsCount);
                        biomeIndex = GetBiomeIndexFromStockBiomeMap((double)x / _width, (double)y / _height, biomeColors, fromData, _width, _height, fromMap._rowWidth, fromMap.nonExactThreshold);
                    }

                    _data[x + y * _width] = (byte)biomeIndex;
                }
            });

            if (badPixelsCount > 0)
            {
                Debug.LogWarning($"[KSPCF/FastBiomeMap] Converting {fromMap._name} : {badPixelsCount} ({(badPixelsCount / (float)size):P3}) pixels not matching a biome color, check the biome texture and biome definitions.");
            }

            return true;
        }

        private static int GetBiomeIndexFromStockBiomeMap(double x, double y, RGBA32[] biomeColors, byte[] rgbData, int width, int height, int rowWidth, float nonExactThreshold)
        {
            GetBilinearCoordinates(x, y, width, height, out int minX, out int maxX, out int minY, out int maxY, out double midX, out double midY);

            // Get 4 samples pixels for bilinear interpolation
            RGBA32 c00 = GetRGB3BppAtTextureCoords(minX, minY, rgbData, rowWidth);
            RGBA32 c10 = GetRGB3BppAtTextureCoords(maxX, minY, rgbData, rowWidth);
            RGBA32 c01 = GetRGB3BppAtTextureCoords(minX, maxY, rgbData, rowWidth);
            RGBA32 c11 = GetRGB3BppAtTextureCoords(maxX, maxY, rgbData, rowWidth);

            return GetBiomeIndexStockBilinearSampling(biomeColors, c00, c10, c01, c11, midX, midY, GetIntNonExactThreshold(nonExactThreshold));
        }

        public override void ConstructBilinearCoords(double x, double y)
        {
            // X wraps around [0, 1[ as it is longitude.
            x = Math.Abs(x - Math.Floor(x));
            centerXD = x * _width;
            minX = (int)centerXD;
            maxX = minX + 1;
            midX = (float)(centerXD - minX);
            if (maxX == _width)
                maxX = 0;

            // Y is clamped to [0, 1[ as it latitude and the poles don't wrap to each other.
            if (y >= 1.0)
                y = lessThanOneDouble;
            else if (y < 0.0)
                y = 0.0;
            centerYD = y * _height;
            minY = (int)centerYD;
            maxY = minY + 1;
            midY = (float)(centerYD - minY);
            if (maxY == _height)
                maxY = _height - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetBilinearCoordinates(double x, double y, int width, int height, out int minX, out int maxX, out int minY, out int maxY, out double midX, out double midY)
        {
            // X wraps around [0, 1[ as it is longitude.
            x = Math.Abs(x - Math.Floor(x));
            double centerXD = x * width;
            minX = (int)centerXD;
            maxX = minX + 1;
            midX = centerXD - minX;
            if (maxX == width)
                maxX = 0;

            // Y is clamped to [0, 1[ as it latitude and the poles don't wrap to each other.
            if (y >= 1.0)
                y = lessThanOneDouble;
            else if (y < 0.0)
                y = 0.0;
            double centerYD = y * height;
            minY = (int)centerYD;
            maxY = minY + 1;
            midY = centerYD - minY;
            if (maxY == height)
                maxY = height - 1;
        }

        /// <summary>
        /// Reimplementation of the stock biome sampler (CBAttributeMapSO.GetAtt()), including all the stock sampling stuff handling incorrect pixel colors in the base texture.
        /// </summary>
        private static unsafe int GetBiomeIndexStockBilinearSampling(RGBA32[] biomeColors, RGBA32 c00, RGBA32 c10, RGBA32 c01, RGBA32 c11, double midX, double midY, int nonExactThreshold)
        {
            int biomeCount = biomeColors.Length;
            bool flag = true; // I still don't understand the logic behind this...
            bool* notNear = stackalloc bool[biomeCount];

            // note : iterating in reverse order from stock, but shouldn't matter here
            for (int i = biomeCount; i-- > 0;)
            {
                RGBA32 biomeColor = biomeColors[i];
                if (biomeColor != c00 && biomeColor != c10 && biomeColor != c01 && biomeColor != c11)
                {
                    notNear[i] = true;
                }
                else
                {
                    notNear[i] = false;
                    flag = false;
                }
            }

            RGBA32 bilinearSample = BilinearSample(c00, c10, c01, c11, midX, midY);

            int biomeIndex = 0;
            int bestMag = int.MaxValue;

            if (flag)
            {
                for (int i = biomeCount; i-- > 0;)
                {
                    int mag = RGBEuclideanDiff(bilinearSample, biomeColors[i]);
                    if (mag < bestMag && mag < nonExactThreshold)
                    {
                        biomeIndex = i;
                        bestMag = mag;
                    }
                }

                return biomeIndex;
            }

            RGBA32 bestSample = c00;
            bestMag = RGBEuclideanDiff(c00, bilinearSample);
            int sampleMag = RGBEuclideanDiff(c10, bilinearSample);
            if (sampleMag < bestMag)
            {
                bestMag = sampleMag;
                bestSample = c10;
            }

            sampleMag = RGBEuclideanDiff(c01, bilinearSample);
            if (sampleMag < bestMag)
            {
                bestMag = sampleMag;
                bestSample = c01;
            }

            sampleMag = RGBEuclideanDiff(c11, bilinearSample);
            if (sampleMag < bestMag)
            {
                bestSample = c11;
            }

            bestMag = int.MaxValue;
            for (int i = biomeCount; i-- > 0;)
            {
                if (notNear[i])
                    continue;

                int mag = RGBEuclideanDiff(bestSample, biomeColors[i]);
                if (mag < bestMag && mag < nonExactThreshold)
                {
                    biomeIndex = i;
                    bestMag = mag;
                }
            }

            return biomeIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetIntNonExactThreshold(float nonExactThreshold)
        {
            return nonExactThreshold < 0f ? int.MaxValue : (int)(nonExactThreshold * (255 * 255));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RGBA32 GetRGB3BppAtTextureCoords(int x, int y, byte[] rgbData, int rowWidth)
        {
            int index = x * 3 + y * rowWidth;
            return new RGBA32(rgbData[index], rgbData[index + 1], rgbData[index + 2]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RGBA32 GetRGBA32AtTextureCoords(int x, int y, Color32[] colorData, int width)
        {
            Color32 color32 = colorData[x + y * width];
            color32.a = 255;
            return color32;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RGBEuclideanDiff(RGBA32 c1, RGBA32 c2)
        {
            int r = c1.r - c2.r;
            int g = c1.g - c2.g;
            int b = c1.b - c2.b;
            return r * r + g * g + b * b;
        }

        private static RGBA32 BilinearSample(RGBA32 c00, RGBA32 c10, RGBA32 c01, RGBA32 c11, double midX, double midY)
        {
            double r0 = c00.r + (c10.r - c00.r) * midX;
            double r1 = c01.r + (c11.r - c01.r) * midX;
            double r = r0 + (r1 - r0) * midY;

            double g0 = c00.g + (c10.g - c00.g) * midX;
            double g1 = c01.g + (c11.g - c01.g) * midX;
            double g = g0 + (g1 - g0) * midY;

            double b0 = c00.b + (c10.b - c00.b) * midX;
            double b1 = c01.b + (c11.b - c01.b) * midX;
            double b = b0 + (b1 - b0) * midY;

            return new RGBA32((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
        }

        public override Texture2D CompileToTexture() => CompileRGB();

        public override Texture2D CompileRGB()
        {
            Texture2D texture2D = new Texture2D(_width, _height, TextureFormat.RGB24, mipChain: false);
            NativeArray<byte> textureData = texture2D.GetRawTextureData<byte>();
            for (int i = _data.Length; i-- > 0;)
            {
                Color pixelColor = Attributes[_data[i]].mapColor;
                int texIndex = i * 3;
                textureData[texIndex] = (byte)Math.Round(pixelColor.r * 255f);
                textureData[texIndex + 1] = (byte)Math.Round(pixelColor.g * 255f);
                textureData[texIndex + 2] = (byte)Math.Round(pixelColor.b * 255f);
            }

            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture2D;
        }

        public override Texture2D CompileRGBA()
        {
            Texture2D texture2D = new Texture2D(_width, _height, TextureFormat.RGBA32, mipChain: false);
            NativeArray<Color32> textureData = texture2D.GetRawTextureData<Color32>();
            for (int i = _data.Length; i-- > 0;)
            {
                Color pixelColor = Attributes[_data[i]].mapColor;
                textureData[i] = new Color32(
                    (byte)Math.Round(pixelColor.r * 255f),
                    (byte)Math.Round(pixelColor.g * 255f),
                    (byte)Math.Round(pixelColor.b * 255f),
                    255);
            }

            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture2D;
        }

        public override Texture2D CompileGreyscale() => throw new NotImplementedException("Can't create Greyscale texture from a biome map");

        public override Texture2D CompileHeightAlpha() => throw new NotImplementedException("Can't create HeightAlpha texture from a biome map");

        /// <summary>
        /// RGB24 color with identical layout to Color32 with FieldOffset tricks to provide fast equality comparison. 
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct RGBA32
        {
            [FieldOffset(0)] public byte r;
            [FieldOffset(1)] public byte g;
            [FieldOffset(2)] public byte b;
            [FieldOffset(3)] private byte a;

            [FieldOffset(0)] private int rgba;

            public RGBA32(byte r, byte g, byte b)
            {
                rgba = 0;
                this.r = r;
                this.g = g;
                this.b = b;
                a = 255;
            }

            public void ClearAlpha() => a = 255;

            public static implicit operator RGBA32(Color c)
            {
                return new RGBA32((byte)Math.Round(c.r * 255f), (byte)Math.Round(c.g * 255f), (byte)Math.Round(c.b * 255f));
            }

            public static unsafe implicit operator RGBA32(Color32 c)
            {
                return *(RGBA32*)&c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(RGBA32 lhs, RGBA32 rhs) => lhs.rgba == rhs.rgba;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(RGBA32 lhs, RGBA32 rhs) => lhs.rgba != rhs.rgba;

            public bool Equals(RGBA32 other) => rgba == other.rgba;
            public override bool Equals(object obj) => obj is RGBA32 other && rgba == other.rgba;
            public override int GetHashCode() => rgba;
        }
    }

    /// <summary>
    /// Convert all biome maps on the fly after system spawn, before main menu is loaded.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.PSystemSpawn, false)]
    internal class BiomeMapOptimizer : MonoBehaviour
    {
#if BIOME_FLIGHTDEBUG
        public static CBAttributeMapSO[] stockBiomeMaps;
#endif

        void OnDestroy()
        {
            Stopwatch watch = new Stopwatch();
            List<string> optimizedBodies = new List<string>(FlightGlobals.Bodies.Count);
            long oldMapsBytes = 0;
            long newMapsBytes = 0;

#if BIOME_FLIGHTDEBUG
            stockBiomeMaps = new CBAttributeMapSO[FlightGlobals.Bodies.Count];
#endif
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body.BiomeMap == null)
                    continue;

                CBAttributeMapSO stockMap = body.BiomeMap;
                if (stockMap.GetType() != typeof(CBAttributeMapSO))
                    continue;

                watch.Start();
                KSPCFFastBiomeMap fastBiomeMap = ScriptableObject.CreateInstance<KSPCFFastBiomeMap>();
                if (!fastBiomeMap.CopyFromMap(stockMap))
                {
                    Debug.LogWarning($"[KSPCF/FastBiomeMap] Unable to optimize biome map for {body.name}, {stockMap._bpp} Bpp maps aren't supported");
                    Destroy(fastBiomeMap);
                    continue;
                }

                body.BiomeMap = fastBiomeMap;

                optimizedBodies.Add(body.name);
                oldMapsBytes += stockMap.Size;
                newMapsBytes += fastBiomeMap.Size;
                watch.Stop();

#if BIOME_CONVERSION_VALIDATE
                Validate(body, stockMap, fastBiomeMap);
#endif
#if BIOME_FLIGHTDEBUG
                stockBiomeMaps[body.flightGlobalsIndex] = stockMap;
#endif
                Destroy(stockMap);
            }

            if (optimizedBodies.Count > 0)
            {
                Debug.Log($"[KSPCF/FastBiomeMap] Optimized {optimizedBodies.Count} biome maps in {watch.Elapsed.TotalSeconds:F3}s, old size: {StaticHelpers.HumanReadableBytes(oldMapsBytes)}, new size: {StaticHelpers.HumanReadableBytes(newMapsBytes)}\nOptimized bodies : {string.Join(", ", optimizedBodies)}");
            }
        }

#if BIOME_CONVERSION_VALIDATE
        void Validate(CelestialBody body, CBAttributeMapSO stockMap, KSPCFFastBiomeMap fastBiomeMap)
        {
            Random.InitState(0);
            int sampleCount = 1000000;
            Vector2d[] coords = new Vector2d[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double lat = ResourceUtilities.Deg2Rad(ResourceUtilities.clampLat(Random.Range(-90f, 90f)));
                double lon = ResourceUtilities.Deg2Rad(ResourceUtilities.clampLon(Random.Range(-180f, 180f)));
                coords[i] = new Vector2d(lat, lon);
            }

            CBAttributeMapSO.MapAttribute[] stockBiomes = new CBAttributeMapSO.MapAttribute[sampleCount];
            Stopwatch stockWatch = new Stopwatch();

            stockWatch.Start();
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2d latLon = coords[i];
                stockBiomes[i] = stockMap.GetAtt(latLon.x, latLon.y);
            }
            stockWatch.Stop();

            CBAttributeMapSO.MapAttribute[] cfBiomes = new CBAttributeMapSO.MapAttribute[sampleCount];

            Stopwatch cfWatch = new Stopwatch();
            cfWatch.Start();
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2d latLon = coords[i];
                cfBiomes[i] = fastBiomeMap.GetAtt(latLon.x, latLon.y);
            }
            cfWatch.Stop();

            double ratio = stockWatch.Elapsed.TotalMilliseconds / cfWatch.Elapsed.TotalMilliseconds;
            int mismatchCount = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                if (stockBiomes[i] != cfBiomes[i])
                {
                    mismatchCount++;
#if DEBUG_BIOME_MISMATCH
                    Vector2d latLon = coords[i];
                    Debug.Log($"[KSPCF/FastBiomeMap] Biome mismatch on {body.name,10}, lat/lon: {latLon.x * Mathf.Rad2Deg,7:F2} / {latLon.y * Mathf.Rad2Deg,7:F2}, {stockBiomes[i].name,15} / {cfBiomes[i].name,15} (stock/fast)");
#endif
                }
            }
            Debug.Log($"[KSPCF/FastBiomeMap] Sampling {sampleCount} biomes on {body.name,10} : {cfWatch.Elapsed.TotalMilliseconds,8:F2}ms vs {stockWatch.Elapsed.TotalMilliseconds,8:F2}ms, ratio: {ratio,4:F1}, samples mismatchs :{mismatchCount,6} ({(mismatchCount / (float)sampleCount),6:P3})");
        }
#endif

    }

#if BIOME_FLIGHTDEBUG

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CheckBiome : MonoBehaviour
    {
        private static Vector3 defaultScale = new Vector3(75f, 75f, 75f);
        const int count = 50;
        GameObject[] spheres = new GameObject[count * count];
        Material[] sphereMaterials = new Material[count * count];

        private void Start()
        {
            for (int i = 0; i < spheres.Length; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.localScale = defaultScale;
                Destroy(sphere.GetComponent<Collider>());
                spheres[i] = sphere;
                sphereMaterials[i] = sphere.GetComponent<Renderer>().material;
            }
        }

        private void Update()
        {
            if (FlightGlobals.ActiveVessel == null || FlightGlobals.currentMainBody == null)
                return;

            Vector3 vesselPos = FlightGlobals.ActiveVessel.transform.position;
            Vector3 down = (FlightGlobals.currentMainBody.position - vesselPos).normalized;
            Vector3 up = -down;

            float du = Vector3.Dot(up, Vector3.up);
            float df = Vector3.Dot(up, Vector3.forward);
            Vector3 v1 = Mathf.Abs(du) < Mathf.Abs(df) ? Vector3.up : Vector3.forward;
            Vector3 forward = Vector3.Cross(v1, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);

            Vector3 upOrigin = up * 500f;

            LayerMask mask = LayerMask.GetMask("Local Scenery");

            CelestialBody body = FlightGlobals.currentMainBody;
            CBAttributeMapSO stockMap = BiomeMapOptimizer.stockBiomeMaps[body.flightGlobalsIndex];
            CBAttributeMapSO fastMap = FlightGlobals.currentMainBody.BiomeMap;

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    Vector3 origin = vesselPos + upOrigin + (forward * (i - count / 2) * 100f) + (right * (j - count / 2) * 100f);
                    if (Physics.Raycast(origin, down, out RaycastHit hitInfo, 5000f, mask))
                    {
                        body.GetLatLonAlt(origin, out double lat, out double lon, out _);
                        lat = ResourceUtilities.Deg2Rad(ResourceUtilities.clampLat(lat));
                        lon = ResourceUtilities.Deg2Rad(ResourceUtilities.clampLon(lon));

                        CBAttributeMapSO.MapAttribute stockBiome = stockMap.GetAtt(lat, lon);
                        CBAttributeMapSO.MapAttribute fastBiome = fastMap.GetAtt(lat, lon);

                        int index = i * count + j;

                        sphereMaterials[index].color = fastBiome.mapColor;

                        Transform transform = spheres[index].transform;
                        transform.position = hitInfo.point;
                        if (stockBiome == fastBiome)
                            transform.localScale = defaultScale;
                        else
                            transform.localScale = defaultScale * 2f;
                    }
                }
            }
        }
    }

#endif
}
