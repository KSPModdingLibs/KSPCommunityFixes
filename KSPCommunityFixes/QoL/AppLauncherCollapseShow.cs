using HarmonyLib;
using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;
using UniLinq;
using UnityEngine.UI;

namespace KSPCommunityFixes.QoL
{
    class AppLauncherCollapseShow : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.SpawnSimpleLayout)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.StartupSequence)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.Show)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.Hide)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.ShouldItShow)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.ShouldItHide)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.ShouldItHide), new Type[] { typeof(GameEvents.VesselSpawnInfo) }),
                this,
                "ApplicationLauncher_ShouldItHideVesselSpawn_Prefix"));
        }
        static bool ApplicationLauncher_ShouldItShow_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                Canvas canvas = ApplicationLauncher.Instance.GetComponent<Canvas>();
                canvas.sortingLayerName = "Default";
                canvas.sortingOrder = 0;
                canvas.overrideSorting = false;
                __instance.Show();
            }
            return false;
        }

        static bool ApplicationLauncher_ShouldItHide_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                Canvas canvas = ApplicationLauncher.Instance.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingLayerName = "Actions";
                canvas.sortingOrder = 20;
                __instance.Hide();
            }
            return false;
        }

        static bool ApplicationLauncher_ShouldItHideVesselSpawn_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                Canvas canvas = ApplicationLauncher.Instance.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingLayerName = "Actions";
                canvas.sortingOrder = 20;
                __instance.Hide();
            }
            return false;
        }

        static bool ApplicationLauncher_Show_Prefix(ApplicationLauncher __instance)
        {
            if (!__instance.launcherSpace.gameObject.activeSelf)
                __instance.launcherSpace.gameObject.SetActive(true);

            ExpandAppLauncher();
            return false;
        }

        static bool ApplicationLauncher_Hide_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                CollapseAppLauncher();
                return false;
            }

            __instance.launcherSpace.gameObject.SetActive(false);
            __instance.onHide();
            ApplicationLauncher.Ready = false;
            return false;
        }

        static void ApplicationLauncher_SpawnSimpleLayout_Postfix(ApplicationLauncher __instance)
        {
            List<Transform> allChilds = __instance.launcherSpace.GetComponentsInChildren<Transform>(true).ToList();
            GameObject expand, collapse;
            Transform appList = __instance.currentLayout.GetGameObject().transform.GetChild(0);
            if (__instance.IsPositionedAtTop)
            {
                expand = GameObject.Instantiate(allChilds.First(t => t.name == "BtnArrowDown").gameObject);
                collapse = GameObject.Instantiate(allChilds.First(t => t.name == "BtnArrowUp").gameObject);
                var lG = __instance.currentLayout.GetGameObject().AddComponent<VerticalLayoutGroup>();
                lG.childAlignment = appList.GetComponent<VerticalLayoutGroup>().childAlignment;
                lG.childForceExpandHeight = false;
            }
            else
            {
                expand = GameObject.Instantiate(allChilds.First(t => t.name == "BtnArrowLeft").gameObject);
                collapse = GameObject.Instantiate(allChilds.First(t => t.name == "BtnArrowRight").gameObject);
                var lG = __instance.currentLayout.GetGameObject().AddComponent<HorizontalLayoutGroup>();
                lG.childAlignment = appList.GetComponent<HorizontalLayoutGroup>().childAlignment;
                lG.childForceExpandWidth = false;
            }

            expand.name = "BtnExpand";
            expand.transform.SetParent(__instance.currentLayout.GetGameObject().transform, false);
            expand.SetActive(true);
            GameObject.DestroyImmediate(expand.GetComponent<KSP.UI.PointerClickAndHoldHandler>());
            var exButton = expand.GetComponent<Button>();
            exButton.onClick = new Button.ButtonClickedEvent();
            exButton.onClick.AddListener(ExpandAppLauncher);
            expand.SetActive(false);

            collapse.name = "BtnCollapse";
            collapse.transform.SetParent(appList.transform, false);
            if (__instance.IsPositionedAtTop)
                collapse.transform.SetSiblingIndex(0);
            collapse.SetActive(true);
            GameObject.DestroyImmediate(collapse.GetComponent<KSP.UI.PointerClickAndHoldHandler>());
            var colButton = collapse.GetComponent<Button>();
            colButton.onClick = new Button.ButtonClickedEvent();
            colButton.onClick.AddListener(CollapseAppLauncher);
        }
        static void ApplicationLauncher_StartupSequence_Postfix(ApplicationLauncher __instance)
        {
            if (__instance.currentLayout == null)
                return;

            Transform[] childs = ApplicationLauncher.Instance.launcherSpace.GetComponentsInChildren<Transform>(true);
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                LayoutElement topRightSpacer = __instance.currentLayout.GetTopRightSpacer();
                topRightSpacer.transform.SetAsLastSibling();

                LayoutElement newSpacer;
                var newSpacerTransform = childs.FirstOrDefault(t => t.name == "ExpandSpacer");
                if (newSpacerTransform == null)
                {
                    newSpacer = GameObject.Instantiate(topRightSpacer);
                    newSpacer.name = "ExpandSpacer";
                    newSpacer.transform.SetParent(__instance.currentLayout.GetGameObject().transform, false);
                }
                else
                {
                    newSpacer = newSpacerTransform.gameObject.GetComponent<LayoutElement>();
                }
                newSpacer.preferredHeight = topRightSpacer.preferredHeight;
                newSpacer.preferredWidth = topRightSpacer.preferredWidth;
                newSpacer.gameObject.SetActive(childs.First(t => t.name == "BtnExpand").gameObject.activeSelf);
            }
            else
            {
                var newSpacerTransform = childs.FirstOrDefault(t => t.name == "ExpandSpacer");
                if (newSpacerTransform != null)
                    newSpacerTransform.gameObject.SetActive(false);
            }
        }

        private static void ToggleAppLauncher(bool show)
        {
            Transform[] childs = ApplicationLauncher.Instance.launcherSpace.GetComponentsInChildren<Transform>(true);
            var expand = childs.FirstOrDefault(t => t.name == "BtnExpand");
            var list = ApplicationLauncher.Instance.currentLayout.GetGameObject().transform.GetChild(0);
            if (expand == null || list == null)
            {
                Debug.LogError($"Couldn't find button (null? {expand == null}) or list (null? {list == null})");
                ApplicationLauncher.Instance.launcherSpace.gameObject.SetActive(show);
                return;
            }
            expand.gameObject.SetActive(!show);
            list.gameObject.SetActive(show);
            var spacer = childs.FirstOrDefault(t => t.name == "ExpandSpacer");
            if (spacer != null)
                spacer.gameObject.SetActive(!show);
        }

        public static void ExpandAppLauncher()
        {
            ToggleAppLauncher(true);
            ApplicationLauncher.Ready = true;
            ApplicationLauncher.Instance.onShow();
        }

        public static void CollapseAppLauncher()
        {
            ToggleAppLauncher(false);
            ApplicationLauncher.Instance.onHide();
            ApplicationLauncher.Ready = false;
        }
    }
}
