namespace KSPCommunityFixes.Performance
{
    class CommNetThrottling : BasePatch
    {
        private static double updateInterval = 2.5;
        private static long lastUpdateFixedFrame = 0L;

        protected override void ApplyPatches()
        {
            KSPCommunityFixes.SettingsNode.TryGetValue("CommNetThrottlingUpdateInterval", ref updateInterval);
            AddPatch(PatchType.Override, typeof(CommNet.CommNetNetwork), nameof(CommNet.CommNetNetwork.Update));
        }

        static void CommNetNetwork_Update_Override(CommNet.CommNetNetwork commNetNetwork)
        {
            double currentTime = Planetarium.GetUniversalTime();
            double interval = currentTime - commNetNetwork.prevUpdate;
            if (!commNetNetwork.queueRebuild && !commNetNetwork.commNet.IsDirty 
                && interval >= 0.0
                && (interval < updateInterval || KSPCommunityFixes.FixedUpdateCount == lastUpdateFixedFrame))
            {
                commNetNetwork.graphDirty = true;
                return;
            }

            lastUpdateFixedFrame = KSPCommunityFixes.FixedUpdateCount;
            commNetNetwork.commNet.Rebuild();
            commNetNetwork.prevUpdate = currentTime;
            commNetNetwork.graphDirty = false;
            commNetNetwork.queueRebuild = false;
        }
    }
}
