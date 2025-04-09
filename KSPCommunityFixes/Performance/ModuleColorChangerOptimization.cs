namespace KSPCommunityFixes.Performance
{
    internal class ModuleColorChangerOptimization : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(ModuleColorChanger), nameof(ModuleColorChanger.Start));
            AddPatch(PatchType.Override, typeof(ModuleColorChanger), nameof(ModuleColorChanger.FixedUpdate));
            AddPatch(PatchType.Override, typeof(ModuleColorChanger), nameof(ModuleColorChanger.Update));
        }

        static void ModuleColorChanger_Start_Postfix(ModuleColorChanger __instance)
        {
            __instance.SetState(__instance.currentRateState); // ensure initial state is correct
        }

        static void ModuleColorChanger_FixedUpdate_Override(ModuleColorChanger mcc)
        {
            return; // state checking moved to Update()
        }

        static void ModuleColorChanger_Update_Override(ModuleColorChanger mcc)
        {
            if (!mcc.useRate || !mcc.isValid)
                return;

            if (HighLogic.LoadedSceneIsEditor && mcc.part.frozen)
                return;

            if (mcc.animState)
            {
                if (mcc.currentRateState < 1f)
                {
                    mcc.currentRateState += mcc.animRate * TimeWarp.deltaTime;
                    if (mcc.currentRateState > 1f)
                        mcc.currentRateState = 1f;

                    mcc.SetState(mcc.currentRateState);
                }
            }
            else
            {
                if (mcc.currentRateState > 0f)
                {
                    mcc.currentRateState -= mcc.animRate * TimeWarp.deltaTime;
                    if (mcc.currentRateState < 0f)
                        mcc.currentRateState = 0f;

                    mcc.SetState(mcc.currentRateState);
                }
            }
        }
    }
}
