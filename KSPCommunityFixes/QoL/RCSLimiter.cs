/*
This patch implements two extra tweakables in the RCS module "Actuation Toggles" Part Action Window.

By default, ModuleRCS will scale down the actuation level of each nozzle depending on how far the 
thrust force is from the "ideal" angle for a given actuation request (unless the "always full action"
toggle is enabled). 

This patch gives the ability to define a separate angle threshold for linear and rotation actuation. 
If the resulting angle from a nozzle thrust force is below that threshold, that nozzle won't fire at
all instead of firing at a reduced level. This allow to optimize efficiency, especially in the case 
of multi-nozzle RCS parts that are impossible to fine-tune with only the actuation toggles.

The default angle limits can be defined in the ModuleRCS / ModuleRCSFX configuration by adding 
`minRotationAlignement` and `minlinearAlignement` fields (value in degrees). If they aren't defined,
they default to 90° (no limit, behavior similar to stock).

To make RCS tweaking easier, the patch also add a potential torque/force readout to the actuation
toggles PAW items. In the editor, the actuation orientation is defined by the first found command 
module, starting from the root part (matching the command module that will be selected as the control
point when launching the ship). The readout also takes the RCS module ISP curve into account, and
uses the currently selected body and state (sea level/altitude/vacuum) of the stock DeltaV app.

The modification to the RCS control scheme is taken into account by the custom KSPCF 
ModuleRCS.GetPotentialTorque() implementation. As of writing, all mods reimplement their own
version of that method, and all of them are ignoring the stock control scheme anyway, so the behavior
change introduced in this patch won't make a significant difference in most cases.
Note that RCSBuildAid tries to simulate the stock control scheme, but its implementation doesn't
reproduce stock behavior correctly, which is why its torque readout doesn't always match the KSPCF one.
*/

using System;
using System.Collections;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;
using UnityEngine;
using KSPCommunityFixes.Modding;

namespace KSPCommunityFixes.QoL
{
    [PatchPriority(Order = 10)]
    class RCSLimiter : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override bool CanApplyPatch(out string reason)
        {
            if (KSPCommunityFixes.GetPatchInstance<BaseFieldListUseFieldHost>() == null)
            {
                reason = $"The {nameof(RCSLimiter)} patch requires the {nameof(BaseFieldListUseFieldHost)} to be enabled";
                return false;
            }
            return base.CanApplyPatch(out reason);
        }

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(ModuleRCS), nameof(ModuleRCS.OnAwake));
            AddPatch(PatchType.Postfix, typeof(ModuleRCS), nameof(ModuleRCS.OnDestroy));
            AddPatch(PatchType.Postfix, typeof(ModuleRCS), nameof(ModuleRCS.UpdateToggles));
            AddPatch(PatchType.Prefix, typeof(ModuleRCS), nameof(ModuleRCS.FixedUpdate));
            AddPatch(PatchType.Postfix, typeof(ModuleRCS), nameof(ModuleRCS.Update));

            moduleRCSExtensions = new Dictionary<ModuleRCS, ModuleRCSExtension>();

            autoLOC_6001330_Pitch = Localizer.Format("#autoLOC_6001330");
            autoLOC_6001331_Yaw = Localizer.Format("#autoLOC_6001331");
            autoLOC_6001332_Roll = Localizer.Format("#autoLOC_6001332");

            autoLOC_6001364_PortStbd = Localizer.Format("#autoLOC_6001364");
            string[] autoLOC_6001364 = autoLOC_6001364_PortStbd.Split('/');
            if (autoLOC_6001364.Length == 2)
            {
                autoLOC_Port = autoLOC_6001364[0];
                autoLOC_Stbd = autoLOC_6001364[1];
            }

            autoLOC_6001365_DorsalVentral = Localizer.Format("#autoLOC_6001365");
            string[] autoLOC_6001365 = autoLOC_6001365_DorsalVentral.Split('/');
            if (autoLOC_6001365.Length == 2)
            {
                autoLOC_Dorsal = autoLOC_6001365[0];
                autoLOC_Ventral = autoLOC_6001365[1];
            }

            autoLOC_6001366_ForeAft = Localizer.Format("#autoLOC_6001366");
            string[] autoLOC_6001366 = autoLOC_6001366_ForeAft.Split('/');
            if (autoLOC_6001366.Length == 2)
            {
                autoLOC_Fore = autoLOC_6001366[0];
                autoLOC_Aft = autoLOC_6001366[1];
            }
        }

        private static string autoLOC_6001330_Pitch;
        private static string autoLOC_6001331_Yaw;
        private static string autoLOC_6001332_Roll;
        private static string autoLOC_6001364_PortStbd;
        private static string autoLOC_6001365_DorsalVentral;
        private static string autoLOC_6001366_ForeAft;

        private static string autoLOC_Port;
        private static string autoLOC_Stbd;
        private static string autoLOC_Dorsal;
        private static string autoLOC_Ventral;
        private static string autoLOC_Fore;
        private static string autoLOC_Aft;

        internal static Dictionary<ModuleRCS, ModuleRCSExtension> moduleRCSExtensions;

        static void ModuleRCS_OnAwake_Postfix(ModuleRCS __instance)
        {
            moduleRCSExtensions.Add(__instance, new ModuleRCSExtension(__instance));
        }

        static void ModuleRCS_OnDestroy_Postfix(ModuleRCS __instance)
        {
            moduleRCSExtensions.Remove(__instance);
        }

        static void ModuleRCS_UpdateToggles_Postfix(ModuleRCS __instance)
        {
            if (moduleRCSExtensions.TryGetValue(__instance, out ModuleRCSExtension rcsExt))
            {
                bool guiActive = __instance.showToggles && __instance.currentShowToggles && __instance.moduleIsEnabled;
                rcsExt.minRotationAlignementField.guiActive = guiActive;
                rcsExt.minRotationAlignementField.guiActiveEditor = guiActive;
                rcsExt.minLinearAlignementField.guiActive = guiActive;
                rcsExt.minLinearAlignementField.guiActiveEditor = guiActive;
            }
        }

        static bool ActuationToggleDisplayed(ModuleRCS mrcs)
        {
            if (!mrcs.showToggles || !mrcs.currentShowToggles)
                return false;

            if (mrcs.part.PartActionWindow == null || !mrcs.part.PartActionWindow.isActiveAndEnabled)
                return false;

            return true;
        }

        internal class ModuleRCSExtension
        {
            public static FieldInfo minRotationAlignementFieldInfo = AccessTools.Field(typeof(ModuleRCSExtension), nameof(minRotationAlignement));
            public static KSPField minRotationAlignementKSPField = new KSPField();
            public static UI_FloatRange minRotationAlignementControl = new UI_FloatRange();

            public static FieldInfo minLinearAlignementFieldInfo = AccessTools.Field(typeof(ModuleRCSExtension), nameof(minLinearAlignement));
            public static KSPField minLinearAlignementKSPField = new KSPField();
            public static UI_FloatRange minLinearAlignementControl = new UI_FloatRange();

            static ModuleRCSExtension()
            {
                minRotationAlignementKSPField.guiActive = false;
                minRotationAlignementKSPField.guiActiveEditor = false;
                minRotationAlignementKSPField.guiName = "Min rotation alignement";
                minRotationAlignementKSPField.guiFormat = "0°";
                minRotationAlignementKSPField.isPersistant = true;

                minRotationAlignementControl.minValue = 5f;
                minRotationAlignementControl.maxValue = 90f;
                minRotationAlignementControl.stepIncrement = 1f;
                minRotationAlignementControl.affectSymCounterparts = UI_Scene.All;

                minLinearAlignementKSPField.guiActive = false;
                minLinearAlignementKSPField.guiActiveEditor = false;
                minLinearAlignementKSPField.guiName = "Min translation alignement";
                minLinearAlignementKSPField.guiFormat = "0°";
                minLinearAlignementKSPField.isPersistant = true;

                minLinearAlignementControl.minValue = 5f;
                minLinearAlignementControl.maxValue = 90f;
                minLinearAlignementControl.stepIncrement = 1f;
                minLinearAlignementControl.affectSymCounterparts = UI_Scene.All;
            }

            public ModuleRCS module;

            public BaseField minRotationAlignementField;
            public float minRotationAlignement;

            public BaseField minLinearAlignementField;
            public float minLinearAlignement;

            public float lastTorqueUpdateFrame;

            public BaseField enablePitchField;
            public BaseField enableRollField;
            public BaseField enableYawField;
            public BaseField enableXField;
            public BaseField enableYField;
            public BaseField enableZField;

            public ModuleRCSExtension(ModuleRCS module)
            {
                this.module = module;

                minRotationAlignementField = new BaseField(minRotationAlignementKSPField, minRotationAlignementFieldInfo, this);
                minRotationAlignementField.uiControlEditor = minRotationAlignementControl;
                minRotationAlignementField.uiControlFlight = minRotationAlignementControl;
                module.Fields.Add(minRotationAlignementField);

                minLinearAlignementField = new BaseField(minLinearAlignementKSPField, minLinearAlignementFieldInfo, this);
                minLinearAlignementField.uiControlEditor = minLinearAlignementControl;
                minLinearAlignementField.uiControlFlight = minLinearAlignementControl;
                module.Fields.Add(minLinearAlignementField);

                enablePitchField = module.Fields[nameof(ModuleRCS.enablePitch)];
                enableRollField = module.Fields[nameof(ModuleRCS.enableRoll)];
                enableYawField = module.Fields[nameof(ModuleRCS.enableYaw)];
                enableXField = module.Fields[nameof(ModuleRCS.enableX)];
                enableYField = module.Fields[nameof(ModuleRCS.enableY)];
                enableZField = module.Fields[nameof(ModuleRCS.enableZ)];

                int moduleIndex = module.part.modules.IndexOf(module);
                if (moduleIndex != -1
                    && module.part.partInfo?.partPrefab != null
                    && moduleIndex < module.part.partInfo.partPrefab.modules.Count
                    && module.part.partInfo.partPrefab.modules[moduleIndex] is ModuleRCS prefabModule
                    && moduleRCSExtensions.TryGetValue(prefabModule, out ModuleRCSExtension prefabLimits))
                {
                    minRotationAlignement = prefabLimits.minRotationAlignement;
                    minLinearAlignement = prefabLimits.minLinearAlignement;
                }
                else
                {
                    minRotationAlignement = 90f;
                    minLinearAlignement = 90f;
                }
            }

            public void UpdatePAWTorqueAndForces(Transform referenceTransform, float thrustForce, Vector3 currentCoM)
            {
                Vector3 posTorque = Vector3.zero;
                Vector3 negTorque = Vector3.zero;
                Vector3 posForce = Vector3.zero;
                Vector3 negForce = Vector3.zero;

                Quaternion controlRotation = referenceTransform.rotation;

                Vector3 pitchCtrl = controlRotation * Vector3.right;
                Vector3 rollCtrl = controlRotation * Vector3.up;
                Vector3 yawCtrl = controlRotation * Vector3.forward;

                float minRotActuation = Mathf.Max(Mathf.Cos(minRotationAlignement * Mathf.Deg2Rad), 0f);
                float minLinActuation = Mathf.Max(Mathf.Cos(minLinearAlignement * Mathf.Deg2Rad), 0f);

                for (int i = module.thrusterTransforms.Count - 1; i >= 0; i--)
                {
                    Transform thruster = module.thrusterTransforms[i];

                    if (!thruster.gameObject.activeInHierarchy)
                        continue;

                    Vector3 thrusterPosFromCoM = thruster.position - currentCoM;
                    Vector3 thrusterDirFromCoM = thrusterPosFromCoM.normalized;
                    Vector3 thrustDirection = module.useZaxis ? thruster.forward : thruster.up;

                    Vector3 thrusterThrust = thrustDirection * thrustForce;
                    Vector3 thrusterTorque = Vector3.Cross(thrusterPosFromCoM, thrusterThrust);
                    // transform in vessel control space
                    thrusterTorque = referenceTransform.InverseTransformDirection(thrusterTorque);

                    if (module.enablePitch && Math.Abs(thrusterTorque.x) > 0.0001f)
                    {
                        Vector3 pitchRot = Vector3.Cross(pitchCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, pitchCtrl));
                        float actuation = Vector3.Dot(thrustDirection, pitchRot.normalized);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minRotActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posTorque.x += thrusterTorque.x * actuation;
                            else
                                negTorque.x += thrusterTorque.x * actuation;
                        }
                    }

                    if (module.enableRoll && Math.Abs(thrusterTorque.y) > 0.0001f)
                    {
                        Vector3 rollRot = Vector3.Cross(rollCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, rollCtrl));
                        float actuation = Vector3.Dot(thrustDirection, rollRot.normalized);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minRotActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posTorque.y += thrusterTorque.y * actuation;
                            else
                                negTorque.y += thrusterTorque.y * actuation;
                        }
                    }

                    if (module.enableYaw && Math.Abs(thrusterTorque.z) > 0.0001f)
                    {
                        Vector3 yawRot = Vector3.Cross(yawCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, yawCtrl));
                        float actuation = Vector3.Dot(thrustDirection, yawRot.normalized);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minRotActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posTorque.z += thrusterTorque.z * actuation;
                            else
                                negTorque.z += thrusterTorque.z * actuation;
                        }
                    }

                    if (module.enableX)
                    {
                        float actuation = Vector3.Dot(thrustDirection, pitchCtrl);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minLinActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posForce.x += thrustForce * actuation;
                            else
                                negForce.x -= thrustForce * actuation;
                        }
                    }

                    if (module.enableY)
                    {
                        float actuation = Vector3.Dot(thrustDirection, yawCtrl);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minLinActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posForce.z += thrustForce * actuation;
                            else
                                negForce.z -= thrustForce * actuation;
                        }
                    }

                    if (module.enableZ)
                    {
                        float actuation = Vector3.Dot(thrustDirection, rollCtrl);
                        float actuationMagnitude = Math.Abs(actuation);

                        if (actuationMagnitude < minLinActuation)
                            actuation = 0f;
                        else if (module.fullThrust && actuationMagnitude > module.fullThrustMin)
                            actuation = Math.Sign(actuation);

                        if (actuation != 0f)
                        {
                            if (actuation > 0f)
                                posForce.y += thrustForce * actuation;
                            else
                                negForce.y -= thrustForce * actuation;
                        }
                    }
                }

                if (module.enablePitch)
                    SetToggleGuiName(enablePitchField, $"{autoLOC_6001330_Pitch}: {Math.Round(posTorque.x, 3):G3} / {-Math.Round(negTorque.x, 3):G3} kNm");
                else
                    SetToggleGuiName(enablePitchField, autoLOC_6001330_Pitch);

                if (module.enableRoll)
                    SetToggleGuiName(enableRollField, $"{autoLOC_6001332_Roll}: {Math.Round(posTorque.y, 3):G3} / {-Math.Round(negTorque.y, 3):G3} kNm");
                else
                    SetToggleGuiName(enableRollField, autoLOC_6001332_Roll);

                if (module.enableYaw)
                    SetToggleGuiName(enableYawField, $"{autoLOC_6001331_Yaw}: {Math.Round(posTorque.z, 3):G3} / {-Math.Round(negTorque.z, 3):G3} kNm");
                else
                    SetToggleGuiName(enableYawField, autoLOC_6001331_Yaw);

                if (module.enableX)
                    SetToggleGuiName(enableXField, autoLOC_Port == null
                            ? $"{autoLOC_6001364_PortStbd}: {Math.Round(posForce.x, 3):G1} / {Math.Round(negForce.x, 3):G1} kN"
                            : $"{autoLOC_Port}: {Math.Round(posForce.x, 3):G1}kN / {autoLOC_Stbd}: {Math.Round(negForce.x, 3):G1}kN");
                else
                    SetToggleGuiName(enableXField, autoLOC_6001364_PortStbd);

                if (module.enableY)
                    SetToggleGuiName(enableYField, autoLOC_Dorsal == null
                        ? $"{autoLOC_6001365_DorsalVentral}: {Math.Round(posForce.z, 3):G1} / {Math.Round(negForce.z, 3):G1} kN"
                        : $"{autoLOC_Dorsal}: {Math.Round(posForce.z, 3):G1}kN / {autoLOC_Ventral}: {Math.Round(negForce.z, 3):G1}kN");
                else
                    SetToggleGuiName(enableYField, autoLOC_6001365_DorsalVentral);

                if (module.enableZ)
                    SetToggleGuiName(enableZField, autoLOC_Fore == null
                        ? $"{autoLOC_6001366_ForeAft}: {Math.Round(posForce.y, 3):G1} / {Math.Round(negForce.y, 3):G1} kN"
                        : $"{autoLOC_Fore}: {Math.Round(posForce.y, 3):G1}kN / {autoLOC_Aft}: {Math.Round(negForce.y, 3):G1}kN");
                else
                    SetToggleGuiName(enableZField, autoLOC_6001366_ForeAft);
            }

            public void DisableTorquePAWInfo()
            {
                SetToggleGuiName(enablePitchField, autoLOC_6001330_Pitch);
                SetToggleGuiName(enableRollField, autoLOC_6001332_Roll);
                SetToggleGuiName(enableYawField, autoLOC_6001331_Yaw);
                SetToggleGuiName(enableXField, autoLOC_6001364_PortStbd);
                SetToggleGuiName(enableYField, autoLOC_6001365_DorsalVentral);
                SetToggleGuiName(enableZField, autoLOC_6001366_ForeAft);
            }

            public static void SetToggleGuiName(BaseField baseField, string guiName)
            {
                baseField.guiName = guiName;

                UIPartActionToggle toggle;

                if (!ReferenceEquals(baseField.uiControlEditor.partActionItem, null))
                    toggle = (UIPartActionToggle)baseField.uiControlEditor.partActionItem;
                else if (!ReferenceEquals(baseField.uiControlFlight.partActionItem, null))
                    toggle = (UIPartActionToggle)baseField.uiControlFlight.partActionItem;
                else
                    return;

                toggle.fieldName.text = guiName;
                toggle.fieldName.rectTransform.sizeDelta = new Vector2(150f, toggle.fieldName.rectTransform.sizeDelta.y);

            }
        }

        #region Editor PAW update

        static void ModuleRCS_Update_Postfix(ModuleRCS __instance)
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
                return;

            if (!ActuationToggleDisplayed(__instance))
                return;

            if (!moduleRCSExtensions.TryGetValue(__instance, out ModuleRCSExtension rcsExt))
                return;

            if (__instance.part.frozen
                || !__instance.moduleIsEnabled
                || !__instance.rcsEnabled
                || __instance.isJustForShow
                || (__instance.part.ShieldedFromAirstream && !__instance.shieldedCanThrust)
                || !EditorSetupPropellant(__instance))
            {
                rcsExt.DisableTorquePAWInfo();
                return;
            }

            if (EditorPhysics.TryGetAndUpdate(out EditorPhysics editorPhysics))
            {
                if (rcsExt.lastTorqueUpdateFrame < editorPhysics.lastShipModificationFrame)
                {
                    rcsExt.lastTorqueUpdateFrame = editorPhysics.lastShipModificationFrame;
                    __instance.StartCoroutine(UpdatePAWDelayed(__instance, rcsExt));
                }
            }
        }

        private static IEnumerator UpdatePAWDelayed(ModuleRCS mrcs, ModuleRCSExtension rcsExt)
        {
            yield return null;

            if (EditorPhysics.TryGetAndUpdate(out EditorPhysics editorPhysics))
            {
                mrcs.realISP = mrcs.atmosphereCurve.Evaluate((float)editorPhysics.atmStaticPressure);
                double exhaustVel = mrcs.realISP * mrcs.G * mrcs.ispMult;
                float thrustForce = (float)(exhaustVel * mrcs.maxFuelFlow * mrcs.flowMult * mrcs.thrustPercentage * 0.01);

                rcsExt.UpdatePAWTorqueAndForces(editorPhysics.referenceTransform, thrustForce, editorPhysics.CoM);
            }
        }


        // ModuleRCS populate the propellant list in OnLoad(), and because the list isn't defined as serializable, it isn't populated for part instantiated in the editor.
        // So check if the list is populated, and if not copy the prefab list.
        private static bool EditorSetupPropellant(ModuleRCS mrcs)
        {
            if (mrcs.propellants.Count > 0)
                return true;

            int moduleIdx = mrcs.part.modules.IndexOf(mrcs);
            if (moduleIdx >= 0 && moduleIdx < mrcs.part.partInfo.partPrefab.modules.Count && mrcs.part.partInfo.partPrefab.modules[moduleIdx] is ModuleRCS prefabModule)
            {
                foreach (Propellant prefabPropellant in prefabModule.propellants)
                    mrcs.propellants.Add(JsonUtility.FromJson<Propellant>(JsonUtility.ToJson(prefabPropellant))); // Propellant is [Serializable], so lazy but effective

                mrcs.mixtureDensity = prefabModule.mixtureDensity;
                mrcs.mixtureDensityRecip = prefabModule.mixtureDensityRecip;
                mrcs.maxFuelFlow = prefabModule.maxFuelFlow;
            }

            return mrcs.propellants.Count > 0;
        }

        #endregion

        #region FixedUpdate

        static bool ModuleRCS_FixedUpdate_Prefix(ModuleRCS __instance)
        {
            __instance.isOperating = false;
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return false;
            }

            if (TimeWarp.CurrentRate > 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH)
            {
                if (ActuationToggleDisplayed(__instance) && moduleRCSExtensions.TryGetValue(__instance, out ModuleRCSExtension rcsExt))
                    rcsExt.DisableTorquePAWInfo();

                __instance.DeactivatePowerFX();
                return false;
            }

            __instance.tC = __instance.thrusterTransforms.Count;
            if (__instance.thrustForces.Length != __instance.tC)
            {
                __instance.thrustForces = new float[__instance.tC];
            }

            int thrusterIdx = __instance.tC;
            while (thrusterIdx-- > 0)
            {
                __instance.thrustForces[thrusterIdx] = 0f;
            }

            __instance.totalThrustForce = 0f;
            __instance.realISP = __instance.atmosphereCurve.Evaluate((float)__instance.part.staticPressureAtm);
            __instance.exhaustVel = __instance.realISP * __instance.G * __instance.ispMult;
            __instance.thrustForceRecip = 1f / __instance.thrusterPower;
            if (__instance.moduleIsEnabled && __instance.vessel != null && __instance.rcsEnabled && !__instance.IsAdjusterBreakingRCS() && (!__instance.part.ShieldedFromAirstream || __instance.shieldedCanThrust))
            {
                ModuleRCSExtension rcsExt = null;
                bool rcsActive;
                if ((rcsActive = __instance.vessel.ActionGroups[KSPActionGroup.RCS]) != __instance.rcs_active)
                {
                    __instance.rcs_active = rcsActive;
                }
                if (__instance.rcs_active && (__instance.inputRot != Vector3.zero || __instance.inputLin != Vector3.zero))
                {
                    __instance.predictedCOM = __instance.vessel.CurrentCoM;

                    float rotLimit, linLimit;
                    if (moduleRCSExtensions.TryGetValue(__instance, out rcsExt))
                    {
                        rotLimit = Mathf.Max(Mathf.Cos(rcsExt.minRotationAlignement * Mathf.Deg2Rad), 0f);
                        linLimit = Mathf.Max(Mathf.Cos(rcsExt.minLinearAlignement * Mathf.Deg2Rad), 0f);
                    }
                    else
                    {
                        rotLimit = 0f;
                        linLimit = 0f;
                    }

                    bool success = false;
                    thrusterIdx = __instance.tC;
                    while (thrusterIdx-- > 0)
                    {
                        __instance.currentThruster = __instance.thrusterTransforms[thrusterIdx];
                        if (__instance.currentThruster.position == Vector3.zero || !__instance.currentThruster.gameObject.activeInHierarchy)
                            continue;

                        Vector3 thrustDir = ((!__instance.useZaxis) ? __instance.currentThruster.up : __instance.currentThruster.forward);
                        __instance.rot = Vector3.Cross(__instance.inputRot, Vector3.ProjectOnPlane(__instance.currentThruster.position - __instance.predictedCOM, __instance.inputRot).normalized);
                        __instance.currentThrustForce = Vector3.Dot(thrustDir, __instance.rot);

                        if (__instance.currentThrustForce < rotLimit)
                            __instance.currentThrustForce = 0f;

                        float linDot = Vector3.Dot(thrustDir, __instance.inputLin);
                        if (linDot >= linLimit)
                            __instance.currentThrustForce += linDot;

                        if (__instance.currentThrustForce == 0f)
                        {
                            __instance.thrustForces[thrusterIdx] = 0f;
                            __instance.isOperating |= false;
                            __instance.UpdatePowerFX(false, thrusterIdx, 0f);
                            continue;
                        }

                        if (__instance.currentThrustForce > 1f)
                        {
                            __instance.currentThrustForce = 1f;
                        }
                        if (__instance.fullThrust && __instance.currentThrustForce >= __instance.fullThrustMin)
                        {
                            __instance.currentThrustForce = 1f;
                        }
                        if (__instance.usePrecision)
                        {
                            if (__instance.useLever)
                            {
                                __instance.leverDistance = __instance.GetLeverDistance(__instance.currentThruster, -thrustDir, __instance.predictedCOM);
                                if (__instance.leverDistance > 1f)
                                {
                                    __instance.currentThrustForce /= __instance.leverDistance;
                                }
                            }
                            else
                            {
                                __instance.currentThrustForce *= __instance.precisionFactor;
                            }
                        }
                        __instance.UpdatePropellantStatus();
                        __instance.currentThrustForce = __instance.CalculateThrust(__instance.currentThrustForce, out success);
                        __instance.thrustForces[thrusterIdx] = __instance.currentThrustForce;
                        bool isThrusting = __instance.currentThrustForce > 0f && success;
                        __instance.isOperating |= isThrusting;
                        __instance.UpdatePowerFX(isThrusting, thrusterIdx, Mathf.Clamp(__instance.currentThrustForce * __instance.thrustForceRecip, 0.1f, 1f));
                        if (isThrusting && !__instance.isJustForShow)
                        {
                            __instance.totalThrustForce += __instance.currentThrustForce;
                            __instance.part.AddForceAtPosition(-thrustDir * __instance.currentThrustForce, __instance.currentThruster.transform.position);
                        }
                    }
                }
                else
                {
                    __instance.DeactivateFX();
                }

                if (ActuationToggleDisplayed(__instance) && (rcsExt != null || moduleRCSExtensions.TryGetValue(__instance, out rcsExt)))
                {
                    float thrustForce = (float)(__instance.exhaustVel * __instance.maxFuelFlow * __instance.flowMult * __instance.thrustPercentage * 0.01);
                    rcsExt.UpdatePAWTorqueAndForces(__instance.vessel.ReferenceTransform, thrustForce, __instance.vessel.CurrentCoM);
                }
            }
            else
            {
                __instance.DeactivateFX();

                if (ActuationToggleDisplayed(__instance) && moduleRCSExtensions.TryGetValue(__instance, out ModuleRCSExtension rcsExt))
                    rcsExt.DisableTorquePAWInfo();
            }

            return false;
        }

        #endregion


    }
}