using Expansions.Serenity;
using ModuleWheels;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    internal class UpdateMOI : MonoBehaviour
    {
        public Part part;

        // runs once during FixedUpdate
        void FixedUpdate()
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (part != null && part.Rigidbody != null)
                {
                    // measured to take ~1us on solar panel and ~10us on inflateable heatshield
                    part.Rigidbody.ResetInertiaTensor();
                }
            }
        }
    }

    internal class AnimatedMOI : BasePatch
    {
        protected override void ApplyPatches()
        {
            // Add to all known animated modules
            AddPatch(PatchType.Postfix, typeof(ModuleControlSurface), nameof(ModuleControlSurface.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleParachute), nameof(ModuleParachute.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleAnimateGeneric), nameof(ModuleAnimateGeneric.OnStart));
            AddPatch(PatchType.Postfix, typeof(RetractableLadder), nameof(RetractableLadder.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleJettison), nameof(ModuleJettison.OnStart)); 
            AddPatch(PatchType.Postfix, typeof(ModuleAnimationGroup), nameof(ModuleAnimationGroup.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleRoboticServoHinge), nameof(ModuleRoboticServoHinge.OnStart));
            AddPatch(PatchType.Postfix, typeof(ModuleRoboticServoPiston), nameof(ModuleRoboticServoPiston.OnStart));

            // Collider is (partially?) disabled during animation in ModuleWheelDeployment, use workaround for that
            AddPatch(PatchType.Postfix, typeof(ModuleWheelDeployment), nameof(ModuleWheelDeployment.FSMSetup));
        }

        static void AddUpdateMOI(PartModule __instance, PartModule.StartState state)
        {
            if (state != PartModule.StartState.Editor)
            {
                UpdateMOI component = __instance.part.GetComponent<UpdateMOI>();
                if (component == null)
                {
                    component = __instance.part.gameObject.AddComponent<UpdateMOI>();
                    component.part = __instance.part;
                }
            }
        }

        static void ModuleControlSurface_OnStart_Postfix(ModuleControlSurface __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleParachute_OnStart_Postfix(ModuleParachute __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleAnimateGeneric_OnStart_Postfix(ModuleAnimateGeneric __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void RetractableLadder_OnStart_Postfix(RetractableLadder __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModulePartVariants_OnStart_Postfix(ModulePartVariants __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleDeployablePart_OnStart_Postfix(ModuleDeployablePart __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleJettison_OnStart_Postfix(ModuleJettison __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleAnimationGroup_OnStart_Postfix(ModuleAnimationGroup __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleRoboticServoHinge_OnStart_Postfix(ModuleRoboticServoHinge __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleRoboticServoPiston_OnStart_Postfix(ModuleRoboticServoPiston __instance, PartModule.StartState state)
        {
            AddUpdateMOI(__instance, state);
        }

        static void ModuleWheelDeployment_FSMSetup_Postfix(ModuleWheelDeployment __instance)
        {
            __instance.st_deployed.OnEnter += delegate { __instance.part.Rigidbody.ResetInertiaTensor(); };
            __instance.st_retracted.OnEnter += delegate { __instance.part.Rigidbody.ResetInertiaTensor(); };

            // Because collider is disabled during animation it gives invalid result
            //__instance.st_retracting.OnFixedUpdate += delegate { MarkColliderInvalid(__instance); };
            //__instance.st_deploying.OnFixedUpdate += delegate { MarkColliderInvalid(__instance); };

            // TODO: Make sure it's disabled even for other modules on this part
        }
    }
}
