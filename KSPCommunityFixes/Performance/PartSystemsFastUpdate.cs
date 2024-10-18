using CompoundParts;
using Highlighting;
using KSP.UI.Screens.Flight;
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

        private static void TemperatureGaugeSystem_Update_Override(TemperatureGaugeSystem tgs)
        {
            if (!FlightGlobals.ready || !HighLogic.LoadedSceneIsFlight)
                return;

            if (GameSettings.TEMPERATURE_GAUGES_MODE < 1 || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight)
            {
                if (tgs.gaugeCount > 0)
                    tgs.DestroyGauges();
                return;
            }

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel.NotDestroyedRefNotEquals(tgs.activeVessel))
                tgs.CreateGauges();

            if (activeVessel.IsNotNullOrDestroyed() && activeVessel.parts.Count != tgs.partCount)
                tgs.RebuildGaugeList();

            if (tgs.gaugeCount == 0)
                return;

            tgs.visibleGauges.Clear();
            for (int i = tgs.gaugeCount; i-- > 0;)
            {
                TemperatureGauge gauge = tgs.gauges[i];
                gauge.GaugeUpdate();
                if (gauge.gaugeActive)
                    tgs.visibleGauges.Add(gauge);
            }

            tgs.visibleGaugeCount = tgs.visibleGauges.Count;

            if (tgs.visibleGaugeCount > 0)
            {
                tgs.visibleGauges.Sort();

                for (int i = 0; i < tgs.visibleGaugeCount; i++)
                    tgs.visibleGauges[i].rTrf.SetSiblingIndex(i);
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
