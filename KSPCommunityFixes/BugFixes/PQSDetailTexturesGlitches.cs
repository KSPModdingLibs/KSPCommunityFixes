using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.BugFixes
{
    internal class PQSDetailTexturesGlitches : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, AccessTools.PropertySetter(typeof(FloatingOrigin), nameof(FloatingOrigin.TerrainShaderOffset)));
            AddPatch(PatchType.Override, typeof(FloatingOrigin), nameof(FloatingOrigin.OnCameraChange));
            AddPatch(PatchType.Override, typeof(Vessel), nameof(Vessel.Update));
        }

        private const double resetOpportunisticThresholdSquared = 10000.0 * 10000.0;
        private const double resetMaxThresholdSquared = 100000.0 * 100000.0;

        private static void FloatingOrigin_set_TerrainShaderOffset_Override(Vector3d value)
        {
            FloatingOrigin fo = FloatingOrigin.fetch;
            if (fo.IsNullOrDestroyed())
                return;

            double sqrMag = value.sqrMagnitude;
            bool resetOffset = false;

            if (sqrMag > resetMaxThresholdSquared)
            {
                // Debug.Log("[PQSDetailTexturesGlitches] Offset is too large, resetting...");
                resetOffset = true;
            }
            else if (sqrMag > resetOpportunisticThresholdSquared 
                && CameraManager.Instance.IsNotNullOrDestroyed() 
                && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight
                && FlightGlobals.ActiveVessel.IsNotNullOrDestroyed() 
                && FlightGlobals.currentMainBody.IsNotNullOrDestroyed()
                && FlightGlobals.currentMainBody.pqsController.IsNotNullOrDestroyed())
            {
                if (FlightCamera.fetch.cameraAlt > FlightCamera.fetch.mainCamera.farClipPlane)
                {
                    resetOffset = true;
                    // Debug.Log("[PQSDetailTexturesGlitches] Terrain isn't visible (too far), resetting offset");
                }
                else
                {
                    ViewFrustum vf = new ViewFrustum(FlightCamera.fetch.mainCamera);
                    if (!vf.ContainsSphere(FlightGlobals.currentMainBody.position, FlightGlobals.currentMainBody.pqsController.radiusMax))
                    {
                        resetOffset = true;
                        // Debug.Log("[PQSDetailTexturesGlitches] Terrain isn't visible (not in view frustum), resetting offset");
                    }
                }
            }

            if (resetOffset)
                value = Vector3d.zero;

            fo.terrainShaderOffset = value;
            Shader.SetGlobalVector("_floatingOriginOffset", new Vector4((float)value.x, (float)value.y, (float)value.z, 0f));
        }

        private static void FloatingOrigin_OnCameraChange_Override(FloatingOrigin fo, CameraManager.CameraMode newMode)
        {
            // prevent a visible terrain texture position switch when the camera didn't actually change
            if (newMode == CameraManager.CameraMode.Flight && CameraManager.Instance.previousCameraMode == CameraManager.CameraMode.Flight)
                return;

            FloatingOrigin.ResetTerrainShaderOffset();
        }

        private static void Vessel_Update_Override(Vessel v)
        {
            if (v.state == Vessel.State.DEAD)
                return;

            if (v.situation == Vessel.Situations.PRELAUNCH)
                v.launchTime = Planetarium.GetUniversalTime();

            v.missionTime = Planetarium.GetUniversalTime() - v.launchTime;
            v.UpdateVesselModuleActivation();

            if (v.autoClean && !v.loaded && HighLogic.CurrentGame.CurrenciesAvailable)
            {
                v.Clean(v.autoCleanReason);
            }
            else
            {
                if (!FlightGlobals.ready)
                    return;

                if (v != FlightGlobals.ActiveVessel)
                {
                    if (v.loaded && Vector3.Distance(v.vesselTransform.position, FlightGlobals.ActiveVessel.vesselTransform.position) > v.vesselRanges.GetSituationRanges(v.situation).unload)
                    {
                        v.Unload();
                    }
                    if (!v.loaded && Vector3.Distance(v.vesselTransform.position, FlightGlobals.ActiveVessel.vesselTransform.position) < v.vesselRanges.GetSituationRanges(v.situation).load)
                    {
                        v.Load();
                        if (v.loaded && v.vesselType == VesselType.SpaceObject)
                        {
                            if (v.comet != null)
                            {
                                AnalyticsUtil.LogCometVesselEvent(AnalyticsUtil.SpaceObjectEventTypes.reached, HighLogic.CurrentGame, v);
                            }
                            else
                            {
                                AnalyticsUtil.LogAsteroidVesselEvent(AnalyticsUtil.SpaceObjectEventTypes.reached, HighLogic.CurrentGame, v);
                            }
                        }
                    }
                }
                //else if (v.mainBody != null && v.mainBody.pqsController != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight)
                //{
                //    //double num = v.mainBody.pqsController.radiusMax - v.mainBody.pqsController.radius;
                //    //if (FlightGlobals.camera_altitude > num && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight)
                //    //{
                //    //    float num2 = Vector3.Dot((v.transform.position - v.mainBody.bodyTransform.position).normalized, FlightCamera.fetch.mainCamera.transform.forward);
                //    //    float num3 = Mathf.Cos(0.5f * FlightCamera.fetch.mainCamera.fieldOfView * FlightCamera.fetch.mainCamera.aspect * ((float)Math.PI / 180f));
                //    //    if (num2 > num3)
                //    //    {
                //    //        FloatingOrigin.ResetTerrainShaderOffset();
                //    //    }
                //    //}
                //}

                if (v.IsControllable && !v.packed)
                    v.autopilot.Update();
            }
        }

        public struct ViewFrustum
        {
            /// <summary>Left plane</summary>
            public PlaneD lP;
            /// <summary>Right plane</summary>
            public PlaneD rP;
            /// <summary>Bottom plane</summary>
            public PlaneD bP;
            /// <summary>Top plane</summary>
            public PlaneD tP;
            /// <summary>Near plane</summary>
            public PlaneD nP;
            /// <summary>Far plane</summary>
            public PlaneD fP;

            /// <summary>Near top-left corner</summary>
            public Vector3d ntlC;
            /// <summary>Near top-right corner</summary>
            public Vector3d ntrC;
            /// <summary>Near bottom-left corner</summary>
            public Vector3d nblC;
            /// <summary>Near bottom-right corner</summary>
            public Vector3d nbrC;
            /// <summary>Far top-left corner</summary>
            public Vector3d ftlC;
            /// <summary>Far top-right corner</summary>
            public Vector3d ftrC;
            /// <summary>Far bottom-left corner</summary>
            public Vector3d fblC;
            /// <summary>Far bottom-right corner</summary>
            public Vector3d fbrC;

            //public Vector3d[] corners;

            public ViewFrustum(Camera camera)
            {
                Matrix4x4D vp = (Matrix4x4D)camera.projectionMatrix * (Matrix4x4D)camera.worldToCameraMatrix;

                lP = CreatePlane(vp.m30 + vp.m00, vp.m31 + vp.m01, vp.m32 + vp.m02, vp.m33 + vp.m03);
                rP = CreatePlane(vp.m30 - vp.m00, vp.m31 - vp.m01, vp.m32 - vp.m02, vp.m33 - vp.m03);
                bP = CreatePlane(vp.m30 + vp.m10, vp.m31 + vp.m11, vp.m32 + vp.m12, vp.m33 + vp.m13);
                tP = CreatePlane(vp.m30 - vp.m10, vp.m31 - vp.m11, vp.m32 - vp.m12, vp.m33 - vp.m13);
                nP = CreatePlane(vp.m30 + vp.m20, vp.m31 + vp.m21, vp.m32 + vp.m22, vp.m33 + vp.m23);
                fP = CreatePlane(vp.m30 - vp.m20, vp.m31 - vp.m21, vp.m32 - vp.m22, vp.m33 - vp.m23);

                nblC = GetPlanesIntersection(ref lP, ref bP, ref nP);
                ntlC = GetPlanesIntersection(ref lP, ref tP, ref nP);
                ntrC = GetPlanesIntersection(ref rP, ref tP, ref nP);
                nbrC = GetPlanesIntersection(ref rP, ref bP, ref nP);

                fblC = GetPlanesIntersection(ref lP, ref bP, ref fP);
                ftlC = GetPlanesIntersection(ref lP, ref tP, ref fP);
                ftrC = GetPlanesIntersection(ref rP, ref tP, ref fP);
                fbrC = GetPlanesIntersection(ref rP, ref bP, ref fP);
            }

            public bool ContainsSphere(Vector3d sC, double sR)
            {
                // First check if the sphere is fully outside the frustum by checking against
                // the planes. We don't strictly need this, but this is cheap to check and
                // allow to early out in many cases before the much heavier face checks.
                double sRNeg = -sR;

                double dToL = lP.GetDistanceToPoint(sC);
                if (dToL < sRNeg) return false;

                double dToR = rP.GetDistanceToPoint(sC);
                if (dToR < sRNeg) return false;

                double dToB = bP.GetDistanceToPoint(sC);
                if (dToB < sRNeg) return false;

                double dToT = tP.GetDistanceToPoint(sC);
                if (dToT < sRNeg) return false;

                double dToF = fP.GetDistanceToPoint(sC);
                if (dToF < sRNeg) return false;

                double dToN = nP.GetDistanceToPoint(sC);
                if (dToN < sRNeg) return false;

                // At this point, we know the sphere is either fully contained by the frustum,
                // or intersecting at least one plane. But the frustum is defined by 6 faces,
                // not 6 infinite planes, so we can have false positives, especially with our
                // main use case of a camera skimming the surface of a huge sphere. For our 
                // check to be fully reliable, we need to check the intersection between each 
                // face, decomposed as two triangles each.

                // First eliminate the case where the sphere is fully contained by the frustum
                if (dToL > sR && dToR > sR && dToB > sR && dToT > sR && dToF > sR && dToN > sR)
                    return true;

                // Left face
                if (SphereIntersectTriangle(ref ntlC, ref ftlC, ref nblC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref nblC, ref ftlC, ref fblC, ref sC, sR)) return true;

                // Right face
                if (SphereIntersectTriangle(ref ntrC, ref nbrC, ref ftrC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref ftrC, ref nbrC, ref fbrC, ref sC, sR)) return true;

                // Bottom face
                if (SphereIntersectTriangle(ref nblC, ref fblC, ref nbrC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref nbrC, ref fblC, ref fbrC, ref sC, sR)) return true;

                // Top face
                if (SphereIntersectTriangle(ref ntlC, ref ntrC, ref ftlC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref ftlC, ref ntrC, ref ftrC, ref sC, sR)) return true;

                // Near face
                if (SphereIntersectTriangle(ref ntlC, ref nblC, ref ntrC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref ntrC, ref nblC, ref nbrC, ref sC, sR)) return true;

                // Far face
                if (SphereIntersectTriangle(ref ftlC, ref ftrC, ref fblC, ref sC, sR)) return true;
                if (SphereIntersectTriangle(ref fblC, ref ftrC, ref fbrC, ref sC, sR)) return true;

                return false;
            }

            private static bool SphereIntersectTriangle(ref Vector3d tA, ref Vector3d tB, ref Vector3d tC, ref Vector3d sC, double sR)
            {
                Vector3d tAO = tA - sC;
                Vector3d tBO = tB - sC;
                Vector3d tCO = tC - sC;
                double rr = sR * sR;
                Vector3d V = Vector3d.Cross(tBO - tAO, tCO - tAO);
                double d = Numerics.Dot(ref tAO, ref V);
                double e = V.sqrMagnitude;
                if (d * d > rr * e)
                    return false;

                double aa = tAO.sqrMagnitude;
                double ab = Numerics.Dot(ref tAO, ref tBO);
                double ac = Numerics.Dot(ref tAO, ref tCO);
                if ((aa > rr) && (ab > aa) && (ac > aa))
                    return false;

                double bb = tBO.sqrMagnitude;
                double bc = Numerics.Dot(ref tBO, ref tCO);
                if ((bb > rr) && (ab > bb) && (bc > bb))
                    return false;

                double cc = tCO.sqrMagnitude;
                if ((cc > rr) && (ac > cc) && (bc > cc))
                    return false;

                double d1 = ab - aa;
                Vector3d AB = tBO - tAO;
                double e1 = AB.sqrMagnitude;
                Vector3d Q1 = tAO * e1 - d1 * AB;
                Vector3d QC = tCO * e1 - Q1;
                if ((Q1.sqrMagnitude > rr * e1 * e1) && (Numerics.Dot(ref Q1, ref QC) > 0.0))
                    return false;

                double d2 = bc - bb;
                Vector3d BC = tCO - tBO;
                double e2 = BC.sqrMagnitude;
                Vector3d Q2 = tBO * e2 - d2 * BC;
                Vector3d QA = tAO * e2 - Q2;
                if ((Q2.sqrMagnitude > rr * e2 * e2) && (Numerics.Dot(ref Q2, ref QA) > 0.0))
                    return false;

                double d3 = ac - cc;
                Vector3d CA = tAO - tCO;
                double e3 = CA.sqrMagnitude;
                Vector3d Q3 = tCO * e3 - d3 * CA;
                Vector3d QB = tBO * e3 - Q3;
                if ((Q3.sqrMagnitude > rr * e3 * e3) && (Numerics.Dot(ref Q3, ref QB) > 0.0))
                    return false;

                return true;
            }

            private static PlaneD CreatePlane(double a, double b, double c, double d)
            {
                double length = Math.Sqrt(a * a + b * b + c * c);
                return new PlaneD(a / length, b / length, c / length, d / length);
            }

            private static Vector3d GetPlanesIntersection(ref PlaneD p1, ref PlaneD p2, ref PlaneD p3)
            {
                // Note 1 : this assume the planes are valid and actually intersecting 
                // Note 2 : code is optimized from :
                // double denom = Vector3d.Dot(p1.normal, Vector3d.Cross(p2.normal, p3.normal));
                // Vector3d cross23 = Vector3d.Cross(p2.normal, p3.normal) * -p1.distance;
                // Vector3d cross31 = Vector3d.Cross(p3.normal, p1.normal) * -p2.distance;
                // Vector3d cross12 = Vector3d.Cross(p1.normal, p2.normal) * -p3.distance;
                // return (cross23 + cross31 + cross12) / denom;

                double a1 = p1.normal.x, b1 = p1.normal.y, c1 = p1.normal.z, d1 = p1.distance;
                double a2 = p2.normal.x, b2 = p2.normal.y, c2 = p2.normal.z, d2 = p2.distance;
                double a3 = p3.normal.x, b3 = p3.normal.y, c3 = p3.normal.z, d3 = p3.distance;

                double denom =
                    a1 * (b2 * c3 - c2 * b3) -
                    b1 * (a2 * c3 - c2 * a3) +
                    c1 * (a2 * b3 - b2 * a3);

                double x =
                    (b2 * c3 - c2 * b3) * -d1 +
                    (c1 * b3 - b1 * c3) * -d2 +
                    (b1 * c2 - c1 * b2) * -d3;

                double y = 
                    (c2 * a3 - a2 * c3) * -d1 +
                    (a1 * c3 - c1 * a3) * -d2 +
                    (c1 * a2 - a1 * c2) * -d3;

                double z =
                    (a2 * b3 - b2 * a3) * -d1 +
                    (b1 * a3 - a1 * b3) * -d2 +
                    (a1 * b2 - b1 * a2) * -d3;

                return new Vector3d(x / denom, y / denom, z / denom);
            }
        }

        public struct PlaneD
        {
            public Vector3d normal;
            public double distance;

            public PlaneD(double normalX, double normalY, double normalZ, double distance)
            {
                normal = new Vector3d(normalX, normalY, normalZ);
                this.distance = distance;
            }

            public double GetDistanceToPoint(Vector3d point)
            {
                return Vector3d.Dot(normal, point) + distance;
            }
        }

    }
}
