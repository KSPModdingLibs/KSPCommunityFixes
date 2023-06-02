using Smooth.Algebraics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;
using VehiclePhysics;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal class BiomeMapOptimizer : MonoBehaviour
    {
        const float Byte2Float = 0.003921569f;

        public static byte[][] biomeMaps;

        void Start()
        {
            biomeMaps = new byte[FlightGlobals.Bodies.Count][];

            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body.BiomeMap != null)
                {
                    byte[] newMap = Optimize(body.BiomeMap);
                    biomeMaps[body.flightGlobalsIndex] = newMap;
                }
            }
        }

        void Update()
        {

        }

        private byte[] Optimize(CBAttributeMapSO biomeMap)
        {
            int biomeCount = biomeMap.Attributes.Length;
            Color[] biomeColors = new Color[biomeCount];
            for (int i = 0; i < biomeCount; i++)
            {
                biomeColors[i] = biomeMap.Attributes[i].mapColor;
            }

            byte[] oldMap = biomeMap._data;
            int bpp = biomeMap._bpp;

            byte[] newMap = new byte[biomeMap._height * biomeMap._width];

            for (int i = 0; i < newMap.Length; i++)
            {
                int colorIndex = i * bpp;
                float r = oldMap[colorIndex] * Byte2Float;
                float g = oldMap[colorIndex + 1] * Byte2Float;
                float b = oldMap[colorIndex + 2] * Byte2Float;

                int biomeIndex = TryGetExactBiomeColorIndex(r, g, b, biomeColors);
                if (biomeIndex > -1)
                {
                    newMap[i] = (byte)biomeIndex;
                    continue;
                }

                biomeIndex = TryGetNearBiomeColorIndex(r, g, b, biomeMap.neighborColorThresh, biomeColors);
                if (biomeIndex > -1)
                {
                    newMap[i] = (byte)biomeIndex;
                    continue;
                }

                biomeIndex = GetBestNeighborBiomeColorIndex(r, g, b, i, biomeMap, biomeColors);
                newMap[i] = (byte)biomeIndex;
            }

            return newMap;
        }

        private int TryGetExactBiomeColorIndex(float r, float g, float b, Color[] biomeColors)
        {
            for (int i = 0; i < biomeColors.Length; i++)
            {
                Color biomeColor = biomeColors[i];
                if (biomeColor.r == r && biomeColor.g == g && biomeColor.b == b)
                    return i;
            }

            return -1;
        }

        private static int TryGetNearBiomeColorIndex(float r, float g, float b, float threshold, Color[] biomeColors)
        {
            int bestIndex = -1;
            float bestColorDiff = float.MaxValue;

            for (int i = 0; i < biomeColors.Length; i++)
            {
                Color biomeColor = biomeColors[i];
                float colorDiff = EuclideanColorDiff(r, g, b, biomeColor.r, biomeColor.g, biomeColor.b);
                if (colorDiff < threshold && colorDiff < bestColorDiff)
                {
                    bestColorDiff = colorDiff;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int[] neighborIndices = new int[9];
        private static List<Color> colorBuffer = new List<Color>(9);

        private static int GetBestNeighborBiomeColorIndex(float r, float g, float b, int pixelIndex, CBAttributeMapSO biomeMap, Color[] biomeColors)
        {
            int width = biomeMap._width;
            neighborIndices[0] = pixelIndex - 1 - width; // top-left
            neighborIndices[1] = pixelIndex - width; // top
            neighborIndices[2] = pixelIndex + 1 - width; // top-right
            neighborIndices[3] = pixelIndex + 1; // right
            neighborIndices[4] = pixelIndex + 1 + width; // bottom-right
            neighborIndices[5] = pixelIndex + width; // bottom
            neighborIndices[6] = pixelIndex - 1 + width; // bottom-left
            neighborIndices[7] = pixelIndex - 1; // left
            neighborIndices[8] = pixelIndex; // center

            colorBuffer.Clear();
            int textureSize = biomeMap._width * biomeMap._height;
            int bpp = biomeMap._bpp;
            byte[] data = biomeMap._data;
            for (int i = 0; i < 8; i++)
            {
                int neighborIndex = neighborIndices[i];
                if (neighborIndex < 0)
                    neighborIndex += textureSize;
                else if (neighborIndex >= textureSize)
                    neighborIndex -= textureSize;

                int colorIndex = neighborIndex * bpp;

                Color neighborColor = new Color(data[colorIndex] * Byte2Float, data[colorIndex + 1] * Byte2Float, data[colorIndex + 2] * Byte2Float, 1f);
                if (!colorBuffer.Contains(neighborColor))
                    colorBuffer.Add(neighborColor);
            }

            int bestIndex = 0;
            float bestColorWeight = float.MaxValue;

            for (int i = 0; i < biomeColors.Length; i++)
            {
                float colorWeight = 0f;
                Color biomeColor = biomeColors[i];
                for (int j = 0; j < colorBuffer.Count; j++)
                {
                    colorWeight += EuclideanColorDiff(colorBuffer[j], biomeColor);
                }

                if (colorWeight < bestColorWeight)
                {
                    bestIndex = i;
                    bestColorWeight = colorWeight;
                }
            }

            return bestIndex;
        }

        private static float EuclideanColorDiff(float r1, float g1, float b1, float r2, float g2, float b2)
        {
            float r = r1 - r2;
            float g = g1 - g2;
            float b = b1 - b2;
            return r * r + g * g + b * b;
        }

        private static float EuclideanColorDiff(Color colA, Color colB)
        {
            float r = colA.r - colB.r;
            float g = colA.g - colB.g;
            float b = colA.b - colB.b;
            return r * r + g * g + b * b;
        }
    }


    //[KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal class FastBiomeQuery : MonoBehaviour
    {
        private static RGBA32[][] biomeColorsByBody;

        IEnumerator Start()
        {
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            biomeColorsByBody = new RGBA32[FlightGlobals.Bodies.Count][];

            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                CBAttributeMapSO biomeMap = body.BiomeMap;
                if (biomeMap == null)
                    continue;

                RGBA32[] biomeColors = new RGBA32[biomeMap.Attributes.Length];
                biomeColorsByBody[body.flightGlobalsIndex] = biomeColors;

                for (int i = 0; i < biomeMap.Attributes.Length; i++)
                {
                    biomeColors[i] = biomeMap.Attributes[i].mapColor;
                }
            }

            List<double> ratios = new List<double>();

            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body.BiomeMap == null)
                    continue;

                CBAttributeMapSO biomeMap = body.BiomeMap;
                Debug.Log($"[{body.name}] exactSearch={biomeMap.exactSearch}, neighborColorThresh={biomeMap.neighborColorThresh}, nonExactThreshold={biomeMap.nonExactThreshold}, bpp={biomeMap._bpp}");

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
                    stockBiomes[i] = biomeMap.GetAtt(latLon.x, latLon.y);
                }
                stockWatch.Stop();
                //Debug.Log($"[FastBiomeQuery] Stock sampling : {stockWatch.Elapsed.TotalMilliseconds:F3}ms");

                CBAttributeMapSO.MapAttribute[] cfBiomes = new CBAttributeMapSO.MapAttribute[sampleCount];
                Stopwatch cfWatch = new Stopwatch();

                cfWatch.Start();
                for (int i = 0; i < sampleCount; i++)
                {
                    Vector2d latLon = coords[i];
                    cfBiomes[i] = GetAttInt(biomeMap, body.flightGlobalsIndex, latLon.x, latLon.y);
                    //cfBiomes[i] = GetAtt(biomeMap, latLon.x, latLon.y);
                }
                cfWatch.Stop();

                double ratio = stockWatch.Elapsed.TotalMilliseconds / cfWatch.Elapsed.TotalMilliseconds;
                ratios.Add(ratio);
                Debug.Log($"[FastBiomeQuery] Sampling {sampleCount} biomes on {body.name,10} : {cfWatch.Elapsed.TotalMilliseconds,8:F2}ms vs {stockWatch.Elapsed.TotalMilliseconds,8:F2}ms, ratio:{ratio:F1}");

                //for (int i = 0; i < sampleCount; i++)
                //{
                //    if (stockBiomes[i] != cfBiomes[i])
                //    {
                //        Debug.LogWarning($"[FastBiomeQuery] Result mismatch at coords {coords[i]}, stock={stockBiomes[i].name}, kspcf={cfBiomes[i].name}");
                //    }
                //}

                int mismatchCount = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    if (stockBiomes[i] != cfBiomes[i])
                    {
                        mismatchCount++;
                    }
                }

                Debug.LogWarning($"[FastBiomeQuery] {mismatchCount} mismatchs ({mismatchCount/sampleCount:P3})");
            }

            Debug.Log($"[FastBiomeQuery] Average ratio : {ratios.Average():F2}");
        }





        private const double HalfPI = Math.PI / 2.0;
        private const double DoublePI = Math.PI * 2.0;
        private const double InversePI = 1.0 / Math.PI;
        private const double InverseDoublePI = 1.0 / (2.0 * Math.PI);

        public static CBAttributeMapSO.MapAttribute GetAttInt(CBAttributeMapSO biomeMap, int bodyIndex, double lat, double lon)
        {
            //if (biomeMap.exactSearch || biomeMap.nonExactThreshold != -1f)
            //{
            //    return biomeMap.GetAtt(lat, lon);
            //}

            lon -= HalfPI;
            if (lon < 0.0)
                lon += DoublePI;
            lon %= DoublePI;
            double x = 1.0 - lon * InverseDoublePI;
            double y = lat * InversePI + 0.5;

            biomeMap.ConstructBilinearCoords(x, y);
            RGBA32 c0 = GetColorRGBA32(biomeMap, biomeMap.minX, biomeMap.minY);
            RGBA32 c1 = GetColorRGBA32(biomeMap, biomeMap.maxX, biomeMap.minY);
            RGBA32 c2 = GetColorRGBA32(biomeMap, biomeMap.minX, biomeMap.maxY);
            RGBA32 c3 = GetColorRGBA32(biomeMap, biomeMap.maxX, biomeMap.maxY);

            RGBA32 pixelColor;

            if (c0 == c1 && c0 == c2 && c0 == c3)
            {
                pixelColor = c0;
            }
            else
            {
                Color cf0 = c0;
                Color cf1 = c1;
                Color cf2 = c2;
                Color cf3 = c3;

                Color lerpColor = BilinearColor(cf0, cf1, cf2, cf3, biomeMap.midX, biomeMap.midY);
                pixelColor = c0;
                float maxMag = RGBADiffSqrMag(cf0, lerpColor);
                float mag = RGBADiffSqrMag(cf1, lerpColor);
                if (mag < maxMag)
                {
                    maxMag = mag;
                    pixelColor = c1;
                }

                mag = RGBADiffSqrMag(cf2, lerpColor);
                if (mag < maxMag)
                {
                    maxMag = mag;
                    pixelColor = c2;
                }

                // There is a bug in the stock method, where it doesn't check the fourth color...
                //mag = RGBADiffSqrMag(cf3, lerpColor);
                //if (mag < maxMag)
                //{
                //    pixelColor = c3;
                //}
            }

            RGBA32[] biomeColors = biomeColorsByBody[bodyIndex];
            int length = biomeColors.Length;
            for (int i = 0; i < length; i++)
            {
                if (biomeColors[i] == pixelColor)
                {
                    return biomeMap.Attributes[i];
                }
            }

            // fallback path if the color doesn't match exactly for some reason, this is the default stock code
            CBAttributeMapSO.MapAttribute result = biomeMap.Attributes[0];
            float maxColorMag = float.MaxValue;
            for (int i = 0; i < length; i++)
            {
                float colorMag = EuclideanColorDiff(pixelColor, biomeColors[i]);
                if (colorMag < maxColorMag)
                {
                    result = biomeMap.Attributes[i];
                    maxColorMag = colorMag;
                }
            }

            return result;
        }

        private static Color BilinearColor(Color col0, Color col1, Color col2, Color col3, double midX, double midY)
        {
            double r0 = col0.r + (col1.r - col0.r) * midX;
            double r1 = col2.r + (col3.r - col2.r) * midX;
            double r = r0 + (r1 - r0) * midY;

            double g0 = col0.g + (col1.g - col0.g) * midX;
            double g1 = col2.g + (col3.g - col2.g) * midX;
            double g = g0 + (g1 - g0) * midY;

            double b0 = col0.b + (col1.b - col0.b) * midX;
            double b1 = col2.b + (col3.b - col2.b) * midX;
            double b = b0 + (b1 - b0) * midY;

            //double a0 = col0.a + (col1.a - col0.a) * midX;
            //double a1 = col2.a + (col3.a - col2.a) * midX;
            //double a = a0 + (a1 - a0) * midY;

            return new Color((float)r, (float)g, (float)b, 1f);
        }

        private static float EuclideanColorDiff(Color colA, Color colB)
        {
            float r = colA.r - colB.r;
            float g = colA.g - colB.g;
            float b = colA.b - colB.b;
            return r * r + g * g + b * b;
        }

        private static float RGBADiffSqrMag(Color colA, Color colB)
        {
            float r = colA.r - colB.r;
            float g = colA.g - colB.g;
            float b = colA.b - colB.b;
            float a = colA.a - colB.a;
            return r * r + g * g + b * b + a * a;
        }

        private static RGBA32 BilinearRGBA32(RGBA32 col0, RGBA32 col1, RGBA32 col2, RGBA32 col3, double midX, double midY)
        {
            double r0 = col0.r + (col1.r - col0.r) * midX;
            double r1 = col2.r + (col3.r - col2.r) * midX;
            double r = r0 + (r1 - r0) * midY;

            double g0 = col0.g + (col1.g - col0.g) * midX;
            double g1 = col2.g + (col3.g - col2.g) * midX;
            double g = g0 + (g1 - g0) * midY;

            double b0 = col0.b + (col1.b - col0.b) * midX;
            double b1 = col2.b + (col3.b - col2.b) * midX;
            double b = b0 + (b1 - b0) * midY;

            //double a0 = col0.a + (col1.a - col0.a) * midX;
            //double a1 = col2.a + (col3.a - col2.a) * midX;
            //double a = a0 + (a1 - a0) * midY;

            return new RGBA32((byte)r, (byte)g, (byte)b, 255);
        }

        private static int RGBA32DiffSqrMag(RGBA32 colA, RGBA32 colB)
        {
            int r = colA.r - colB.r;
            int g = colA.g - colB.g;
            int b = colA.b - colB.b;
            return r * r + g * g + b * b;
        }

        private static RGBA32 GetColorRGBA32(CBAttributeMapSO biomeMap, int x, int y)
        {
            int index = biomeMap.PixelIndex(x, y);
            byte[] data = biomeMap._data;

            switch (biomeMap._bpp)
            {
                case 3:
                    return new RGBA32(data[index], data[index + 1], data[index + 2], 255);
                case 4:
                    return new RGBA32(data[index], data[index + 1], data[index + 2], data[index + 3]);
                case 2:
                {
                    byte rgb = data[index];
                    return new RGBA32(rgb, rgb, rgb, data[index + 1]);
                }
                default:
                {
                    byte rgb = data[index];
                    return new RGBA32(rgb, rgb, rgb, 255);
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RGBA32
        {
            private const float Byte2Float = 0.003921569f;

            [FieldOffset(0)]
            public int rgba;

            [FieldOffset(0)]
            public byte r;

            [FieldOffset(1)]
            public byte g;

            [FieldOffset(2)]
            public byte b;

            [FieldOffset(3)]
            public byte a;

            public RGBA32(byte r, byte g, byte b, byte a)
            {
                rgba = 0;
                this.r = r;
                this.g = g;
                this.b = b;
                this.a = a;
            }

            public static implicit operator RGBA32(Color c)
            {
                return new RGBA32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));
            }

            public static implicit operator Color(RGBA32 c)
            {
                return new Color(c.r * Byte2Float, c.g * Byte2Float, c.b * Byte2Float, c.a * Byte2Float);
            }

            public bool Equals(RGBA32 other) => rgba == other.rgba;
            public override bool Equals(object obj) => obj is RGBA32 other && Equals(other);
            public static bool operator ==(RGBA32 lhs, RGBA32 rhs) => lhs.rgba == rhs.rgba;
            public static bool operator !=(RGBA32 lhs, RGBA32 rhs) => lhs.rgba != rhs.rgba;
            public override int GetHashCode() => rgba;
        }
    }
}
