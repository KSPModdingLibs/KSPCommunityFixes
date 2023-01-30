using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class EditorPhysics : BasePatch
    {
        protected override bool IgnoreConfig => true;

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            instance = this;

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(DeltaVAppSituation), nameof(DeltaVAppSituation.UpdatePressureDisplay)),
                this));

            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            GameEvents.onDeltaVAppAtmosphereChanged.Add(OnDeltaVAppAtmosphereChanged);
            GameEvents.OnControlPointChanged.Add(OnControlPointChanged);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
        }

        private static EditorPhysics instance;
        public static bool TryGetAndUpdate(out EditorPhysics updatedInstance)
        {
            instance.Update();
            if (instance.isValid)
            {
                updatedInstance = instance;
                return true;
            }

            updatedInstance = null;
            return false;
        }

        private bool isValid;

        public Vector3 CoM => EditorMarker_CoM.CraftCoM;
        public Transform referenceTransform;
        public Part referencePart;
        public int referencePartShipIndex;

        public CelestialBody body;
        public double atmStaticPressureKpa;
        public double atmStaticPressure;
        public double atmDensity;
        public double atmTemperature;

        public int lastShipModificationFrame = int.MaxValue;
        private int lastShipStatsUpdateFrame;

        private void Update()
        {
            if (lastShipStatsUpdateFrame < lastShipModificationFrame)
            {
                lastShipStatsUpdateFrame = lastShipModificationFrame;
                EditorUpdateShipStats();
            }
        }

        private void EditorUpdateShipStats()
        {
            if (EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.parts.Count == 0)
            {
                isValid = false;
                return;
            }

            if (DeltaVGlobals.fetch != null && DeltaVGlobals.ready && DeltaVGlobals.DeltaVAppValues.body != null)
            {
                body = DeltaVGlobals.DeltaVAppValues.body;

                if (!DeltaVGlobals.DeltaVAppValues.body.atmosphere || DeltaVGlobals.DeltaVAppValues.situation == DeltaVSituationOptions.Vaccum)
                {
                    
                    atmStaticPressureKpa = 0.0;
                    atmStaticPressure = 0.0;
                    atmTemperature = 0.0;
                    atmDensity = 0.0;
                }
                else
                {
                    if (DeltaVGlobals.DeltaVAppValues.situation == DeltaVSituationOptions.Altitude)
                    {
                        atmTemperature = body.GetFullTemperature(DeltaVGlobals.DeltaVAppValues.altitude, 0.0);
                        atmStaticPressureKpa = body.GetPressure(DeltaVGlobals.DeltaVAppValues.altitude);
                    }
                    else
                    {
                        atmTemperature = body.GetFullTemperature(0.0, 0.0);
                        atmStaticPressureKpa = body.GetPressure(0.0);
                    }

                    atmStaticPressure = atmStaticPressureKpa * 0.0098692326671601278;
                    atmDensity = DeltaVGlobals.DeltaVAppValues.atmDensity;
                }
            }
            else
            {
                atmStaticPressureKpa = 0.0;
                atmStaticPressure = 0.0;
                atmTemperature = 0.0;
                atmDensity = 0.0;
            }

            if (referenceTransform.IsNullOrDestroyed() || EditorLogic.fetch.ship.Parts.IndexOf(referencePart) != referencePartShipIndex)
            {
                if (!GetFirstReferenceTransform(EditorLogic.RootPart, ref referenceTransform, ref referencePart))
                {
                    referencePart = EditorLogic.RootPart;
                    referenceTransform = EditorLogic.RootPart.referenceTransform;
                }

                referencePartShipIndex = EditorLogic.fetch.ship.Parts.IndexOf(referencePart);
            }

            if (referenceTransform.IsNullOrDestroyed())
            {
                isValid = false;
                return;
            }

            bool GetFirstReferenceTransform(Part part, ref Transform referenceTransform, ref Part referencePart)
            {
                if (part.isControlSource != Vessel.ControlLevel.NONE)
                {
                    ModuleCommand mc = part.FindModuleImplementing<ModuleCommand>();
                    if (mc != null && mc.controlPoints != null && mc.controlPoints.TryGetValue(mc.activeControlPointName, out ControlPoint ctrlPoint))
                        referenceTransform = ctrlPoint.transform;
                    else
                        referenceTransform = part.referenceTransform;

                    referencePart = part;
                    return true;
                }

                int childIdx = part.children.Count;
                while (childIdx-- > 0)
                {
                    if (GetFirstReferenceTransform(part.children[childIdx], ref referenceTransform, ref referencePart))
                        return true;
                }

                return false;
            }

            EditorMarker_CoM comMarker = EditorVesselOverlays.fetch.CoMmarker;
            if (comMarker == null)
            {
                isValid = false;
                return;
            }

            if (!comMarker.isActiveAndEnabled)
            {
                comMarker.rootPart = EditorLogic.RootPart;
                comMarker.UpdatePosition();
            }

            isValid = true;
        }

        private void OnGameSceneLoadRequested(GameScenes data)
        {
            referenceTransform = null;
            referencePart = null;
            referencePartShipIndex = -1;
            atmStaticPressure = 0f;
            lastShipModificationFrame = 0;
            lastShipStatsUpdateFrame = 0;
        }

        private void OnControlPointChanged(Part part, ControlPoint controlPoint)
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR || EditorLogic.fetch.ship == null)
                return;

            int partIndex = EditorLogic.fetch.ship.Parts.IndexOf(part);
            if (partIndex < 0)
                return;

            lastShipModificationFrame = Time.frameCount;
            referenceTransform = controlPoint.transform;
            referencePart = part;
            referencePartShipIndex = partIndex;
        }

        private void OnDeltaVAppAtmosphereChanged(DeltaVSituationOptions data)
        {
            lastShipModificationFrame = Time.frameCount;
        }

        private void OnEditorShipModified(ShipConstruct ship)
        {
            if (ship != EditorLogic.fetch.ship || ship.parts.Count == 0)
                return;

            lastShipModificationFrame = Time.frameCount;
        }

        // OnDeltaVAppAtmosphereChanged isn't fired when the altitude is modified, so implement our own event
        static void DeltaVAppSituation_UpdatePressureDisplay_Postfix()
        {
            instance.lastShipModificationFrame = Time.frameCount;
        }
    }
}