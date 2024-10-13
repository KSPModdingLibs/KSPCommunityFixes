using CompoundParts;
using Highlighting;
using KSP.UI;
using KSP.UI.Screens.Flight;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Highlighting.Highlighter;
using Renderer = UnityEngine.Renderer;

namespace KSPCommunityFixes.Performance
{
    internal class PartSystemsFastUpdate : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(Part), nameof(Part.Update));

            AddPatch(PatchType.Override, typeof(TemperatureGaugeSystem), nameof(TemperatureGaugeSystem.Update));

            AddPatch(PatchType.Override, typeof(Highlighter), nameof(Highlighter.UpdateRenderers));

            AddPatch(PatchType.Override, typeof(CModuleLinkedMesh), nameof(CModuleLinkedMesh.TrackAnchor));
        }

        private static void Part_Update_Override(Part p)
        {
            if (FlightDriver.Pause || p.state == PartStates.DEAD || p.state == PartStates.PLACEMENT || p.state == PartStates.CARGO)
                return;


            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.IsNotNullOrDestroyed())
            {
                p.onEditorUpdate();
                p.updateMirroring();
            }

            p.UpdateMouseOver();

            if (!HighLogic.LoadedSceneIsFlight || !p.started || !FlightGlobals.ready)
                return;

            PhysicsGlobals physicsGlobals = PhysicsGlobals.instance;
            if (physicsGlobals.IsNullOrDestroyed())
            {
                PhysicsGlobals.instance = UnityEngine.Object.FindObjectOfType<PhysicsGlobals>();
                physicsGlobals = PhysicsGlobals.instance;
            }

            if (p.temperatureRenderer != null)
                Part_UpdatePartTemperatureRenderer(p, physicsGlobals);

            if (p.overrideSkillUpdate || p.CrewCapacity > 0)
                Part_UpdatePartValues(p);

            p.onPartUpdate();

            if (p.state == PartStates.ACTIVE)
                p.onActiveUpdate();

            p.ModulesOnUpdate();
            p.InternalOnUpdate();
            Part_UpdateAeroDisplay(p, physicsGlobals);

            if (p.protoModuleCrew != null)
            {
                int i = 0;
                for (int count = p.protoModuleCrew.Count; i < count; i++)
                    p.protoModuleCrew[i].ActiveUpdate(p);
            }
        }

        private static void Part_UpdatePartTemperatureRenderer(Part p, PhysicsGlobals physicsGlobals)
        {
            MaterialColorUpdater mcu = p.temperatureRenderer;

            if (mcu.renderers == null || mcu.renderers.Count == 0)
                return;

            float skinTemp = (float)p.skinTemperature;

            float normalizedTemp;
            if (skinTemp <= physicsGlobals.blackBodyRadiationMin)
            {
                // when skin temp is below min temp, nothing to show (color.alpha = 0), so don't do anything if
                // current color alpha is 0 already.
                if (mcu.setColor.a == 0f)
                    return;

                normalizedTemp = 0f;
            }
            else if (skinTemp >= physicsGlobals.blackBodyRadiationMin)
            {
                normalizedTemp = 1f;
            }
            else
            {
                normalizedTemp = 
                    (skinTemp - physicsGlobals.blackBodyRadiationMin) 
                    / (physicsGlobals.blackBodyRadiationMax - physicsGlobals.blackBodyRadiationMin);
            }

            mcu.setColor = physicsGlobals.blackBodyRadiation.Evaluate(normalizedTemp);
            mcu.setColor.a *= physicsGlobals.blackBodyRadiationAlphaMult * p.blackBodyRadiationAlphaMult;
            mcu.mpb.SetColor(mcu.propertyID, mcu.setColor);

            for (int i = mcu.renderers.Count; i-- > 0;)
            {
                Renderer renderer = mcu.renderers[i];
                if (renderer.IsNullOrDestroyed())
                    mcu.renderers.RemoveAt(i);
                else
                    renderer.SetPropertyBlock(mcu.mpb);
            }
        }

        private static void Part_UpdatePartValues(Part p)
        {
            PartValues partValues = p.partValues;
            EventValueComparisonUpdate(partValues.MaxThrottle);
            EventValueComparisonUpdate(partValues.HeatProduction);
            EventValueComparisonUpdate(partValues.FuelUsage);
            EventValueComparisonUpdate(partValues.EnginePower);
            EventValueComparisonUpdate(partValues.SteeringRadius);
            EventValueComparisonUpdate(partValues.AutopilotSkill);
            EventValueComparisonUpdate(partValues.AutopilotKerbalSkill);
            EventValueComparisonUpdate(partValues.AutopilotSASSkill);
            EventValueComparisonUpdate(partValues.RepairSkill);
            EventValueComparisonUpdate(partValues.FailureRepairSkill);
            EventValueComparisonUpdate(partValues.ScienceSkill);
            EventValueComparisonUpdate(partValues.CommsRange);
            EventValueOperationUpdate(partValues.ScienceReturnSum);
            EventValueComparisonUpdate(partValues.ScienceReturnMax);
            EventValueComparisonUpdate(partValues.EVAChuteSkill);
        }

        private static void EventValueComparisonUpdate<T>(EventValueComparison<T> valueComparer)
        {
            T newValue = valueComparer.defaultValue;
            for (int i = valueComparer.events.Count; i-- > 0;)
            {
                EventValueComparison<T>.EvtDelegate valueGetter = valueComparer.events[i];

                if (valueGetter.originator is UnityEngine.Object unityObject && unityObject.IsDestroyed())
                {
                    Debug.Log($"EventManager: Removing event '{valueComparer.eventName}' for object of type '{valueGetter.originatorType}' as object is null.");
                    valueComparer.events.RemoveAt(i);
                    continue;
                }

                T suscriberValue = valueGetter.evt();
                if (valueComparer.comparer(suscriberValue, newValue))
                    newValue = suscriberValue;
            }

            valueComparer.value = newValue;
        }

        private static void EventValueOperationUpdate<T>(EventValueOperation<T> valueOperation)
        {
            T newValue = valueOperation.defaultValue;
            for (int i = valueOperation.events.Count; i-- > 0;)
            {
                EventValueOperation<T>.EvtDelegate valueGetter = valueOperation.events[i];

                if (valueGetter.originator is UnityEngine.Object unityObject && unityObject.IsDestroyed())
                {
                    Debug.Log($"EventManager: Removing event '{valueOperation.eventName}' for object of type '{valueGetter.originatorType}' as object is null.");
                    valueOperation.events.RemoveAt(i);
                    continue;
                }

                T suscriberValue = valueGetter.evt();
                newValue = valueOperation.operation(suscriberValue, newValue);
            }

            valueOperation.value = newValue;
        }

        private static void Part_UpdateAeroDisplay(Part p, PhysicsGlobals physicsGlobals)
        {
            bool shouldBeActive = physicsGlobals.aeroForceDisplay && p.staticPressureAtm > 0.0 && p.rb.IsNotNullOrDestroyed();
            if (shouldBeActive)
            {
                if (p.dragArrowPtr.IsNullOrDestroyed())
                {
                    p.dragArrowPtr = ArrowPointer.Create(p.transform, p.CoPOffset, p.dragVectorDirLocal, p.dragScalar * PhysicsGlobals.AeroForceDisplayScale, Color.red, worldSpace: false);
                }
                else
                {
                    p.dragArrowPtr.Offset = p.CoPOffset;
                    p.dragArrowPtr.Direction = p.dragVectorDirLocal;
                    p.dragArrowPtr.Length = p.dragScalar * PhysicsGlobals.AeroForceDisplayScale;
                }

                if (p.bodyLiftArrowPtr.IsNullOrDestroyed())
                {
                    p.bodyLiftArrowPtr = ArrowPointer.Create(p.transform, p.bodyLiftLocalPosition, p.bodyLiftLocalVector, PhysicsGlobals.AeroForceDisplayScale, Color.cyan, worldSpace: false);
                }
                else
                {
                    float magnitude = p.bodyLiftLocalVector.magnitude;
                    Vector3 direction = p.bodyLiftLocalVector;
                    p.bodyLiftArrowPtr.Direction = direction;
                    p.bodyLiftArrowPtr.Length = magnitude * PhysicsGlobals.AeroForceDisplayScale;
                }
            }
            else if (p.aeroDisplayWasActive)
            {
                if (p.dragArrowPtr.IsNotNullOrDestroyed())
                {
                    UnityEngine.Object.Destroy(p.dragArrowPtr.gameObject);
                    p.dragArrowPtr = null;
                }

                if (p.bodyLiftArrowPtr.IsNotNullOrDestroyed())
                {
                    UnityEngine.Object.Destroy(p.bodyLiftArrowPtr.gameObject);
                    p.bodyLiftArrowPtr = null;
                }
            }

            if (shouldBeActive != p.aeroDisplayWasActive)
                p.RefreshHighlighter();

            p.aeroDisplayWasActive = shouldBeActive;
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
