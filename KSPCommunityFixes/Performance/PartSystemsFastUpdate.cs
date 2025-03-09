using CompoundParts;
using Highlighting;
using KSP.UI.Screens.Flight;
using KSP.UI.Util;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Highlighting.Highlighter;

namespace KSPCommunityFixes.Performance
{
    internal class PartSystemsFastUpdate : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(TemperatureGaugeSystem), nameof(TemperatureGaugeSystem.Update));

            AddPatch(PatchType.Override, typeof(Highlighter), nameof(Highlighter.UpdateRenderers));

            AddPatch(PatchType.Override, typeof(CModuleLinkedMesh), nameof(CModuleLinkedMesh.TrackAnchor));
        }

        // Complete reimplementation of TemperatureGaugeSystem :
        // - Minimal update overhead when no gauges are active/shown
        // - Vastly reduced update overhead when gauges/highlight are active/shown
        // - Gauges are now instantiated on demand, eliminating (most of) the cost on vessel load
        // - Gauges are now recycled when the vessel is modified / switched, instead of destroying and re-instantiating them immediately
        // Gauges were previously always instantiated for every part on the active vessel, and this was pretty slow due to triggering
        // a lot of internal UI/layout/graphic dirtying on every gauge instantiation. Overall, the operation can take several hundred
        // milliseconds in not-so large part count situations, leading to very significant hiccups 
        // TemperatureGaugeSystem.Update average frame time with a ~500 part vessel, gauges not shown : Stock 0.6%, KSPCF 0.04%
        // TemperatureGaugeSystem.Update average frame time with a ~500 part vessel, ~16 gauges shown : Stock 1.7%, KSPCF 0.3%
        // See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/194
        private static void TemperatureGaugeSystem_Update_Override(TemperatureGaugeSystem tgs)
        {
            if (!FlightGlobals.ready || !HighLogic.LoadedSceneIsFlight)
                return;

            if (GameSettings.TEMPERATURE_GAUGES_MODE == 0
                || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight
                || FlightGlobals.ActiveVessel.IsNullOrDestroyed())
            {
                if (tgs.visibleGauges.Count == 0)
                    return;

                for (int i = tgs.visibleGauges.Count; i-- > 0;)
                {
                    TemperatureGauge gauge = tgs.visibleGauges[i];
                    if (gauge.gaugeActive)
                        gauge.progressBar.gameObject.SetActive(false);

                    if (gauge.highlightActive && gauge.part.IsNotNullOrDestroyed())
                        gauge.part.SetHighlightDefault();

                    gauge.part = null;
                    gauge.gaugeActive = false;
                    gauge.showGauge = false;
                    gauge.highlightActive = false;
                }

                tgs.visibleGauges.Clear();
                return;
            }

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            List<Part> parts = activeVessel.parts;
            int partCount = parts.Count;

            List<TemperatureGauge> gauges = tgs.gauges;
            tgs.visibleGauges.Clear();

            if (tgs.activeVessel.IsNullRef())
            {
                tgs.activeVessel = activeVessel;
                tgs.partCount = partCount;
            }
            else if (tgs.activeVessel.RefNotEquals(activeVessel) || tgs.partCount != partCount)
            {
                tgs.activeVessel = activeVessel;
                tgs.partCount = partCount;

                for (int i = gauges.Count; i-- > 0;)
                {
                    TemperatureGauge gauge = gauges[i];
                    if (gauge.IsNullRef())
                        continue;

                    if (gauge.gaugeActive)
                        gauge.progressBar.gameObject.SetActive(false);

                    if (gauge.highlightActive && gauge.part.IsNotNullOrDestroyed())
                        gauge.part.SetHighlightDefault();

                    gauge.part = null;
                    gauge.gaugeActive = false;
                    gauge.showGauge = false;
                    gauge.highlightActive = false;
                }
            }

            while (gauges.Count < partCount)
                gauges.Add(null);

            while (gauges.Count > partCount)
            {
                int lastGaugeIdx = gauges.Count - 1;
                TemperatureGauge gauge = gauges[lastGaugeIdx];
                if (gauge.IsNotNullRef())
                    UnityEngine.Object.Destroy(gauge.gameObject);
                gauges.RemoveAt(lastGaugeIdx);
            }

            float gaugeThreshold = PhysicsGlobals.instance.temperatureGaugeThreshold;
            float gaugeHighlightThreshold = PhysicsGlobals.instance.temperatureGaugeHighlightThreshold;

            bool gaugesEnabled = (GameSettings.TEMPERATURE_GAUGES_MODE & 1) > 0;
            bool highlightsEnabled = (GameSettings.TEMPERATURE_GAUGES_MODE & 2) > 0;

            for (int i = 0; i < partCount; i++)
            {
                Part part = parts[i];

                float skinTempFactor = (float)(part.skinTemperature / part.skinMaxTemp);
                float tempFactor = (float)(part.temperature / part.maxTemp);

                if (skinTempFactor > tempFactor)
                    tempFactor = skinTempFactor;

                TemperatureGauge gauge = gauges[i];

                bool gaugeEnabled = gaugesEnabled && tempFactor > gaugeThreshold * part.gaugeThresholdMult;
                bool highlightEnabled = highlightsEnabled && tempFactor > gaugeHighlightThreshold * part.edgeHighlightThresholdMult;

                if (gaugeEnabled || highlightEnabled)
                {
                    if (gauge.IsNullRef())
                    {
                        gauge = UnityEngine.Object.Instantiate(tgs.temperatureGaugePrefab);
                        gauge.transform.SetParent(tgs.transform, worldPositionStays: false);
                        gauge.Setup(part, gaugeHighlightThreshold, gaugeThreshold);
                        gauges[i] = gauge;
                    }
                    else if (part.RefNotEquals(gauge.part))
                    {
                        gauge.part = part;
                    }

                    if (gaugeEnabled)
                    {
                        bool showGauge = true;
                        Vector3 partPos = part.partTransform.position;
                        // this is the main remaining perf hog
                        // It should be feasible to grab the camera matrix once and perform the transformation manually, but well...
                        gauge.uiPos = RectUtil.WorldToUISpacePos(partPos, FlightCamera.fetch.mainCamera, MainCanvasUtil.MainCanvasRect, ref showGauge);

                        if (showGauge)
                        {
                            tgs.visibleGauges.Add(gauge);

                            gauge.distanceFromCamera = Vector3.SqrMagnitude(FlightCamera.fetch.mainCamera.transform.position - partPos);
                            gauge.rTrf.localPosition = gauge.uiPos;

                            const float minValueChange = 1f / 120f; // slider is ~60 pixels at 100% UI scale
                            float valueChange = Math.Abs(gauge.progressBar.value - tempFactor);
                            if (valueChange > minValueChange)
                            {
                                gauge.sliderFill.color = Color.Lerp(Color.green, Color.red, tempFactor);
                                gauge.progressBar.value = tempFactor; // setting this is awfully slow, so only set it when the slider is visually changing...
                            }
                        }

                        if (!gauge.gaugeActive || showGauge != gauge.showGauge)
                        {
                            gauge.gaugeActive = true;
                            gauge.showGauge = showGauge;
                            gauge.progressBar.gameObject.SetActive(showGauge);
                        }
                    }
                    else if (gauge.gaugeActive)
                    {
                        gauge.gaugeActive = false;
                        gauge.showGauge = false;
                        gauge.progressBar.gameObject.SetActive(false);
                    }

                    if (highlightEnabled)
                    {
                        if (!gauge.highlightActive)
                        {
                            gauge.highlightActive = true;
                            part.SetHighlightType(Part.HighlightType.AlwaysOn);
                            part.SetHighlight(active: true, recursive: false);
                        }

                        gauge.edgeRatio = Mathf.InverseLerp(gauge.edgeHighlightThreshold * part.edgeHighlightThresholdMult, 1f, tempFactor);
                        gauge.colorScale = tempFactor;
                        part.SetHighlightColor(Color.Lerp(XKCDColors.Red * tempFactor, XKCDColors.KSPNotSoGoodOrange * tempFactor, gauge.edgeRatio));
                    }
                    else if (gauge.highlightActive)
                    {
                        gauge.highlightActive = false;
                        if (gauge.part.IsNotNullOrDestroyed())
                            part.SetHighlightDefault();
                    }
                }
                else if (gauge.IsNotNullRef() && gauge.gaugeActive)
                {
                    gauge.progressBar.gameObject.SetActive(false);
                    gauge.gaugeActive = false;
                    gauge.showGauge = false;
                    if (gauge.highlightActive)
                    {
                        gauge.highlightActive = false;
                        if (gauge.part.IsNotNullOrDestroyed())
                            part.SetHighlightDefault();
                    }
                }
            }

            tgs.visibleGaugeCount = tgs.visibleGauges.Count;

            if (tgs.visibleGaugeCount > 0)
            {
                // A simple manual insertion sort is an order of magnitude faster than going through the IComparable interface
                List<TemperatureGauge> visibleGauges = tgs.visibleGauges;
                for (int i = 1; i < tgs.visibleGaugeCount; i++)
                {
                    TemperatureGauge current = visibleGauges[i];
                    int j = i;
                    while (j > 0 && visibleGauges[j - 1].distanceFromCamera > current.distanceFromCamera)
                        visibleGauges[j] = visibleGauges[--j];

                    visibleGauges[j] = current;
                }

                for (int i = 0; i < tgs.visibleGaugeCount; i++)
                {
                    TemperatureGauge visibleGauge = visibleGauges[i];
                    if (visibleGauge.rTrf.GetSiblingIndex() != i)
                        visibleGauge.rTrf.SetSiblingIndex(i);
                }

            }
        }

        private static bool Highlighter_UpdateRenderers_Override(Highlighter hl)
        {
            if (hl.renderersDirty)
            {
                Part part = hl.tr.GetComponent<Part>();
                if (part.IsNullRef())
                    return true; // if highlighter is not on a part, fall back to stock logic

                List<Renderer> partRenderers = part.FindModelRenderersReadOnly();
                hl.highlightableRenderers = new List<RendererCache>(partRenderers.Count);
                for (int i = partRenderers.Count; i-- > 0;)
                {
                    Renderer renderer = partRenderers[i];
                    if (renderer.gameObject.layer != 1 && !renderer.material.name.Contains("KSP/Alpha/Translucent Additive"))
                    {
                        RendererCache rendererCache = new RendererCache(renderer, hl.opaqueMaterial, hl.zTestFloat, hl.stencilRefFloat);
                        hl.highlightableRenderers.Add(rendererCache);
                    }
                }
                hl.highlighted = false;
                hl.renderersDirty = false;
                hl.currentColor = Color.clear;
                return true;
            }

            bool dirty = false;
            for (int i = hl.highlightableRenderers.Count; i-- > 0;)
            {
                if (hl.highlightableRenderers[i].renderer.IsDestroyed())
                {
                    hl.highlightableRenderers[i].CleanUp();
                    hl.highlightableRenderers.RemoveAt(i);
                    hl.renderersDirty = true;
                    dirty = true;
                }
            }

            return dirty;
        }

        private static void CModuleLinkedMesh_TrackAnchor_Override(CModuleLinkedMesh strut, bool setTgtAnchor, Vector3 rDir, Vector3 rPos, Quaternion rRot)
        {
            if (strut.targetAnchor.IsNotNullOrDestroyed())
            {
                if (!strut.tweakingTarget && !strut.part.PartTweakerSelected)
                {
                    if (setTgtAnchor && strut.compoundPart.IsNotNullOrDestroyed() && strut.compoundPart.transform.IsNotNullOrDestroyed())
                    {
                        strut.targetAnchor.position = strut.compoundPart.transform.TransformPoint(rPos);
                        strut.targetAnchor.rotation = strut.compoundPart.transform.rotation * rRot;
                    }
                }
                else
                {
                    strut.compoundPart.targetPosition = strut.transform.InverseTransformPoint(strut.targetAnchor.position);
                    strut.compoundPart.targetRotation = strut.targetAnchor.localRotation;
                    strut.compoundPart.UpdateWorldValues();
                }
            }

            bool lineRotationComputed = false;
            Quaternion lineRotation = default;
            if (strut.endCap.IsNotNullOrDestroyed())
            {
                Vector3 vector = strut.line.position - strut.endCap.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < strut.lineMinimumLength)
                    {
                        strut.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        strut.line.gameObject.SetActive(true);
                        lineRotation = Quaternion.LookRotation(vector / magnitude, strut.transform.forward);
                        lineRotationComputed = true;
                        strut.line.rotation = lineRotation;
                        Vector3 localScale = strut.line.localScale;
                        strut.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * strut.part.scaleFactor);
                        strut.endCap.rotation = lineRotation;
                    }
                }
            }
            else if (strut.transform.IsNotNullOrDestroyed() && strut.targetAnchor.IsNotNullOrDestroyed())
            {
                Vector3 vector = strut.transform.position - strut.targetAnchor.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < strut.lineMinimumLength)
                    {
                        strut.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        strut.line.gameObject.SetActive(true);
                        if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                        {
                            Debug.LogError(string.Concat("[CModuleLinkedMesh]: Object ", strut.name, ": Look vector magnitude invalid. Vector is (", vector.x, ", ", vector.y, ", ", vector.z, "). Transform ", strut.transform.position.IsInvalid() ? "invalid" : "valid", " ", strut.transform.position, ", target ", strut.targetAnchor.position.IsInvalid() ? "invalid" : "valid", ", ", strut.targetAnchor.position));
                            return;
                        }
                        lineRotation = Quaternion.LookRotation(vector / magnitude, strut.transform.forward);
                        lineRotationComputed = true;
                        strut.line.rotation = lineRotation;
                        Vector3 localScale = strut.line.localScale;
                        strut.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * strut.part.scaleFactor);
                    }
                }
            }
            if (strut.startCap.IsNotNullOrDestroyed())
            {
                if (!lineRotationComputed)
                    strut.startCap.rotation = strut.line.rotation;
                else
                    strut.startCap.rotation = lineRotation;
            }
        }
    }
}
