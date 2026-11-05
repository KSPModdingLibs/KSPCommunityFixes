using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPCommunityFixes.Performance
{
    internal static class DragCubeGpuReduction
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Configure(IntPtr texture, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr Callback();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Status();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Release();

        private static Configure configure;
        private static Status status;
        private static Release release;

        internal static bool verified;
        private static bool disabled;
        private static bool initialized;
        private static ComputeBuffer buffer;
        private static uint[] partial;
        private static CommandBuffer commands;

        private static T Export<T>(IntPtr library, string name) where T : Delegate
        {
            IntPtr address = GetProcAddress(library, name);
            if (address == IntPtr.Zero)
                throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        internal struct Statistics
        {
            internal int hits;
            internal int totalDrag;
            internal int maxDepth;

            internal void Calculate(float cameraSize, float farClipPlane, out float area, out float drag, out float depth)
            {
                area = drag = depth = 0f;
                if (hits == 0)
                    return;

                float pixelArea = Mathf.Pow(2f * cameraSize / DragCubeGeneration.textureSize, 2f);
                area = pixelArea * hits;
                drag = totalDrag / (hits * 255f);
                depth = farClipPlane * (maxDepth / 255f);
            }
        }

        internal static bool TryReduce(RenderTexture input, out Statistics statistics)
        {
            statistics = default;
            if (disabled || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11
                || !SystemInfo.supportsComputeShaders || input == null
                || input.width != DragCubeGeneration.textureSize || input.height != DragCubeGeneration.textureSize)
                return false;

            try
            {
                if (!initialized)
                {
                    string path = Path.Combine(KSPCommunityFixes.ModPath, "Plugins/x86_64/KSPCFDragReduction.dll");
                    IntPtr library = LoadLibraryW(path);
                    if (library == IntPtr.Zero)
                        throw new InvalidOperationException($"Native loader error {Marshal.GetLastWin32Error()}");
                    configure = Export<Configure>(library, "ConfigureDragReduction");
                    status = Export<Status>(library, "GetDragReductionStatus");
                    release = Export<Release>(library, "ReleaseDragReduction");
                    IntPtr callback = Export<Callback>(library, "GetDragReductionEvent")();
                    if (callback == IntPtr.Zero)
                        throw new InvalidOperationException("Drag reduction callback unavailable");
                    initialized = true;
                    Application.quitting += Dispose;
                    commands = new CommandBuffer { name = "KSPCFDragReduction" };
                    commands.IssuePluginEvent(callback, 0);
                    int groups = ((input.width + 15) / 16) * ((input.height + 15) / 16);
                    buffer = new ComputeBuffer(groups, 16);
                    partial = new uint[groups * 4];
                }

                if (configure(input.GetNativeTexturePtr(), buffer.GetNativeBufferPtr()) < 0)
                    throw new InvalidOperationException("Unsupported drag reduction resources");
                Graphics.ExecuteCommandBuffer(commands);
                buffer.GetData(partial);
                if (status() < 0)
                    throw new InvalidOperationException("Drag reduction callback failed");

                for (int i = 0; i < partial.Length; i += 4)
                {
                    statistics.hits = checked(statistics.hits + (int)partial[i]);
                    statistics.totalDrag = checked(statistics.totalDrag + (int)partial[i + 1]);
                    statistics.maxDepth = Math.Max(statistics.maxDepth, checked((int)partial[i + 2]));
                }

                if (statistics.hits > input.width * input.height || statistics.maxDepth > 255
                    || statistics.totalDrag > statistics.hits * 255L)
                    throw new InvalidOperationException("Invalid drag reduction statistics");
                return true;
            }
            catch (Exception e)
            {
                disabled = true;
                Debug.LogWarning($"[KSPCF/DragCubeGeneration] GPU reduction disabled: {e.Message}");
                Dispose();
                return false;
            }
        }

        internal static unsafe void Verify(Statistics statistics, float cameraSize, float farClipPlane, float area, float drag, float depth)
        {
            statistics.Calculate(cameraSize, farClipPlane, out float gpuArea, out float gpuDrag, out float gpuDepth);
            verified = *(int*)&gpuArea == *(int*)&area && *(int*)&gpuDrag == *(int*)&drag && *(int*)&gpuDepth == *(int*)&depth;
            if (!verified)
            {
                disabled = true;
                Debug.LogWarning("[KSPCF/DragCubeGeneration] GPU reduction differs from CPU results, retaining CPU path");
                Dispose();
            }
        }

        private static void Dispose()
        {
            disabled = true;
            if (initialized)
            {
                release();
                initialized = false;
                Application.quitting -= Dispose;
            }
            buffer?.Release();
            buffer = null;
            commands?.Dispose();
            commands = null;
        }
    }
}
