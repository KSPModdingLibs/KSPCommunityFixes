using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.QoL
{
    internal class BetterSAS : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, 
                AccessTools.Constructor(typeof(VesselAutopilot), new Type[] { typeof(Vessel) }), 
                nameof(VesselAutopilot_Ctor_Override));

            AddPatch(PatchType.Override, typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.GetTotalVesselTorque));
            AddPatch(PatchType.Override, typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.CheckCoasting));
            AddPatch(PatchType.Override, typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.UpdateMaximumAcceleration));
        }

        static void VesselAutopilot_Ctor_Override(VesselAutopilot vesselAutopilot, Vessel vessel)
        {
            vesselAutopilot.vessel = vessel;
            vesselAutopilot.sas = new KSPCFVesselSAS(vessel);
        }

        static Vector3 VesselSAS_GetTotalVesselTorque_Override(KSPCFVesselSAS sas, Vessel v)
        {
            sas.posTorque.MutateZero();
            sas.negTorque.MutateZero();

            for (int partIdx = v.parts.Count; partIdx-- > 0;)
            {
                List<PartModule> pmList = v.parts[partIdx].modules.modules;
                for (int moduleIdx = pmList.Count; moduleIdx-- > 0;)
                {
                    PartModule pm = pmList[moduleIdx];
                    if (!pm.IsDestroyed() && pm is ITorqueProvider torqueProvider)
                    {
                        torqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                        sas.posTorque.Add(pos);
                        sas.negTorque.Add(neg);
                    }
                }
            }

            if (sas.posTorque.x < 0f) sas.posTorque.x = 0f;
            if (sas.posTorque.y < 0f) sas.posTorque.y = 0f;
            if (sas.posTorque.z < 0f) sas.posTorque.z = 0f;
            if (sas.negTorque.x < 0f) sas.negTorque.x = 0f;
            if (sas.negTorque.y < 0f) sas.negTorque.y = 0f;
            if (sas.negTorque.z < 0f) sas.negTorque.z = 0f;

            Vector3 averageTorque = new Vector3(
                0.5f * (sas.posTorque.x + sas.negTorque.x),
                0.5f * (sas.posTorque.y + sas.negTorque.y),
                0.5f * (sas.posTorque.z + sas.negTorque.z));

            return averageTorque;
        }

        static void VesselSAS_CheckCoasting_Override(KSPCFVesselSAS sas)
        {
            if (sas.angularAccelerationMax.x > 0f)
            {
                // float num = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.x / angularAccelerationMax.x);
                float num;
                if (sas.vessel.angularVelocity.x > 0f)
                    num = Mathf.Abs(Time.deltaTime + sas.vessel.angularVelocity.x / sas.angularAccelerationPosMax.x);
                else
                    num = Mathf.Abs(Time.deltaTime + sas.vessel.angularVelocity.x / sas.angularAccelerationNegMax.x);


                float num2 = Mathf.Abs(sas.rotationDelta.x * ((float)Math.PI / 180f) / sas.vessel.angularVelocity.x);
                if (num * sas.stopScalar > num2 && Math.Sign(sas.rotationDelta.x) != Math.Sign(sas.vessel.angularVelocity.x))
                {
                    sas.pidLockedPitch.Reset();
                    sas.rotationDelta.x = 0f - sas.rotationDelta.x;
                }
                else if (num * sas.coastScalar > num2 && Math.Sign(sas.rotationDelta.x) != Math.Sign(sas.vessel.angularVelocity.x))
                {
                    sas.pidLockedPitch.Reset();
                    sas.rotationDelta.x = 0f;
                }
            }

            if (sas.angularAccelerationMax.z > 0f)
            {
                //float num3 = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.z / angularAccelerationMax.z);
                float num3;
                if (sas.vessel.angularVelocity.z > 0f)
                    num3 = Mathf.Abs(Time.deltaTime + sas.vessel.angularVelocity.z / sas.angularAccelerationPosMax.z);
                else
                    num3 = Mathf.Abs(Time.deltaTime + sas.vessel.angularVelocity.z / sas.angularAccelerationNegMax.z);


                float num4 = Mathf.Abs(sas.rotationDelta.z * ((float)Math.PI / 180f) / sas.vessel.angularVelocity.z);
                if (num3 * sas.stopScalar > num4 && Math.Sign(sas.rotationDelta.z) != Math.Sign(sas.vessel.angularVelocity.z))
                {
                    sas.pidLockedYaw.Reset();
                    sas.rotationDelta.z = 0f - sas.rotationDelta.z;
                }
                else if (num3 * sas.coastScalar > num4 && Math.Sign(sas.rotationDelta.z) != Math.Sign(sas.vessel.angularVelocity.z))
                {
                    sas.pidLockedYaw.Reset();
                    sas.rotationDelta.z = 0f;
                }
            }
        }

        static void VesselSAS_UpdateMaximumAcceleration_Override(KSPCFVesselSAS sas)
        {
            sas.angularAccelerationMax.x = Mathf.Max(sas.torqueVector.x / sas.vessel.MOI.x, 0.0001f);
            sas.angularAccelerationMax.y = Mathf.Max(sas.torqueVector.y / sas.vessel.MOI.y, 0.0001f);
            sas.angularAccelerationMax.z = Mathf.Max(sas.torqueVector.z / sas.vessel.MOI.z, 0.0001f);

            sas.angularAccelerationPosMax.x = Mathf.Max(sas.posTorque.x / sas.vessel.MOI.x, 0.0001f);
            sas.angularAccelerationPosMax.y = Mathf.Max(sas.posTorque.y / sas.vessel.MOI.y, 0.0001f);
            sas.angularAccelerationPosMax.z = Mathf.Max(sas.posTorque.z / sas.vessel.MOI.z, 0.0001f);

            sas.angularAccelerationNegMax.x = Mathf.Max(sas.negTorque.x / sas.vessel.MOI.x, 0.0001f);
            sas.angularAccelerationNegMax.y = Mathf.Max(sas.negTorque.y / sas.vessel.MOI.y, 0.0001f);
            sas.angularAccelerationNegMax.z = Mathf.Max(sas.negTorque.z / sas.vessel.MOI.z, 0.0001f);
        }
    }

    public class KSPCFVesselSAS : VesselAutopilot.VesselSAS
    {
        internal Vector3 angularAccelerationPosMax;
        internal Vector3 angularAccelerationNegMax;

        public KSPCFVesselSAS(Vessel v) : base(v)
        {
        }
    }
}
