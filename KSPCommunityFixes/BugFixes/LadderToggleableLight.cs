using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

// This patch is mainly intended as a fix for the stock "Kelus-LV Bay Mobility Enhancer" light being always active, 
// even when the ladder is retracted.
// But this also provide a generalized way of linking a RetractableLadder module to a ModuleLight module, see comments
// in the RetractableLadderLightController module (further in this file) for how to use it.

namespace KSPCommunityFixes.BugFixes
{
    public class LadderToggleableLight : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(RetractableLadder), nameof(RetractableLadder.OnStart))));
        }

        static IEnumerable<CodeInstruction> RetractableLadder_OnStart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_KerbalFSM_StartFSM = AccessTools.Method(typeof(KerbalFSM), nameof(KerbalFSM.StartFSM), new[] { typeof(string) });
            MethodInfo m_PatchRetractableLadderStateMachine = AccessTools.Method(typeof(LadderToggleableLight), nameof(PatchRetractableLadderStateMachine));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, m_KerbalFSM_StartFSM))
                {
                    for (int j = i; j > i - 6; j--)
                    {
                        if (code[j].opcode == OpCodes.Ldarg_0)
                        {
                            code.Insert(j, new CodeInstruction(OpCodes.Ldarg_0));
                            code.Insert(j + 1, new CodeInstruction(OpCodes.Call, m_PatchRetractableLadderStateMachine));
                            break;
                        }
                    }
                    break;
                }
            }

            return code;
        }

        static void PatchRetractableLadderStateMachine(RetractableLadder retractableLadder)
        {
            retractableLadder.st_retracted.OnEnter += delegate
            {
                RetractableLadderLightController controller = RetractableLadderLightController.GetController(retractableLadder);
                if (controller.IsNullRef())
                    return;

                controller.ToggleLight(false);
            };

            retractableLadder.st_retracted.OnLeave += delegate
            {
                RetractableLadderLightController controller = RetractableLadderLightController.GetController(retractableLadder);
                if (controller.IsNullRef())
                    return;

                controller.ToggleLight(true);
            };
        }
    }

    public class RetractableLadderLightController : PartModule
    {
        /// <summary>
        /// If not defined, the controller will target the first found RetractableLadder on the part.
        /// If defined, the controller will target the RetractableLadder with a matching "ladderAnimationRootName".
        /// </summary>
        [KSPField] public string ladderAnimationRootName;

        /// <summary>
        /// If not defined, the controller will target the first found ModuleLight on the part.
        /// If defined, the controller will target the ModuleLight with a matching "moduleID".
        /// </summary>
        [KSPField] public string lightModuleID;

        private bool refsLoaded;
        private RetractableLadder retractableLadder;
        private ModuleLight moduleLight;

        public override void OnStart(StartState state)
        {
            LoadRefs();
        }

        private void LoadRefs()
        {
            refsLoaded = true;

            for (int i = part.modules.Count; i-- > 0;)
            {
                PartModule pm = part.modules[i];
                if (moduleLight.IsNullRef() && pm is ModuleLight ml)
                {
                    if (!string.IsNullOrEmpty(lightModuleID))
                    {
                        if (ml.moduleID == lightModuleID)
                        {
                            moduleLight = ml;
                        }
                    }
                    else
                    {
                        moduleLight = ml;
                    }
                }
                else if (retractableLadder.IsNullRef() && pm is RetractableLadder rl)
                {
                    if (!string.IsNullOrEmpty(ladderAnimationRootName))
                    {
                        if (rl.ladderAnimationRootName == ladderAnimationRootName)
                        {
                            retractableLadder = rl;
                        }
                    }
                    else
                    {
                        retractableLadder = rl;
                    }
                }
            }
        }

        public void ToggleLight(bool lightOn)
        {
            if (moduleLight.IsNullRef())
                return;

            if (lightOn)
                moduleLight.LightsOn();
            else
                moduleLight.LightsOff();

        }

        public static RetractableLadderLightController GetController(RetractableLadder ladder)
        {
            List<PartModule> pmList = ladder.part.modules.modules;
            for (int i = pmList.Count; i-- > 0;)
            {
                if (pmList[i] is RetractableLadderLightController rllc)
                {
                    if (!rllc.refsLoaded)
                        rllc.LoadRefs();

                    if (rllc.retractableLadder.RefEquals(ladder))
                        return rllc;
                }
            }

            return null;
        }
    }
}
