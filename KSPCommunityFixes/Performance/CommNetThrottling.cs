using System.Diagnostics;

namespace KSPCommunityFixes.Performance
{
    class CommNetThrottling : BasePatch
    {
        private static double maxGameTimeInterval = 2.5;
        private static long minRealTimeInterval = 20;
        private static Stopwatch realTimeUpdateTimer = new Stopwatch();

        protected override void ApplyPatches()
        {
            ConfigNode settingsNode = KSPCommunityFixes.SettingsNode.GetNode("COMMNET_THROTTLING_SETTINGS");

            if (settingsNode != null)
            {
                settingsNode.TryGetValue("maxGameTimeInterval", ref maxGameTimeInterval);
                settingsNode.TryGetValue("minRealTimeInterval", ref minRealTimeInterval);
            }

            AddPatch(PatchType.Override, typeof(CommNet.CommNetNetwork), nameof(CommNet.CommNetNetwork.Update));
        }

        static void CommNetNetwork_Update_Override(CommNet.CommNetNetwork commNetNetwork)
        {
            double currentGameTime = Planetarium.GetUniversalTime();
            double currentGameTimeInterval = currentGameTime - commNetNetwork.prevUpdate;
            if (!commNetNetwork.queueRebuild && !commNetNetwork.commNet.IsDirty 
                && currentGameTimeInterval >= 0.0
                && (currentGameTimeInterval < maxGameTimeInterval || realTimeUpdateTimer.ElapsedMilliseconds < minRealTimeInterval))
            {
                commNetNetwork.graphDirty = true;
                return;
            }

            // UnityEngine.Debug.Log($"CommNet update interval : {realTimeUpdateTimer.Elapsed.TotalMilliseconds:F0}ms");

            commNetNetwork.commNet.Rebuild();
            realTimeUpdateTimer.Restart();
            commNetNetwork.prevUpdate = currentGameTime;
            commNetNetwork.graphDirty = false;
            commNetNetwork.queueRebuild = false;
        }
    }
}
