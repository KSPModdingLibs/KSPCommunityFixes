using System;
using CompoundParts;
using HarmonyLib;
using Highlighting;
using KSP.UI.Screens.Flight;
using System.Collections.Generic;
using UnityEngine;
using static Highlighting.Highlighter;

namespace KSPCommunityFixes.Performance
{
    internal class AuxiliarySystemsFastUpdate : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TemperatureGaugeSystem), nameof(TemperatureGaugeSystem.Update)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Highlighter), nameof(Highlighter.UpdateRenderers)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VolumeNormalizer), nameof(VolumeNormalizer.Update)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(CModuleLinkedMesh), nameof(CModuleLinkedMesh.TrackAnchor)),
                this));
        }

        private static bool TemperatureGaugeSystem_Update_Prefix(TemperatureGaugeSystem __instance)
        {
            if (!FlightGlobals.ready || !HighLogic.LoadedSceneIsFlight)
                return false;

            if (GameSettings.TEMPERATURE_GAUGES_MODE < 1 || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight)
            {
                if (__instance.gaugeCount > 0)
                    __instance.DestroyGauges();

                return false;
            }

            if (__instance.TestRecreate())
                __instance.CreateGauges();

            if (__instance.TestRebuild())
                __instance.RebuildGaugeList();

            if (__instance.gaugeCount == 0)
                return false;

            __instance.visibleGauges.Clear();
            for (int i = __instance.gaugeCount; i-- > 0;)
            {
                TemperatureGauge gauge = __instance.gauges[i];
                gauge.GaugeUpdate();
                if (gauge.gaugeActive)
                    __instance.visibleGauges.Add(gauge);
            }

            __instance.visibleGaugeCount = __instance.visibleGauges.Count;

            if (__instance.visibleGaugeCount > 0)
            {
                __instance.visibleGauges.Sort();

                for (int i = 0; i < __instance.visibleGaugeCount; i++)
                    __instance.visibleGauges[i].rTrf.SetSiblingIndex(i);
            }
            return false;
        }

        private static bool Highlighter_UpdateRenderers_Prefix(Highlighter __instance, out bool __result)
        {
            __result = __instance.renderersDirty;
            if (__result)
            {
                Part part = __instance.tr.GetComponent<Part>();
                if (part.IsNullRef())
                    return true; // if highlighter is not on a part, fall back to stock logic

                List<Renderer> partRenderers = part.FindModelRenderersReadOnly();
                __instance.highlightableRenderers = new List<RendererCache>(partRenderers.Count);
                for (int i = partRenderers.Count; i-- > 0;)
                {
                    Renderer renderer = partRenderers[i];
                    if (renderer.gameObject.layer != 1 && !renderer.material.name.Contains("KSP/Alpha/Translucent Additive"))
                    {
                        RendererCache rendererCache = new RendererCache(renderer, __instance.opaqueMaterial, __instance.zTestFloat, __instance.stencilRefFloat);
                        __instance.highlightableRenderers.Add(rendererCache);
                    }
                }
                __instance.highlighted = false;
                __instance.renderersDirty = false;
                __instance.currentColor = Color.clear;
                __result = true;
                return false;
            }

            for (int i = __instance.highlightableRenderers.Count; i-- > 0;)
            {
                if (__instance.highlightableRenderers[i].renderer.IsDestroyed())
                {
                    __instance.highlightableRenderers[i].CleanUp();
                    __instance.highlightableRenderers.RemoveAt(i);
                    __instance.renderersDirty = true;
                    __result = true;
                }
            }

            return false;
        }

        private static bool VolumeNormalizer_Update_Prefix(VolumeNormalizer __instance)
        {
            float newVolume;
            if (GameSettings.SOUND_NORMALIZER_ENABLED)
            {
                __instance.threshold = GameSettings.SOUND_NORMALIZER_THRESHOLD;
                __instance.sharpness = GameSettings.SOUND_NORMALIZER_RESPONSIVENESS;
                AudioListener.GetOutputData(__instance.samples, 0);
                __instance.level = 0f;

                for (int i = 0; i < __instance.sampleCount; i += 1 + GameSettings.SOUND_NORMALIZER_SKIPSAMPLES)
                    __instance.level = Mathf.Max(__instance.level, Mathf.Abs(__instance.samples[i]));

                if (__instance.level > __instance.threshold)
                    newVolume = __instance.threshold / __instance.level;
                else
                    newVolume = 1f;

                newVolume = Mathf.Lerp(AudioListener.volume, newVolume * GameSettings.MASTER_VOLUME, __instance.sharpness * Time.deltaTime);
            }
            else
            {
                newVolume = Mathf.Lerp(AudioListener.volume, GameSettings.MASTER_VOLUME, __instance.sharpness * Time.deltaTime);
            }

            if (newVolume != __instance.volume)
                AudioListener.volume = newVolume;

            __instance.volume = newVolume;

            return false;

        }

        private static bool CModuleLinkedMesh_TrackAnchor_Prefix(CModuleLinkedMesh __instance, bool setTgtAnchor, Vector3 rDir, Vector3 rPos, Quaternion rRot)
        {
            CModuleLinkedMesh st = __instance;

            if (st.targetAnchor.IsNotNullOrDestroyed())
            {
                if (!st.tweakingTarget && !st.part.PartTweakerSelected)
                {
                    if (setTgtAnchor && st.compoundPart.IsNotNullOrDestroyed() && st.compoundPart.transform.IsNotNullOrDestroyed())
                    {
                        st.targetAnchor.position = st.compoundPart.transform.TransformPoint(rPos);
                        st.targetAnchor.rotation = st.compoundPart.transform.rotation * rRot;
                    }
                }
                else
                {
                    st.compoundPart.targetPosition = st.transform.InverseTransformPoint(st.targetAnchor.position);
                    st.compoundPart.targetRotation = st.targetAnchor.localRotation;
                    st.compoundPart.UpdateWorldValues();
                }
            }

            bool lineRotationComputed = false;
            Quaternion lineRotation = default;
            if (st.endCap.IsNotNullOrDestroyed())
            {
                Vector3 vector = st.line.position - st.endCap.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < st.lineMinimumLength)
                    {
                        st.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        st.line.gameObject.SetActive(true);
                        lineRotation = Quaternion.LookRotation(vector / magnitude, st.transform.forward);
                        lineRotationComputed = true;
                        st.line.rotation = lineRotation;
                        Vector3 localScale = st.line.localScale;
                        st.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * st.part.scaleFactor);
                        st.endCap.rotation = lineRotation;
                    }
                }
            }
            else if (st.transform.IsNotNullOrDestroyed() && st.targetAnchor.IsNotNullOrDestroyed())
            {
                Vector3 vector = st.transform.position - st.targetAnchor.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < st.lineMinimumLength)
                    {
                        st.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        st.line.gameObject.SetActive(true);
                        if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                        {
                            Debug.LogError(string.Concat("[CModuleLinkedMesh]: Object ", st.name, ": Look vector magnitude invalid. Vector is (", vector.x, ", ", vector.y, ", ", vector.z, "). Transform ", st.transform.position.IsInvalid() ? "invalid" : "valid", " ", st.transform.position, ", target ", st.targetAnchor.position.IsInvalid() ? "invalid" : "valid", ", ", st.targetAnchor.position));
                            return false;
                        }
                        lineRotation = Quaternion.LookRotation(vector / magnitude, st.transform.forward);
                        lineRotationComputed = true;
                        st.line.rotation = lineRotation;
                        Vector3 localScale = st.line.localScale;
                        st.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * st.part.scaleFactor);
                    }
                }
            }
            if (st.startCap.IsNotNullOrDestroyed())
            {
                if (!lineRotationComputed)
                    st.startCap.rotation = st.line.rotation;
                else
                    st.startCap.rotation = lineRotation;
            }

            return false;
        }
    }
}
