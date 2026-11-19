using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // KSPLog subscribes to Application.logMessageReceivedThreaded, so Unity calls it on whatever
    // thread called Debug.Log. Every KSPLog callback then runs on that thread : the KSP.log
    // StreamWriter and the in-memory log ring buffer are written without any locking, error
    // messages are posted through ScreenMessages, and the debug console instantiates a TextMeshPro
    // object for each line. Creating Unity objects off the main thread can crash the game (in
    // Mesh::CreateMesh for the TextMeshPro case), and concurrent writes can corrupt the log.
    // Mods that log from ThreadPool callbacks or other worker threads trigger this.
    // The prefix here queues messages logged from other threads and replays them on the main
    // thread, at the next main thread log call or at the next Update, whichever comes first.
    internal class ThreadSafeLogging : BasePatch
    {
        private struct LogEntry
        {
            public string logString;
            public string stackTrace;
            public LogType type;
        }

        private static Thread mainThread;
        private static readonly ConcurrentQueue<LogEntry> messageQueue = new ConcurrentQueue<LogEntry>();

        protected override void ApplyPatches()
        {
            mainThread = Thread.CurrentThread;
            AddPatch(PatchType.Prefix, typeof(KSPLog), nameof(KSPLog.LogCallback));
        }

        protected override void OnPatchApplied()
        {
            GameObject go = new GameObject("KSPCF_ThreadSafeLogging");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<QueueFlusher>();
        }

        private static bool KSPLog_LogCallback_Prefix(string logString, string stackTrace, LogType type)
        {
            if (Thread.CurrentThread != mainThread)
            {
                messageQueue.Enqueue(new LogEntry { logString = logString, stackTrace = stackTrace, type = type });
                return false;
            }

            // Log any queued messages before this main thread message
            FlushMessageQueue();

            return true;
        }

        private static void FlushMessageQueue()
        {
            while (messageQueue.TryDequeue(out LogEntry entry))
            {
                try
                {
                    KSPLog.logCallback?.Invoke(entry.logString, entry.stackTrace, entry.type);
                }
                catch (Exception)
                {
                    // Logging here would likely produce infinite exception handling recursion
                }
            }
        }

        private class QueueFlusher : MonoBehaviour
        {
            private void Update() => FlushMessageQueue();
        }
    }
}
