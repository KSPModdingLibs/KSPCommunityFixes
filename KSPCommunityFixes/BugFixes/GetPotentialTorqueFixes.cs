using System;
using System.Collections;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using KSP.Localization;
using KSPCommunityFixes.QoL;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;
using KSPCommunityFixes.Library;

/*
This patch is a rewrite of the stock implementations for `ITorqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg)`.
All 4 of the stock implementations have various issues and are generally giving unreliable (not to say plain wrong) results.
Those issues are commented in each patch, but to summarize :
- ModuleRectionWheels is mostly ok, its only issue is to ignore the state of "authority limiter" tweakable
- ModuleRCS is giving entirely random results, and the stcok implementation just doesn't make any sense. Note that compared to
  other custom implementations (MechJeb, TCA, kOS), the KSPCF implementation account for the RCS module control scheme thrust 
  clamping and the actual thrust power (instead of the theoretical maximum).
- ModuleGimbal results are somewhat coherent, but their magnitude for pitch/yaw is wrong. They are underestimated for CoM-aligned 
  engines and vastly overestimated for engines placed off-CoM-center.
- ModuleControlSurface results are generally unreliable. Beside the fact that they can be randomly negative, the magnitude is
  usually wrong and inconsistent. Depending on part placement relative to the CoM, they can return zero available torque or being 
  vastly overestimated. They also don't account for drag induced torque, and are almost entirely borked when a control surface is 
  in the deployed state.

Note that the KSPCF GetPotentialTorque() implementations for ModuleControlSurface and especially for ModuleGimbal are more 
computationally intensive that the stock ones. Profiling a stock Dynawing with RCS enabled during ascent show a ~30% degradation 
when summing the vessel total available torque (~250 calls median : 0.31ms vs 0.24ms, frame time : 1.81% vs 1.46% ). Overall
this feels acceptable, but this is still is a non-negligible impact that will likely be noticeable in some cases (ie, 
atmospheric flight with a large vessel having many gimballing engines and control surfaces).
The implementations are pretty naive and could probably be vastly optimized by someone with a better understanding than me of 
the underlying maths and physics.

The KSPCF implementations follow these conventions :
- in pos/neg : x is pitch, y is roll, z is yaw
- `pos` is the actuation induced torque for a positive FlightCtrlState (pitch = 1, roll, = 1 yaw = 1) control request
- `neg` is the actuation induced torque for a negative FlightCtrlState (pitch = -1, roll, = -1 yaw = -1) control request
- Contrary to the stock implementations, values are strictly the **actuation induced** torque (ie, the torque difference
  between the neutral state and the actuated state). Especially in the case of ModuleGimbal, the stock implementation
  returns the actuation torque plus the eventual "structural" torque due to an eventual CoM/CoT misalignement.
- Positive values mean actuation will induce a torque in the desired direction. Negatives values mean that actuation will
  induce a torque in the opposite direction. For example, a negative `pos.x` value mean that for a positive roll actuation
  (ctrlState.roll = 1), the torque provider will produce a torque inducing a negative roll, essentially reducing the total
  available torque in that direction. This can notably happen with the stock aero control surfaces, due to their control 
  scheme being only based on their relative position/orientation to the vessel CoM and ignoring other factors like AoA.
- Like the stock implementations, they will give reliable results only if called from FixedUpdate(), including the control 
  state callbacks like `Vessel.OnFlyByWire` or `Vessel.On*AutopilotUpdate`. Calling them from the Update() loop will result 
  in an out-of-sync CoM position being used, producing garbage results.

So in the context of the KSPCF patch, a correct implementation of a `GetVesselPotentialTorque()` method is :
 ```cs
 foreach (ITorqueProvider torqueProvider)
 {
   torqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
   vesselPosTorque += pos;
   vesselNegTorque += neg;
 }
 if (vesselPosTorque.x < 0f) vesselPosTorque.x = 0f;
 if (vesselPosTorque.y < 0f) vesselPosTorque.y = 0f;
 if (vesselPosTorque.z < 0f) vesselPosTorque.z = 0f;
 if (vesselNegTorque.x < 0f) vesselNegTorque.x = 0f;
 if (vesselNegTorque.y < 0f) vesselNegTorque.y = 0f;
 if (vesselNegTorque.z < 0f) vesselNegTorque.z = 0f;
 ```

Quick review of how the stock implementations are handled in the modding ecosystem :
- *It seems* Mechjeb doesn't care about a value being from "pos" or "neg", it assume a negative value from either of the vector3 
  is a negative torque component (ie, if "pos.x" or "neg.x" is negative, it add that as negative available torque around x).
  Ref : https://github.com/MuMech/MechJeb2/blob/f5c1193813da7d2e2e347f963dd4ee4b7fb11a90/MechJeb2/VesselState.cs#L1073-L1076
  Ref2 : https://github.com/MuMech/MechJeb2/blob/f5c1193813da7d2e2e347f963dd4ee4b7fb11a90/MechJeb2/Vector6.cs#L82-L93
  As it is, since MechJeb doesn't care for pos/neg and only consider the max, the patches will result in wrong values, but arguably 
  since it reimplement RCS they will only be "different kind of wrong" for control surfaces and gimbals, and probably "less wrong"
  overall.
- kOS assume that the absolute value should be used.
  (side note : kOS reimplements ModuleReactionWheel.GetPotentialTorque() to get around the authority limiter bug)
  Ref : https://github.com/KSP-KOS/KOS/blob/7b7874153bc6c428404b3a1a913487b2fd0a9d99/src/kOS/Control/SteeringManager.cs#L658-L664
  The patches should apply mostly alright for kOS, at the exception of occasional negative values for gimbals and control surfaces
  being treated as positive, resulting in a higher available torque than what it should.
- TCA doesn't seem aware of the possibility of negative values, it assume they are positive.
  Ref : https://github.com/allista/ThrottleControlledAvionics/blob/b79a7372ab69616801f9953256b43ee872b90cf2/VesselProps/TorqueProps.cs#L167-L169
  The patches should more or less work for TCA, at the exception of negative gimbal/control surfaces values being treated incorrectly
  and the reaction wheels authority limiter being applied twice.
- Atmospheric Autopilot replace the stock module implementation by its own and doesn't use the interface at all
  Ref : https://github.com/Boris-Barboris/AtmosphereAutopilot/blob/master/AtmosphereAutopilot/SyncModuleControlSurface.cs
- FAR implements a replacement for ModuleControlSurface and consequently has a custom GetPotentialTorque() implementation.
  It seems that it will *always* return positive "pos" values and negative "neg" values :
  Ref : https://github.com/dkavolis/Ferram-Aerospace-Research/blob/95e127ae140b4be9699da8783d24dd8db726d753/FerramAerospaceResearch/LEGACYferram4/FARControllableSurface.cs#L294-L300
*/

namespace KSPCommunityFixes.BugFixes
{
    class GetPotentialTorqueFixes : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(ModuleReactionWheel), nameof(ModuleReactionWheel.GetPotentialTorque));
            AddPatch(PatchType.Override, typeof(ModuleRCS), nameof(ModuleRCS.GetPotentialTorque));
            AddPatch(PatchType.Override, typeof(ModuleControlSurface), nameof(ModuleControlSurface.GetPotentialTorque));
            AddPatch(PatchType.Postfix, typeof(ModuleControlSurface), nameof(ModuleControlSurface.OnStart));
            AddPatch(PatchType.Override, typeof(ModuleGimbal), nameof(ModuleGimbal.GetPotentialTorque));
            AddPatch(PatchType.Postfix, typeof(ModuleGimbal), nameof(ModuleGimbal.OnStart));

            autoLOC_6001330_Pitch = Localizer.Format("#autoLOC_6001330");
            autoLOC_6001331_Yaw = Localizer.Format("#autoLOC_6001331");
            autoLOC_6001332_Roll = Localizer.Format("#autoLOC_6001332");

            KSPCommunityFixes.Instance.StartCoroutine(CustomUpdate());
        }

        private IEnumerator CustomUpdate()
        {
            while (true)
            {
                ModuleGimbalExtension.UpdateInstances();
                ModuleCtrlSrfExtension.UpdateInstances();
                yield return null;
            }
        }

        private static string autoLOC_6001330_Pitch;
        private static string autoLOC_6001331_Yaw;
        private static string autoLOC_6001332_Roll;

        #region ModuleReactionWheel

        // Fix reaction wheels reporting incorrect available torque when the "Wheel Authority" tweakable is set below 100%.
        static void ModuleReactionWheel_GetPotentialTorque_Override(ModuleReactionWheel mrw, out Vector3 pos, out Vector3 neg)
        {
            if (mrw.moduleIsEnabled && mrw.wheelState == ModuleReactionWheel.WheelState.Active && mrw.actuatorModeCycle != 2)
            {
                float authorityLimiter = mrw.authorityLimiter * 0.01f;
                neg.x = pos.x = mrw.PitchTorque * authorityLimiter;
                neg.y = pos.y = mrw.RollTorque * authorityLimiter;
                neg.z = pos.z = mrw.YawTorque * authorityLimiter;
                return;
            }

            pos = neg = Vector3.zero;
        }

        #endregion

        #region ModuleRCS

        // The stock implementation is 100% broken, this is a complete replacement
        static void ModuleRCS_GetPotentialTorque_Override(ModuleRCS mrcs, out Vector3 pos, out Vector3 neg)
        {
            pos = neg = Vector3.zero;

            if (!mrcs.moduleIsEnabled
                || !mrcs.rcsEnabled
                || !mrcs.rcs_active
                || mrcs.IsAdjusterBreakingRCS()
                || mrcs.isJustForShow
                || mrcs.flameout
                || (mrcs.part.ShieldedFromAirstream && !mrcs.shieldedCanThrust)
                || (!mrcs.enablePitch && !mrcs.enableRoll && !mrcs.enableYaw))
            {
                return;
            }

            double power = GetMaxRCSPower(mrcs);
            if (power < 0.0001)
                return;

            ref TransformMatrix vesselLocalToWorld = ref TransformMatrix.LocalToWorldCurrent(mrcs.vessel.ReferenceTransform);
            Vector3d pitchDir = vesselLocalToWorld.Right;
            Vector3d rollDir = vesselLocalToWorld.Up;
            Vector3d yawDir = vesselLocalToWorld.Forward;

            ref TransformMatrix vesselWorldToLocal = ref TransformMatrix.WorldToLocalCurrent(mrcs.vessel.ReferenceTransform);

            double minRotActuation;
            bool checkActuation = RCSLimiter.moduleRCSExtensions != null;
            if (checkActuation && RCSLimiter.moduleRCSExtensions.TryGetValue(mrcs, out RCSLimiter.ModuleRCSExtension limits))
            {
                minRotActuation = Math.Max(Math.Cos(limits.minRotationAlignement * UtilMath.Deg2Rad), 0.0);
            }
            else
            {
                minRotActuation = 0.0;
                checkActuation = mrcs.fullThrust;
            }

            for (int i = mrcs.thrusterTransforms.Count; i-- > 0;)
            {
                Transform thruster = mrcs.thrusterTransforms[i];

                if (!thruster.gameObject.activeInHierarchy)
                    continue;

                Vector3d thrusterPosFromCoM = thruster.position - mrcs.vessel.CoMD;
                Vector3d thrusterDirFromCoM = thrusterPosFromCoM.normalized;
                Vector3d thrustDirection = mrcs.useZaxis ? thruster.forward : thruster.up;

                double thrusterPower = power;
                if (FlightInputHandler.fetch.precisionMode)
                {
                    if (mrcs.useLever)
                    {
                        double leverDistance = mrcs.GetLeverDistance(thruster, thrustDirection, mrcs.vessel.CoMD);
                        if (leverDistance > 1.0)
                        {
                            thrusterPower /= leverDistance;
                        }
                    }
                    else
                    {
                        thrusterPower *= mrcs.precisionFactor;
                    }
                }

                double thrustX = thrustDirection.x * thrusterPower;
                double thrustY = thrustDirection.y * thrusterPower;
                double thrustZ = thrustDirection.z * thrusterPower;

                Vector3d thrusterTorque = new Vector3d(
                    thrusterPosFromCoM.y * thrustZ - thrusterPosFromCoM.z * thrustY,
                    thrusterPosFromCoM.z * thrustX - thrusterPosFromCoM.x * thrustZ,
                    thrusterPosFromCoM.x * thrustY - thrusterPosFromCoM.y * thrustX);

                // transform in vessel control space
                vesselWorldToLocal.MutateMultiplyVector(ref thrusterTorque);

                if (mrcs.enablePitch && Math.Abs(thrusterTorque.x) > 0.0001)
                {
                    double actuation = GetRCSActuation(ref pitchDir, ref thrusterDirFromCoM, ref thrustDirection);
                    
                    if (checkActuation)
                    {
                        double actuationMagnitude = Math.Abs(actuation);
                        if (actuationMagnitude < minRotActuation)
                            actuation = 0.0;
                        else if (mrcs.fullThrust && actuationMagnitude > mrcs.fullThrustMin)
                            actuation = Math.Sign(actuation);
                    }

                    if (actuation != 0.0)
                    {
                        if (actuation > 00)
                            pos.x += (float)(thrusterTorque.x * actuation);
                        else
                            neg.x += (float)(thrusterTorque.x * actuation);
                    }
                }

                if (mrcs.enableRoll && Math.Abs(thrusterTorque.y) > 0.0001f)
                {
                    double actuation = GetRCSActuation(ref rollDir, ref thrusterDirFromCoM, ref thrustDirection);

                    if (checkActuation)
                    {
                        double actuationMagnitude = Math.Abs(actuation);
                        if (actuationMagnitude < minRotActuation)
                            actuation = 0.0;
                        else if (mrcs.fullThrust && actuationMagnitude > mrcs.fullThrustMin)
                            actuation = Math.Sign(actuation);
                    }

                    if (actuation != 0.0)
                    {
                        if (actuation > 0.0)
                            pos.y += (float)(thrusterTorque.y * actuation);
                        else
                            neg.y += (float)(thrusterTorque.y * actuation);
                    }
                }

                if (mrcs.enableYaw && Math.Abs(thrusterTorque.z) > 0.0001f)
                {
                    double actuation = GetRCSActuation(ref yawDir, ref thrusterDirFromCoM, ref thrustDirection);

                    if (checkActuation)
                    {
                        double actuationMagnitude = Math.Abs(actuation);
                        if (actuationMagnitude < minRotActuation)
                            actuation = 0.0;
                        else if (mrcs.fullThrust && actuationMagnitude > mrcs.fullThrustMin)
                            actuation = Math.Sign(actuation);
                    }

                    if (actuation != 0.0)
                    {
                        if (actuation > 0.0)
                            pos.z += (float)(thrusterTorque.z * actuation);
                        else
                            neg.z += (float)(thrusterTorque.z * actuation);
                    }
                }
            }

#if DEBUG
            TorqueUIModule ui = mrcs.part.FindModuleImplementing<TorqueUIModule>();
            if (ui != null)
            {
                ui.pos = pos;
                ui.neg = neg;
            }
#endif
        }

        private static double GetMaxRCSPower(ModuleRCS mrcs)
        {
            if (!mrcs.requiresFuel)
                return 1f;

            double flowMult = mrcs.flowMult;
            if (mrcs.useThrustCurve)
                flowMult *= mrcs.thrustCurveDisplay;

            return flowMult * mrcs.maxFuelFlow * mrcs.exhaustVel * mrcs.thrustPercentage * 0.01;
        }

        private static double GetRCSActuation(ref Vector3d controlDir, ref Vector3d thrusterDirFromCoM, ref Vector3d thrustDirection)
        {
            double xP, yP, zP;

            double dirSqr = controlDir.x * controlDir.x + controlDir.y * controlDir.y + controlDir.z * controlDir.z;
            if (dirSqr < 1.401298464324817E-45)
            {
                xP = thrusterDirFromCoM.x;
                yP = thrusterDirFromCoM.y;
                zP = thrusterDirFromCoM.z;
            }
            else
            {
                double dot = thrusterDirFromCoM.x * controlDir.x + thrusterDirFromCoM.y * controlDir.y + thrusterDirFromCoM.z * controlDir.z;
                xP = thrusterDirFromCoM.x - controlDir.x * dot / dirSqr;
                yP = thrusterDirFromCoM.y - controlDir.y * dot / dirSqr;
                zP = thrusterDirFromCoM.z - controlDir.z * dot / dirSqr;
            }

            double xC, yC, zC;
            xC = controlDir.y * zP - controlDir.z * yP;
            yC = controlDir.z * xP - controlDir.x * zP;
            zC = controlDir.x * yP - controlDir.y * xP;

            double rotMag = Math.Sqrt(xC * xC + yC * yC + zC * zC);
            if (rotMag > 0.0)
            {
                xC /= rotMag;
                yC /= rotMag;
                zC /= rotMag;
            }
            else
            {
                xC = 0.0;
                yC = 0.0;
                zC = 0.0;
            }

            return thrustDirection.x * xC + thrustDirection.y * yC + thrustDirection.z * zC;
        }

        #endregion

        #region ModuleControlSurface

        // The stock ModuleControlSurface.GetPotentialTorque() implementation has several issues and its results are overall very wrong :
        // - It doesn't take drag forces into account (only lift).
        // - It attempt to provide actuation torque (ie, the torque difference between pos/neg actuation and the neutral state) by substracting
        //   the neutral lift force vector to the actuated pos/neg force vectors. This is wrong and produce garbage results, it's the resulting
        //   torque from the neutral vector that should be substracted to the resulting torque from the pos/neg force vectors.
        // - It entirely fails to correct the raw torque results for the pitch/roll/yaw actuation inversion and clamping logic, resulting in
        //   wrongly negative values and random inversions of the neg / pos value.
        // - For reasons that escape my understanding entirely, it multiply the torque results by the vessel CoM to Part vector, resulting in 
        //   more non-sense sign inversions and a near squaring of the result magnitude.
        // - It generally doesn't handle correctly control surfaces in the deployed state.

        // This reimplementation fixes all the above issues.
        // It still has a few shortcomings. Notably, it partially reuse results from the previous FixedUpdate() and mix them with current values.
        // This mean the results are slightly wrong, but this saves quite a bit of extra processing in that already quite performance heavy method,
        // and the error magnitude shouldn't matter for potential applications of GetPotentialTorque().
        // It shall also be noted that the result magnitude is an approximation when the actuation is clamped by the module (ie, when the control
        // surface neutral state isn't aligned with the airflow), with the error being greater when the allowed actuation is lower.

        // Note that the results can still return negative components. The meaning of a negative value is that the actuation of that component will
        // induce a torque in the opposite direction. For example, a negative pos.x value mean that for a positive roll actuation (ctrlState.roll > 0),
        // the control surface will produce a torque incuding a negative roll, essentially reducing the total available torque in that direction.


        // stuff we don't have in the editor : nVel, Qlift, part.machNumber, Qdrag, baseLiftForce

        private class ModuleCtrlSrfExtension
        {
            private static Dictionary<ModuleControlSurface, ModuleCtrlSrfExtension> instances = new Dictionary<ModuleControlSurface, ModuleCtrlSrfExtension>();

            public static ModuleCtrlSrfExtension Get(ModuleControlSurface module)
            {
                if (instances.TryGetValue(module, out ModuleCtrlSrfExtension gimbalExt))
                    return gimbalExt;

                return new ModuleCtrlSrfExtension(module);
            }

            private ModuleControlSurface module;

            public Vector3 pos;
            public Vector3 neg;

            public Vector3 worldCoM;
            public Vector3 localCoM;
            public Vector3 nVel;
            public Vector3 neutralForce;
            public double QLift;
            public double QDrag;
            public float currentDeployAngle;
            public float machNumber;

            public float lastTime;
            public Vector3 lastBaseLiftForce;
            private bool lastPitch;
            private bool lastRoll;
            private bool lastYaw;
            private float QLiftThreshold;
            private float timeThreshold;

            private bool pawTorqueEnabled;
            private BaseField ignorePitchField;
            private BaseField ignoreRollField;
            private BaseField ignoreYawField;

            public ModuleCtrlSrfExtension(ModuleControlSurface module)
            {
                this.module = module;

                ignorePitchField = module.Fields[nameof(ModuleControlSurface.ignorePitch)];
                ignoreRollField = module.Fields[nameof(ModuleControlSurface.ignoreRoll)];
                ignoreYawField = module.Fields[nameof(ModuleControlSurface.ignoreYaw)];

                module.part.OnJustAboutToBeDestroyed += OnDestroy;
                instances.Add(module, this);

                QLiftThreshold = Random.Range(0.04f, 0.06f);
                timeThreshold = Random.Range(0.75f, 1.25f);
            }

            public void OnDestroy()
            {
                module.part.OnJustAboutToBeDestroyed -= OnDestroy;
                instances.Remove(module);
                module = null;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateCachedState(Vector3 worldCoM, Vector3 localCoM)
            {
                this.worldCoM = worldCoM;
                this.localCoM = localCoM;
                lastTime = Time.fixedTime;
                nVel = module.nVel;
                QLift = module.Qlift;
                QDrag = module.Qdrag;
                machNumber = (float)module.part.machNumber;

                if (module.deploy)
                {
                    currentDeployAngle = module.currentDeployAngle;
                    Vector3 rhsNeutral = Quaternion.AngleAxis(currentDeployAngle, module.baseTransform.rotation * Vector3.right) * module.baseTransform.forward;
                    float dotNeutral = Vector3.Dot(nVel, rhsNeutral);
                    float absDotNeutral = Mathf.Abs(dotNeutral);
                    neutralForce = module.GetLiftVector(rhsNeutral, dotNeutral, absDotNeutral, QLift, machNumber) * module.ctrlSurfaceArea;
                    neutralForce += GetDragForce(module, this, absDotNeutral);
                }
                else
                {
                    currentDeployAngle = 0f;
                    neutralForce = module.baseLiftForce * module.ctrlSurfaceArea;
                    neutralForce += GetDragForce(module, this, module.absDot);
                }

                lastBaseLiftForce = module.baseLiftForce;
                lastPitch = module.ignorePitch;
                lastRoll = module.ignoreRoll;
                lastYaw = module.ignoreYaw;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsCacheValid(Vector3 worldCoM, Vector3 localCoM)
            {
                if ((QLift > 0.0 && Math.Abs((module.Qlift / QLift) - 1.0) > QLiftThreshold)
                  || (this.localCoM - localCoM).sqrMagnitude > 0.1f * 0.1f
                  || (lastBaseLiftForce - module.baseLiftForce).sqrMagnitude > 0.1f * 0.1f
                  || Time.fixedTime > lastTime + timeThreshold
                  || currentDeployAngle != module.currentDeployAngle
                  || lastPitch != module.ignorePitch || lastRoll != module.ignoreRoll || lastYaw != module.ignoreYaw)
                {
                    UpdateCachedState(worldCoM, localCoM);
                    return false;
                }

                return true;
            }

            public void UpdateEditorState(Transform referenceTransform, EditorPhysics editorPhysics)
            {
                if (editorPhysics.atmDensity == 0.0)
                    return;

                worldCoM = editorPhysics.CoM;

                nVel = referenceTransform.up; // velocity normalized

                currentDeployAngle = module.deploy ? module.currentDeployAngle : 0f;

                double dynamicPressureKpa = editorPhysics.atmDensity;
                double speed = 100.0; // just assume 100m/s for now

                dynamicPressureKpa *= 0.0005 * speed * speed;
                QLift = dynamicPressureKpa * 1000.0;
                QDrag = dynamicPressureKpa * 1000.0;

                double speedOfSound = editorPhysics.body.GetSpeedOfSound(editorPhysics.atmStaticPressureKpa, editorPhysics.atmDensity);
                if (speedOfSound > 0.0)
                    machNumber = (float)(speed / speedOfSound);
                else
                    machNumber = 0f;

                Vector3 rhsNeutral = Quaternion.AngleAxis(currentDeployAngle, module.baseTransform.rotation * Vector3.right) * module.baseTransform.forward;
                float dotNeutral = Vector3.Dot(nVel, rhsNeutral);
                float absDotNeutral = Mathf.Abs(dotNeutral);
                neutralForce = GetLiftForce(module, this, rhsNeutral, dotNeutral, absDotNeutral) * module.ctrlSurfaceArea;
                neutralForce += GetDragForce(module, this, absDotNeutral);
            }

            public static void UpdateInstances()
            {
                foreach (ModuleCtrlSrfExtension moduleExtension in instances.Values)
                {
                    try
                    {
                        if (moduleExtension.module.isActiveAndEnabled && !moduleExtension.module.displaceVelocity)
                            moduleExtension.UpdatePAW();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            private void UpdatePAW()
            {
                if (!ActuationToggleDisplayed(module))
                {
                    if (pawTorqueEnabled)
                        DisablePAWTorque();

                    return;
                }

                ModuleControlSurface_GetPotentialTorque_Override(module, out Vector3 pos, out Vector3 neg);

                if (pos == Vector3.zero && neg == Vector3.zero)
                {
                    if (pawTorqueEnabled)
                        DisablePAWTorque();

                    return;
                }

                pawTorqueEnabled = true;

                if (!module.ignorePitch)
                    SetToggleGuiName(ignorePitchField, $"{autoLOC_6001330_Pitch}: {Math.Round(pos.x, 3):G3} / {-Math.Round(neg.x, 3):G3} kNm");
                else
                    SetToggleGuiName(ignorePitchField, autoLOC_6001330_Pitch);

                if (!module.ignoreRoll)
                    SetToggleGuiName(ignoreRollField, $"{autoLOC_6001332_Roll}: {Math.Round(pos.y, 3):G3} / {-Math.Round(neg.y, 3):G3} kNm");
                else
                    SetToggleGuiName(ignoreRollField, autoLOC_6001332_Roll);

                if (!module.ignoreYaw)
                    SetToggleGuiName(ignoreYawField, $"{autoLOC_6001331_Yaw}: {Math.Round(pos.z, 3):G3} / {-Math.Round(neg.z, 3):G3} kNm");
                else
                    SetToggleGuiName(ignoreYawField, autoLOC_6001331_Yaw);
            }

            private void DisablePAWTorque()
            {
                pawTorqueEnabled = false;
                SetToggleGuiName(ignorePitchField, autoLOC_6001330_Pitch);
                SetToggleGuiName(ignoreRollField, autoLOC_6001332_Roll);
                SetToggleGuiName(ignoreYawField, autoLOC_6001331_Yaw);
            }

            static bool ActuationToggleDisplayed(ModuleControlSurface module)
            {
                if (module.part.PartActionWindow.IsNullOrDestroyed() || !module.part.PartActionWindow.isActiveAndEnabled)
                    return false;

                return true;
            }

            private static void SetToggleGuiName(BaseField baseField, string guiName)
            {
                baseField.guiName = guiName;

                UIPartActionToggle toggle;

                if (baseField.uiControlEditor.partActionItem.IsNotNullOrDestroyed())
                    toggle = (UIPartActionToggle)baseField.uiControlEditor.partActionItem;
                else if (baseField.uiControlFlight.partActionItem.IsNotNullOrDestroyed())
                    toggle = (UIPartActionToggle)baseField.uiControlFlight.partActionItem;
                else
                    return;

                toggle.fieldName.text = guiName;
                ((RectTransform)toggle.fieldName.transform).sizeDelta = new Vector2(150f, toggle.fieldName.rectTransform.sizeDelta.y);
            }
        }

        static void ModuleControlSurface_OnStart_Postfix(ModuleControlSurface __instance)
        {
            ModuleCtrlSrfExtension.Get(__instance);
        }

        static void ModuleControlSurface_GetPotentialTorque_Override(ModuleControlSurface mcs, out Vector3 pos, out Vector3 neg)
        {
            pos = Vector3.zero;
            neg = Vector3.zero;

            bool isEditor = HighLogic.LoadedScene == GameScenes.EDITOR;

            if (isEditor)
            {
                if (mcs.ignorePitch && mcs.ignoreYaw && mcs.ignoreRoll)
                    return;
            }
            else
            {
                if (mcs.Qlift < 1.0 || (mcs.ignorePitch && mcs.ignoreYaw && mcs.ignoreRoll))
                    return;
            }

            if (mcs.displaceVelocity)
            {
                if (isEditor)
                    return;

                // This case is for handling "propeller blade" control surfaces. Those have a completely different behavior and
                // actuation scheme (and why this wasn't implemented as a separate module is beyond my understanding).
                // This is the stock GetPotentialTorque() implementation for them, I've no idea how correct it is and just don't
                // have the motivation to investigate.

                Vector3 potentialForcePos = mcs.GetPotentialLift(true);
                Vector3 potentialForceNeg = mcs.GetPotentialLift(false);
                float magnitude = mcs.vesselBladeLiftReference.magnitude;
                pos = Vector3.Dot(potentialForcePos, mcs.vesselBladeLiftReference) * mcs.potentialBladeControlTorque / magnitude;
                neg = Vector3.Dot(potentialForceNeg, mcs.vesselBladeLiftReference) * mcs.potentialBladeControlTorque / magnitude;
            }
            else
            {
                // The stock method doesn't handle correctly the deployed state :
                // - It always apply `currentDeployAngle` in the AngleAxis() call, but that field is updated only if `mcs.deploy == true`, and
                //   it isn't reverted to 0 if deploy changes from true to false, resulting in the deployed angle still being applied after un-deploying.
                // - It always substract `baseLiftForce` which is always the non-deployed lift vector, resulting in the positive deflection being twice
                //   what it should be and the negative deflection being always zero.

                ModuleCtrlSrfExtension moduleExt;
                Transform vesselReferenceTransform;

                if (isEditor)
                {
                    if (!EditorPhysics.TryGetAndUpdate(out EditorPhysics editorPhysics) || editorPhysics.atmDensity == 0.0)
                        return;

                    vesselReferenceTransform = editorPhysics.referenceTransform;
                    moduleExt = ModuleCtrlSrfExtension.Get(mcs);
                    moduleExt.UpdateEditorState(vesselReferenceTransform, editorPhysics);
                }
                else
                {
                    vesselReferenceTransform = mcs.vessel.ReferenceTransform;

                    moduleExt = ModuleCtrlSrfExtension.Get(mcs);
                    if (moduleExt.IsCacheValid(mcs.vessel.CurrentCoM, vesselReferenceTransform.InverseTransformPoint(mcs.vessel.CurrentCoM)))
                    {
                        pos = moduleExt.pos;
                        neg = moduleExt.neg;
                        return;
                    }
                }

                Vector3 potentialForcePos = GetPotentialLiftAndDrag(mcs, moduleExt, moduleExt.currentDeployAngle, true);
                Vector3 potentialForceNeg = GetPotentialLiftAndDrag(mcs, moduleExt, moduleExt.currentDeployAngle, false);

                Vector3 partPosition = mcs.part.Rigidbody.worldCenterOfMass - moduleExt.worldCoM;

                Vector3 posTorque = vesselReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, potentialForcePos));
                Vector3 negTorque = vesselReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, potentialForceNeg));

                Vector3 neutralTorque = vesselReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, moduleExt.neutralForce));

                posTorque -= neutralTorque;
                negTorque -= neutralTorque;

                // At this point, we have raw torque results for two given actuations. However, GetPotentialTorque() is supposed to
                // represent the torque produced by pitch/roll/yaw requests. We need to determine which actuation is applied for a pos=(1,1,1)
                // and neg=(-1,-1,-1) ctrlState, then swap the raw torque components accordingly. Said otherwise, to take an example,
                // we need to answer : does a positive pitch request results in a positive or negative actuation ?
                // Additionally, ModuleControlSurface will clamp actuation magnitude depending on the surface orientation, and apply an
                // additional (weird) clamping for the deployed state.

                // The following code is essentially derived from ModuleControlSurface.FixedCtrlSurfaceUpdate(), which is the method responsible
                // for updating the control surface angle according to the vessel.ctrlState pitch/roll/yaw request.

                float deployAction;
                if (mcs.deploy)
                {
                    deployAction = mcs.usesMirrorDeploy
                        ? ((mcs.deployInvert ? (-1f) : 1f) * (mcs.partDeployInvert ? (-1f) : 1f) * (mcs.mirrorDeploy ? (-1f) : 1f))
                        : ((mcs.deployInvert ? (-1f) : 1f) * Mathf.Sign((Quaternion.Inverse(vesselReferenceTransform.rotation) * (mcs.baseTransform.position - moduleExt.worldCoM)).x));

                    deployAction *= -1f;
                }
                else
                {
                    deployAction = 0f;
                }

                Vector3 comRelPos = mcs.baseTransform.InverseTransformPoint(moduleExt.worldCoM);

#if DEBUG
                Vector3 posAction = Vector3.zero;
                Vector3 negAction = Vector3.zero;
#endif

                if (!mcs.ignorePitch)
                {
                    Vector3 pitchVector = vesselReferenceTransform.rotation * new Vector3(1f, 0f, 0f);
                    float pitchActionPos = Vector3.Dot(pitchVector, mcs.baseTransform.rotation * Vector3.right);
                    if (comRelPos.y < 0f)
                        pitchActionPos = -pitchActionPos;

                    float pitchActionNeg = -pitchActionPos;

                    if (mcs.deploy)
                    {
                        pitchActionPos = Mathf.Clamp(pitchActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        pitchActionNeg = Mathf.Clamp(pitchActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    // I hope I got this right. TBH, this was mostly a trial and error job.
                    // - the control surface actuation direction depends on sign of the action
                    // - then we clamp and inverse the raw torque by the action
                    //   note that this direct scaling is a rough approximation, as the torque output vs actuation function isn't linear,
                    //   but the whole thing is computationally intensive (and complex) enough already...
                    if (pitchActionPos > 0f)
                    {
                        pos.x = negTorque.x * pitchActionPos;
                        neg.x = posTorque.x * pitchActionNeg;
                    }
                    else
                    {
                        pos.x = posTorque.x * pitchActionNeg;
                        neg.x = negTorque.x * pitchActionPos;
                    }
#if DEBUG
                    posAction.x = pitchActionPos;
                    negAction.x = pitchActionNeg;
#endif
                }

                if (!mcs.ignoreYaw)
                {
                    Vector3 yawVector = vesselReferenceTransform.rotation * new Vector3(0f, 0f, 1f);
                    float yawActionPos = Vector3.Dot(yawVector, mcs.baseTransform.rotation * Vector3.right);
                    if (comRelPos.y < 0f)
                        yawActionPos = -yawActionPos;

                    float yawActionNeg = -yawActionPos;

                    if (mcs.deploy)
                    {
                        yawActionPos = Mathf.Clamp(yawActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        yawActionNeg = Mathf.Clamp(yawActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    if (yawActionPos > 0f)
                    {
                        pos.z = negTorque.z * yawActionPos;
                        neg.z = posTorque.z * yawActionNeg;
                    }
                    else
                    {
                        pos.z = posTorque.z * yawActionNeg;
                        neg.z = negTorque.z * yawActionPos;
                    }
#if DEBUG
                    posAction.z = yawActionPos;
                    negAction.z = yawActionNeg;
#endif
                }

                if (!mcs.ignoreRoll)
                {
                    // optimization note : we could get rollAction by doing `rollAction = mcs.roll / ctrlStateRoll` where
                    // ctrlStateRoll is the `vessel.ctrlState.roll` value from the last fixedUpdate(). 
                    // But implementing that would be a mess, and the value would be slightly wrong due to being a frame outdated
                    // (altough this won't matter much, the overall GetPotentialTorque() implementation already rely on a bunch of
                    // one-frame outdated values for performance optimization reasons)

                    Vector3 rhs = new Vector3(comRelPos.x, 0f, comRelPos.z);

                    float rollActionPos = Vector3.Dot(Vector3.right, rhs)
                                          * (1f - (Mathf.Abs(Vector3.Dot(rhs.normalized, Quaternion.Inverse(mcs.baseTransform.rotation) * vesselReferenceTransform.up)) * 0.5f + 0.5f))
                                          * Mathf.Sign(Vector3.Dot(mcs.baseTransform.up, vesselReferenceTransform.up))
                                          * Mathf.Sign(mcs.ctrlSurfaceRange)
                                          * -1f;

                    rollActionPos = Mathf.Clamp(rollActionPos, -1f, 1f);

                    float rollActionNeg = -rollActionPos;

                    if (mcs.deploy)
                    {
                        rollActionPos = Mathf.Clamp(rollActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        rollActionNeg = Mathf.Clamp(rollActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    if (rollActionPos > 0f)
                    {
                        pos.y = negTorque.y * rollActionPos;
                        neg.y = posTorque.y * rollActionNeg;
                    }
                    else
                    {
                        pos.y = posTorque.y * rollActionNeg;
                        neg.y = negTorque.y * rollActionPos;
                    }
#if DEBUG
                    posAction.y = rollActionPos;
                    negAction.y = rollActionNeg;
#endif
                }

                moduleExt.pos = pos;
                moduleExt.neg = neg;

#if DEBUG
                TorqueUIModule ui = mcs.part.FindModuleImplementing<TorqueUIModule>();
                if (ui != null)
                {
                    ui.pos = pos;
                    ui.neg = neg;

                    ui.Fields["spos"].guiActive = true;
                    ui.spos = posTorque;

                    ui.Fields["sneg"].guiActive = true;
                    ui.sneg = negTorque;

                    ui.Fields["posAction"].guiActive = true;
                    ui.posAction = posAction;

                    ui.Fields["negAction"].guiActive = true;
                    ui.negAction = negAction;
                }
#endif
            }
        }

        private static Vector3 GetPotentialLiftAndDrag(ModuleControlSurface mcs, ModuleCtrlSrfExtension moduleExt, float deployAngle, bool positiveDeflection)
        {
            float deflectionDir = positiveDeflection ? 1f : -1f;
            float angle = deployAngle + (deflectionDir * mcs.ctrlSurfaceRange * mcs.authorityLimiter * 0.01f);
            Vector3 rhs = Quaternion.AngleAxis(angle, mcs.baseTransform.rotation * Vector3.right) * mcs.baseTransform.forward;
            float dot = Vector3.Dot(moduleExt.nVel, rhs);
            float absDot = Mathf.Abs(dot);
            Vector3 result = GetLiftForce(mcs, moduleExt, rhs, dot, absDot) * mcs.ctrlSurfaceArea;
            result += GetDragForce(mcs, moduleExt, absDot);
            return result;
        }

        private static Vector3 GetLiftForce(ModuleControlSurface mcs, ModuleCtrlSrfExtension moduleExt, Vector3 liftVector, float liftDot, float absDot)
        {
            if (mcs.nodeEnabled && mcs.attachNode.attachedPart != null)
            {
                return Vector3.zero;
            }
            float liftScalar = Mathf.Sign(liftDot) * mcs.liftCurve.Evaluate(absDot) * mcs.liftMachCurve.Evaluate(moduleExt.machNumber);
            liftScalar *= mcs.deflectionLiftCoeff;
            if (liftScalar != 0f && !float.IsNaN(liftScalar))
            {
                liftScalar = (float)(moduleExt.QLift * PhysicsGlobals.LiftMultiplier * liftScalar);
                if (mcs.perpendicularOnly)
                {
                    Vector3 vector = -liftVector * liftScalar;
                    vector = Vector3.ProjectOnPlane(vector, -moduleExt.nVel);
                    return vector;
                }
                return -liftVector * liftScalar;
            }
            return Vector3.zero;
        }

        private static Vector3 GetDragForce(ModuleControlSurface mcs, ModuleCtrlSrfExtension moduleExt, float absDot)
        {
            if (!mcs.useInternalDragModel || (mcs.nodeEnabled && mcs.attachNode.attachedPart.IsNotNullOrDestroyed()))
                return Vector3.zero;

            float dragScalar = mcs.dragCurve.Evaluate(absDot) * mcs.dragMachCurve.Evaluate(moduleExt.machNumber);
            dragScalar *= mcs.deflectionLiftCoeff;
            if (dragScalar != 0f && !float.IsNaN(dragScalar))
            {
                dragScalar = (float)moduleExt.QDrag * dragScalar * PhysicsGlobals.LiftDragMultiplier;
                return -moduleExt.nVel * dragScalar * mcs.ctrlSurfaceArea;
            }

            return Vector3.zero;
        }

        #endregion

        #region ModuleGimbal

        private class ModuleGimbalExtension
        {
            private static Dictionary<ModuleGimbal, ModuleGimbalExtension> instances = new Dictionary<ModuleGimbal, ModuleGimbalExtension>();

            public static ModuleGimbalExtension Get(ModuleGimbal module)
            {
                if (instances.TryGetValue(module, out ModuleGimbalExtension gimbalExt))
                    return gimbalExt;

                return new ModuleGimbalExtension(module);
            }

            private ModuleGimbal module;

            public Vector3 pos;
            public Vector3 neg;

            private Vector3 lastLocalCoM;
            private float lastThrustForce;
            private float lastTime;
            private float lastGimbalLimiter;
            private bool lastPitch;
            private bool lastRoll;
            private bool lastYaw;
            private float timeThreshold;

            private bool pawTorqueEnabled;
            private BaseField enablePitchField;
            private BaseField enableRollField;
            private BaseField enableYawField;

            public ModuleGimbalExtension(ModuleGimbal module)
            {
                this.module = module;

                enablePitchField = module.Fields[nameof(ModuleGimbal.enablePitch)];
                enableRollField = module.Fields[nameof(ModuleGimbal.enableRoll)];
                enableYawField = module.Fields[nameof(ModuleGimbal.enableYaw)];

                module.part.OnJustAboutToBeDestroyed += OnDestroy;
                instances.Add(module, this);

                timeThreshold = Random.Range(0.75f, 1.25f);
            }

            public void OnDestroy()
            {
                module.part.OnJustAboutToBeDestroyed -= OnDestroy;
                instances.Remove(module);
                module = null;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateLastState(Vector3 localCoM, float thrustForce)
            {
                lastLocalCoM = localCoM;
                lastThrustForce = thrustForce;
                lastTime = Time.fixedTime;
                lastGimbalLimiter = module.gimbalLimiter;
                lastPitch = module.enablePitch;
                lastRoll = module.enableRoll;
                lastYaw = module.enableYaw;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsCacheValid(Vector3 localCoM, float thrustForce)
            {
                if (Math.Abs(lastThrustForce - thrustForce) > 1.0
                    || (lastLocalCoM - localCoM).sqrMagnitude > 0.1f * 0.1f
                    || Time.fixedTime > lastTime + timeThreshold
                    || lastGimbalLimiter != module.gimbalLimiter
                    || lastPitch != module.enablePitch || lastRoll != module.enableRoll || lastYaw != module.enableYaw)
                {
                    UpdateLastState(localCoM, thrustForce);
                    return false;
                }

                return true;
            }

            public static void UpdateInstances()
            {
                foreach (ModuleGimbalExtension moduleExtension in instances.Values)
                {
                    try
                    {
                        if (moduleExtension.module.isActiveAndEnabled)
                            moduleExtension.UpdatePAW();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            private void UpdatePAW()
            {
                if (!ActuationToggleDisplayed(module))
                {
                    if (pawTorqueEnabled)
                        DisablePAWTorque();

                    return;
                }

                ModuleGimbal_GetPotentialTorque_Override(module, out Vector3 pos, out Vector3 neg);

                if (pos == Vector3.zero && neg == Vector3.zero)
                {
                    if (pawTorqueEnabled)
                        DisablePAWTorque();

                    return;
                }

                pawTorqueEnabled = true;

                if (module.enablePitch)
                    SetToggleGuiName(enablePitchField, $"{autoLOC_6001330_Pitch}: {Math.Round(pos.x, 3):G3} / {-Math.Round(neg.x, 3):G3} kNm");
                else
                    SetToggleGuiName(enablePitchField, autoLOC_6001330_Pitch);

                if (module.enableRoll)
                    SetToggleGuiName(enableRollField, $"{autoLOC_6001332_Roll}: {Math.Round(pos.y, 3):G3} / {-Math.Round(neg.y, 3):G3} kNm");
                else
                    SetToggleGuiName(enableRollField, autoLOC_6001332_Roll);

                if (module.enableYaw)
                    SetToggleGuiName(enableYawField, $"{autoLOC_6001331_Yaw}: {Math.Round(pos.z, 3):G3} / {-Math.Round(neg.z, 3):G3} kNm");
                else
                    SetToggleGuiName(enableYawField, autoLOC_6001331_Yaw);
            }

            private void DisablePAWTorque()
            {
                pawTorqueEnabled = false;
                SetToggleGuiName(enablePitchField, autoLOC_6001330_Pitch);
                SetToggleGuiName(enableRollField, autoLOC_6001332_Roll);
                SetToggleGuiName(enableYawField, autoLOC_6001331_Yaw);
            }

            static bool ActuationToggleDisplayed(ModuleGimbal module)
            {
                if (!module.showToggles || !module.currentShowToggles)
                    return false;

                if (module.part.PartActionWindow.IsNullOrDestroyed() || !module.part.PartActionWindow.isActiveAndEnabled)
                    return false;

                return true;
            }

            private static void SetToggleGuiName(BaseField baseField, string guiName)
            {
                baseField.guiName = guiName;

                UIPartActionToggle toggle;

                if (baseField.uiControlEditor.partActionItem.IsNotNullOrDestroyed())
                    toggle = (UIPartActionToggle)baseField.uiControlEditor.partActionItem;
                else if (baseField.uiControlFlight.partActionItem.IsNotNullOrDestroyed())
                    toggle = (UIPartActionToggle)baseField.uiControlFlight.partActionItem;
                else
                    return;

                toggle.fieldName.text = guiName;
                ((RectTransform)toggle.fieldName.transform).sizeDelta = new Vector2(150f, toggle.fieldName.rectTransform.sizeDelta.y);
            }
        }

        static void ModuleGimbal_OnStart_Postfix(ModuleGimbal __instance)
        {
            ModuleGimbalExtension.Get(__instance);
        }

        static void ModuleGimbal_GetPotentialTorque_Override(ModuleGimbal mg, out Vector3 pos, out Vector3 neg)
        {
            pos = Vector3.zero;
            neg = Vector3.zero;

            bool isEditor = HighLogic.LoadedScene == GameScenes.EDITOR;

            if (isEditor)
            {
                if (mg.gimbalLock
                    || !mg.moduleIsEnabled
                    || (!mg.enablePitch && !mg.enableRoll && !mg.enableYaw))
                {
                    return;
                }
            }
            else
            {
                if (mg.gimbalLock
                    || !mg.gimbalActive
                    || !mg.moduleIsEnabled
                    || (!mg.enablePitch && !mg.enableRoll && !mg.enableYaw))
                {
                    return;
                }
            }

            // ensure we don't create a cache entry when the part is destroyed
            if (mg.part.State == PartStates.DEAD)
                return;

            if (mg.engineMultsList == null)
                mg.CreateEngineList();

            ModuleGimbalExtension gimbalCache;
            Vector3 worldCoM;
            Vector3 localCoM;
            Transform vesselReferenceTransform;
            int transformIndex;
            EditorPhysics editorPhysics;

            if (isEditor)
            {
                if (!EditorPhysics.TryGetAndUpdate(out editorPhysics))
                    return;

                worldCoM = editorPhysics.CoM;
                vesselReferenceTransform = editorPhysics.referenceTransform;
                localCoM = vesselReferenceTransform.InverseTransformPoint(worldCoM);
                gimbalCache = ModuleGimbalExtension.Get(mg);
            }
            else
            {
                editorPhysics = null;

                float totalThrust = 0f;
                transformIndex = mg.gimbalTransforms.Count;
                while (transformIndex-- > 0)
                {
                    int engineIndex = mg.engineMultsList[transformIndex].Count;
                    while (engineIndex-- > 0)
                        totalThrust += mg.engineMultsList[transformIndex][engineIndex].Key.finalThrust;
                }

                if (totalThrust == 0f)
                    return;

                worldCoM = mg.vessel.CurrentCoM;
                vesselReferenceTransform = mg.vessel.ReferenceTransform;
                localCoM = vesselReferenceTransform.InverseTransformPoint(worldCoM);

                gimbalCache = ModuleGimbalExtension.Get(mg);
                if (gimbalCache.IsCacheValid(localCoM, totalThrust))
                {
                    pos = gimbalCache.pos;
                    neg = gimbalCache.neg;
                    return;
                }
            }

            transformIndex = mg.gimbalTransforms.Count;
            while (transformIndex-- > 0)
            {
                List<KeyValuePair<ModuleEngines, float>> engines = mg.engineMultsList[transformIndex];

                Transform gimbalTransform = mg.gimbalTransforms[transformIndex];

                // this is the neutral gimbalTransform.localRotation
                Quaternion neutralLocalRot = mg.initRots[transformIndex];
                Quaternion neutralWorldRot = gimbalTransform.parent.rotation * neutralLocalRot;
                // get the rotation between the current gimbal rotation and the neutral rotation
                Quaternion gimbalWorldRotToNeutral = neutralWorldRot * Quaternion.Inverse(gimbalTransform.rotation);

                Vector3 neutralTorque = Vector3.zero;
                Vector3 pitchPosTorque = Vector3.zero;
                Vector3 pitchNegTorque = Vector3.zero;
                Vector3 rollPosTorque = Vector3.zero;
                Vector3 rollNegTorque = Vector3.zero;
                Vector3 yawPosTorque = Vector3.zero;
                Vector3 yawNegTorque = Vector3.zero;

                Vector3 controlPoint = vesselReferenceTransform.InverseTransformPoint(gimbalTransform.position);

                bool inversedControl = localCoM.y < controlPoint.y;

                Quaternion pitchPosActuation;
                Quaternion pitchNegActuation;
                if (mg.enablePitch)
                {
                    pitchPosActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.right, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    pitchNegActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.left, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    pitchPosActuation = Quaternion.identity;
                    pitchNegActuation = Quaternion.identity;
                }

                Quaternion rollPosActuation;
                Quaternion rollNegActuation;
                if (mg.enableRoll)
                {
                    rollPosActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.up, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    rollNegActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.down, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    rollPosActuation = Quaternion.identity;
                    rollNegActuation = Quaternion.identity;
                }

                Quaternion yawPosActuation;
                Quaternion yawNegActuation;
                if (mg.enableYaw)
                {
                    yawPosActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.forward, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    yawNegActuation = GetGimbalWorldRotation(mg, vesselReferenceTransform, gimbalTransform, Vector3.back, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    yawPosActuation = Quaternion.identity;
                    yawNegActuation = Quaternion.identity;
                }

                int engineIndex = engines.Count;
                while (engineIndex-- > 0)
                {
                    KeyValuePair<ModuleEngines, float> engineThrustMultiplier = engines[engineIndex];

                    ModuleEngines engine = engineThrustMultiplier.Key;
                    float thrustMultiplier = engineThrustMultiplier.Value;

                    float thrustMagnitude;
                    if (isEditor)
                    {
                        if (editorPhysics.atmStaticPressure == 0f)
                            thrustMagnitude = engine.MaxThrustOutputVac(true);
                        else
                            thrustMagnitude = engine.MaxThrustOutputAtm(true, true, (float)editorPhysics.atmStaticPressure, editorPhysics.atmTemperature, editorPhysics.atmDensity);
                    }
                    else
                    {
                        thrustMagnitude = engine.finalThrust;
                    }

                    thrustMagnitude *= thrustMultiplier;

                    if (thrustMagnitude <= 0f)
                        continue;

                    int thrustTransformIndex = engine.thrustTransforms.Count;
                    while (thrustTransformIndex-- > 0)
                    {
                        Transform thrustTransform = engine.thrustTransforms[thrustTransformIndex];

                        // To get the "neutral" transform position, we need to walk back the transform hierarchy to correct for the current gimbal
                        // rotation induced thrustTransform position offset. It's not critical to do it (see below as for why), but it would be weird
                        // to have the end results varying slightly depending on the current actuation.
                        // But note that when getting the actuated forces, we don't use the modified thrustTransform position. In most cases, the  
                        // actuation induced position shift of the thrustTransform won't matter much, since the gimbal pivot - thrustTransform distance
                        // is usally tiny compared to the CoM-thrustTransform distance.
                        Vector3 thrustTransformPosition = gimbalTransform.position + (gimbalWorldRotToNeutral * (thrustTransform.position - gimbalTransform.position));
                        Vector3 trustPosFromCoM = thrustTransformPosition - worldCoM;

                        // get the neutral thrust force by removing the thrustTransform current actuation induced rotation 
                        Vector3 neutralThrustForce = gimbalWorldRotToNeutral * (thrustTransform.forward * thrustMagnitude);

                        // get the "natural" torque induced by the engine thrust, in world space
                        neutralTorque += Vector3.Cross(trustPosFromCoM, neutralThrustForce);

                        if (mg.enablePitch)
                        {
                            pitchPosTorque += Vector3.Cross(trustPosFromCoM, pitchPosActuation * neutralThrustForce);
                            pitchNegTorque += Vector3.Cross(trustPosFromCoM, pitchNegActuation * neutralThrustForce);
                        }

                        if (mg.enableRoll)
                        {
                            rollPosTorque += Vector3.Cross(trustPosFromCoM, rollPosActuation * neutralThrustForce);
                            rollNegTorque += Vector3.Cross(trustPosFromCoM, rollNegActuation * neutralThrustForce);
                        }

                        if (mg.enableYaw)
                        {
                            yawPosTorque += Vector3.Cross(trustPosFromCoM, yawPosActuation * neutralThrustForce);
                            yawNegTorque += Vector3.Cross(trustPosFromCoM, yawNegActuation * neutralThrustForce);
                        }
                    }
                }

                neutralTorque = vesselReferenceTransform.InverseTransformDirection(neutralTorque);

                if (mg.enablePitch)
                {
                    pitchPosTorque = vesselReferenceTransform.InverseTransformDirection(pitchPosTorque);
                    pitchNegTorque = vesselReferenceTransform.InverseTransformDirection(pitchNegTorque);
                    pos.x += pitchPosTorque.x - neutralTorque.x;
                    neg.x -= pitchNegTorque.x - neutralTorque.x;
                }

                if (mg.enableRoll)
                {
                    rollPosTorque = vesselReferenceTransform.InverseTransformDirection(rollPosTorque);
                    rollNegTorque = vesselReferenceTransform.InverseTransformDirection(rollNegTorque);
                    pos.y += rollPosTorque.y - neutralTorque.y;
                    neg.y -= rollNegTorque.y - neutralTorque.y;
                }

                if (mg.enableYaw)
                {
                    yawPosTorque = vesselReferenceTransform.InverseTransformDirection(yawPosTorque);
                    yawNegTorque = vesselReferenceTransform.InverseTransformDirection(yawNegTorque);
                    pos.z += yawPosTorque.z - neutralTorque.z;
                    neg.z -= yawNegTorque.z - neutralTorque.z;
                }
            }

            gimbalCache.pos = pos;
            gimbalCache.neg = neg;

#if DEBUG
            TorqueUIModule ui = mg.part.FindModuleImplementing<TorqueUIModule>();
            if (ui != null)
            {
                ui.pos = pos;
                ui.neg = neg;
            }
#endif
        }

        static Quaternion GetGimbalWorldRotation(ModuleGimbal mg, Transform referenceTransform, Transform gimbalTransform, Vector3 ctrlState, Vector3 controlPoint, bool inversedControl, Quaternion neutralLocalRot, Quaternion neutralWorldRot)
        {
            if (inversedControl)
            {
                ctrlState.x *= -1f;
                ctrlState.z *= -1f;
            }

            if (ctrlState.y != 0f && mg.enableRoll)
            {
                if (controlPoint.x > mg.minRollOffset)
                    ctrlState.x += ctrlState.y;
                else if (controlPoint.x < -mg.minRollOffset)
                    ctrlState.x -= ctrlState.y;

                if (controlPoint.z > mg.minRollOffset)
                    ctrlState.z += ctrlState.y;
                else if (controlPoint.z < -mg.minRollOffset)
                    ctrlState.z -= ctrlState.y;
            }

            // Stock does gimbalTransform.InverseTransformDirection(), resulting in the available torque varying with the current gimbal actuation...
            // To work around that, we call InverseTransformDirection() on the parent, then apply the neutral rotation.
            Vector3 localActuation =
                Quaternion.Inverse(neutralLocalRot)
                * gimbalTransform.parent.InverseTransformDirection(referenceTransform.TransformDirection(ctrlState));

            // get actuation angles
            localActuation.x = Mathf.Clamp(localActuation.x, -1f, 1f) * ((localActuation.x > 0f) ? mg.gimbalRangeXP : mg.gimbalRangeXN) * mg.gimbalLimiter * 0.01f;
            localActuation.y = Mathf.Clamp(localActuation.y, -1f, 1f) * ((localActuation.y > 0f) ? mg.gimbalRangeYP : mg.gimbalRangeYN) * mg.gimbalLimiter * 0.01f;

            // get local rotation
            Quaternion gimbalRotation =
                neutralLocalRot
                * Quaternion.AngleAxis(localActuation.x, mg.xMult * Vector3.right)
                * Quaternion.AngleAxis(localActuation.y, mg.yMult * (mg.flipYZ ? Vector3.forward : Vector3.up));

            // transform in world space
            gimbalRotation = (gimbalTransform.parent.rotation * gimbalRotation) * Quaternion.Inverse(neutralWorldRot);

            return gimbalRotation;
        }

        #endregion
    }

#if DEBUG
    public class TorqueUIModule : PartModule
    {
        [KSPField(guiActive = true, guiFormat = "F1")]
        public Vector3 pos;
        [KSPField(guiActive = true, guiFormat = "F1")]
        public Vector3 neg;

        // control surface debug stuff
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 spos;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 sneg;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 posAction;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 negAction;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 actionV;

        // gimbal debug stuff
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 gimbalNeutralTorque;
    }
#endif
}