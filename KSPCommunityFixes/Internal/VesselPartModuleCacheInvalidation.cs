namespace KSPCommunityFixes
{
    /// <summary>
    /// Always on infrastructure patch keeping <see cref="VesselPartModuleCache"/> in sync with
    /// modules being added to or removed from a part at runtime.<para/>
    /// <see cref="Part.ClearModuleReferenceCache"/> is what stock calls from every
    /// <see cref="PartModuleList"/> mutator, so it is the single hook needed for this. It is also
    /// called from <c>Part.OnDestroy()</c>, which is harmless : invalidation is lazy, and a part
    /// being destroyed implies a vessel modification anyway.
    /// </summary>
    internal class VesselPartModuleCacheInvalidation : BasePatch
    {
        protected override bool IgnoreConfig => true;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(Part), nameof(Part.ClearModuleReferenceCache));
        }

        private static void Part_ClearModuleReferenceCache_Postfix(Part __instance)
        {
            VesselPartModuleCache.SetDirty(__instance.vessel);
        }
    }
}
