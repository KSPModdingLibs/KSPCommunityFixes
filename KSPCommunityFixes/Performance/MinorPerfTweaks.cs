using Contracts.Predicates;
using Expansions;
using Expansions.Serenity;
using HarmonyLib;
using ModuleWheels;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class MinorPerfTweaks : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, AccessTools.PropertyGetter(typeof(FlightGlobals), nameof(FlightGlobals.fetch)), nameof(FlightGlobals_fetch_Override));

            AddPatch(PatchType.Override, typeof(VolumeNormalizer), nameof(VolumeNormalizer.Update));

            AddPatch(PatchType.Override, typeof(MonoUtilities), nameof(MonoUtilities.RefreshContextWindows));
            AddPatch(PatchType.Override, typeof(MonoUtilities), nameof(MonoUtilities.RefreshPartContextWindow));

            AddPatch(PatchType.Transpiler, typeof(PQS), nameof(PQS.StartSphere));

            // Part has a large number of isX methods that boil down to part.HasModuleImplementing<T>
            // or part.FindModuleImplementing<T>. Override them to use the cached Fast variants instead.
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isKerbalEVA));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isCargoPart));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isInventoryPart));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isGroundDeployable));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isLaunchClamp));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isAnchoredDecoupler));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isDecoupler));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isDockingPort));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isAirIntake));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isSolarPanel));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRadiator));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isAntenna));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isGenerator));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isFairing));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isEngine), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isParachute));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRobotic), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticRotor), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticRotor), [typeof(ModuleRoboticServoRotor).MakeByRefType()], nameof(Part_isRoboticRotor_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticHinge), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticHinge), [typeof(ModuleRoboticServoHinge).MakeByRefType()], nameof(Part_isRoboticHinge_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticPiston), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticPiston), [typeof(ModuleRoboticServoPiston).MakeByRefType()], nameof(Part_isRoboticPiston_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticRotationServo), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticRotationServo), [typeof(ModuleRoboticRotationServo).MakeByRefType()], nameof(Part_isRoboticRotationServo_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isControlSurface), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isControlSurface), [typeof(ModuleControlSurface).MakeByRefType()], nameof(Part_isControlSurface_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isBaseServo));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticController), Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isRoboticController), [typeof(ModuleRoboticController).MakeByRefType()], nameof(Part_isRoboticController_Out_Override));
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isKerbalSeat));
            AddPatch(PatchType.Override, typeof(Part), "hasWheelDamage", Type.EmptyTypes);
            AddPatch(PatchType.Override, typeof(Part), "hasWheelDamage", [typeof(ModuleWheelDamage).MakeByRefType()], nameof(Part_hasWheelDamage_Out_Override));
        }

        // When FlightGlobals._fetch is null/destroyed, the stock "fetch" getter fallback to a FindObjectOfType()
        // call. This is extremly slow, and account for ~10% of the total loading time (7 seconds) of the total
        // launch > main menu on stock + BDB install, due to being called during part compilation.
        // The _fetch field is acquired from Awake() and set to null in OnDestroy(), so there is no real reason for this.
        // The only behavior change I can think of would be something calling fetch in between the OnDestroy()
        // call and the effective destruction of the native object. In any case, this can be qualified as a bug,
        // as the flightglobal instance left accessible will be in quite invalid state.
        private static FlightGlobals FlightGlobals_fetch_Override()
        {
            return FlightGlobals._fetch.IsNullOrDestroyed() ? null : FlightGlobals._fetch;
        }

        // setting AudioListener.volume is actually quite costly (0.7% of the frame time),
        // so avoid setting it when the value hasn't actually changed...
        private static void VolumeNormalizer_Update_Override(VolumeNormalizer vn)
        {
            float newVolume;
            if (GameSettings.SOUND_NORMALIZER_ENABLED)
            {
                vn.threshold = GameSettings.SOUND_NORMALIZER_THRESHOLD;
                vn.sharpness = GameSettings.SOUND_NORMALIZER_RESPONSIVENESS;
                AudioListener.GetOutputData(vn.samples, 0);
                vn.level = 0f;

                for (int i = 0; i < vn.sampleCount; i += 1 + GameSettings.SOUND_NORMALIZER_SKIPSAMPLES)
                    vn.level = Mathf.Max(vn.level, Mathf.Abs(vn.samples[i]));

                if (vn.level > vn.threshold)
                    newVolume = vn.threshold / vn.level;
                else
                    newVolume = 1f;

                newVolume = Mathf.Lerp(AudioListener.volume, newVolume * GameSettings.MASTER_VOLUME, vn.sharpness * Time.deltaTime);
            }
            else
            {
                newVolume = Mathf.Lerp(AudioListener.volume, GameSettings.MASTER_VOLUME, vn.sharpness * Time.deltaTime);
            }

            if (newVolume != vn.volume)
                AudioListener.volume = newVolume;

            vn.volume = newVolume;
        }

        // MonoUtilities.RefreshContextWindows calls Object.FindObjectsOfType.
        // This is quite slow. The method is not used in stock but several mods
        // do use it and it would otherwise take up a notable chunk of scene
        // switch times.
        private static void MonoUtilities_RefreshContextWindows_Override(Part part)
        {
            if (part.IsNotNullRef() && part.PartActionWindow.IsNotNullOrDestroyed())
                part.PartActionWindow.displayDirty = true;
        }

        private static void MonoUtilities_RefreshPartContextWindow_Override(Part part)
        {
            if (part.IsNotNullRef() && part.PartActionWindow.IsNotNullOrDestroyed())
                part.PartActionWindow.displayDirty = true;
        }

        private static PQSCache GetPQSCache(Type _)
        {
            var instance = PQSCache.Instance;
            if (!instance.IsNullOrDestroyed())
                return instance;

            // If we can't find a valid instance then fall back to FindObjectOfType
            return (PQSCache)UnityEngine.Object.FindObjectOfType(typeof(PQSCache));
        }

        // PQS.StartSphere calls out to FindObjectOfType. Since it is called a
        // number of times during scene switch, this can add up to >1s of overhead
        // during scene switches.
        //
        // PQSCache already tracks the active instance so this just replaces the
        // call to FindObjectOfType with one that reads from PQSCache.Instance.
        private static IEnumerable<CodeInstruction> PQS_StartSphere_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var findObjectOfTypeMethod = SymbolExtensions.GetMethodInfo(
                () => UnityEngine.Object.FindObjectOfType(typeof(PQSCache))
            );

            var matcher = new CodeMatcher(instructions);
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, findObjectOfTypeMethod))
                .SetOperandAndAdvance(SymbolExtensions.GetMethodInfo(() => GetPQSCache(null)));

            return matcher.Instructions();
        }

        // Part has a large number of isX methods that basically boil down to
        // part.HasModuleImplementing<X> or part.FindModuleImplementing<X>. We
        // override them here to use the faster cached variants defined by KSPCF
        // and get a more or less free speedup.
        #region Part.isX overrides
        private static bool Part_isKerbalEVA_Override(Part __instance)
            => __instance.HasModuleImplementingFast<KerbalEVA>();

        private static bool Part_isCargoPart_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleCargoPart>();

        private static bool Part_isInventoryPart_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleInventoryPart>();

        private static bool Part_isGroundDeployable_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleGroundPart>();

        private static bool Part_isLaunchClamp_Override(Part __instance)
            => __instance.HasModuleImplementingFast<LaunchClamp>();

        private static bool Part_isAnchoredDecoupler_Override(Part __instance, out ModuleAnchoredDecoupler moduleAnchoredDecoupler)
            => (moduleAnchoredDecoupler = __instance.FindModuleImplementingFast<ModuleAnchoredDecoupler>()) != null;

        private static bool Part_isDecoupler_Override(Part __instance, out ModuleDecouple moduleDecoupler)
            => (moduleDecoupler = __instance.FindModuleImplementingFast<ModuleDecouple>()) != null;

        private static bool Part_isDockingPort_Override(Part __instance, out ModuleDockingNode dockingPort)
            => (dockingPort = __instance.FindModuleImplementingFast<ModuleDockingNode>()) != null;

        private static bool Part_isAirIntake_Override(Part __instance, out ModuleResourceIntake intake)
        {
            intake = __instance.FindModuleImplementingFast<ModuleResourceIntake>();
            if (intake != null && intake.resourceName != "IntakeAir")
                intake = null;
            return intake != null;
        }

        private static bool Part_isSolarPanel_Override(Part __instance, out ModuleDeployableSolarPanel solarPanel)
            => (solarPanel = __instance.FindModuleImplementingFast<ModuleDeployableSolarPanel>()) != null;

        private static bool Part_isRadiator_Override(Part __instance, out ModuleDeployableRadiator radiator)
            => (radiator = __instance.FindModuleImplementingFast<ModuleDeployableRadiator>()) != null;

        private static bool Part_isAntenna_Override(Part __instance, out ModuleDeployableAntenna antenna)
            => (antenna = __instance.FindModuleImplementingFast<ModuleDeployableAntenna>()) != null;

        private static bool Part_isGenerator_Override(Part __instance, out ModuleGenerator generator)
            => (generator = __instance.FindModuleImplementingFast<ModuleGenerator>()) != null;

        private static bool Part_isFairing_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleProceduralFairing>();

        private static bool Part_isEngine_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleEngines>();

        private static bool Part_isParachute_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleParachute>();

        private static bool Part_isRobotic_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<BaseServo>();

        private static bool Part_isRoboticRotor_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<ModuleRoboticServoRotor>();

        private static bool Part_isRoboticRotor_Out_Override(Part __instance, out ModuleRoboticServoRotor rotor)
            => (rotor = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<ModuleRoboticServoRotor>()
                : null) != null;

        private static bool Part_isRoboticHinge_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<ModuleRoboticServoHinge>();

        private static bool Part_isRoboticHinge_Out_Override(Part __instance, out ModuleRoboticServoHinge hinge)
            => (hinge = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<ModuleRoboticServoHinge>()
                : null) != null;

        private static bool Part_isRoboticPiston_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<ModuleRoboticServoPiston>();

        private static bool Part_isRoboticPiston_Out_Override(Part __instance, out ModuleRoboticServoPiston piston)
            => (piston = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<ModuleRoboticServoPiston>()
                : null) != null;

        private static bool Part_isRoboticRotationServo_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<ModuleRoboticRotationServo>();

        private static bool Part_isRoboticRotationServo_Out_Override(Part __instance, out ModuleRoboticRotationServo rotationServo)
            => (rotationServo = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<ModuleRoboticRotationServo>()
                : null) != null;

        private static bool Part_isControlSurface_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleControlSurface>();

        private static bool Part_isControlSurface_Out_Override(Part __instance, out ModuleControlSurface controlSurface)
            => (controlSurface = __instance.FindModuleImplementingFast<ModuleControlSurface>()) != null;

        private static bool Part_isBaseServo_Override(Part __instance, out BaseServo servo)
            => (servo = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<BaseServo>()
                : null) != null;

        private static bool Part_isRoboticController_Override(Part __instance)
            => ExpansionsLoader.IsExpansionInstalled("Serenity")
                && __instance.HasModuleImplementingFast<ModuleRoboticController>();

        private static bool Part_isRoboticController_Out_Override(Part __instance, out ModuleRoboticController controller)
            => (controller = ExpansionsLoader.IsExpansionInstalled("Serenity")
                ? __instance.FindModuleImplementingFast<ModuleRoboticController>()
                : null) != null;

        private static bool Part_isKerbalSeat_Override(Part __instance)
            => __instance.HasModuleImplementingFast<KerbalSeat>();

        private static bool Part_hasWheelDamage_Override(Part __instance)
            => __instance.HasModuleImplementingFast<ModuleWheelDamage>();

        private static bool Part_hasWheelDamage_Out_Override(Part __instance, out ModuleWheelDamage wheelDamage)
            => (wheelDamage = __instance.FindModuleImplementingFast<ModuleWheelDamage>()) != null;
        #endregion
    }
}
