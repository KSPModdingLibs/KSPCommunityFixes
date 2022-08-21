using HarmonyLib;
using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;
using UniLinq;
using UnityEngine.UI;

namespace KSPCommunityFixes.QoL
{
    class ToolbarShowHide : BasePatch
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
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.OnDestroy)),
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
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.ShouldItHide), Type.EmptyTypes),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.ShouldItHide), new Type[] { typeof(GameEvents.VesselSpawnInfo) }),
                this,
                "ApplicationLauncher_ShouldItHideVesselSpawn_Prefix"));

            defaultLayerId = SortingLayer.NameToID("Default");
            appsLayerId = SortingLayer.NameToID("Apps");
            actionsLayerId = SortingLayer.NameToID("Actions");
        }

        private static int defaultLayerId;
        private static int appsLayerId;
        private static int actionsLayerId;

        private static void SetAppLauncherOnTopOfKSCApps(ApplicationLauncher appLauncher, bool onTop)
        {
            Canvas canvas = appLauncher.GetComponent<Canvas>();
            if (onTop)
            {
                canvas.overrideSorting = true;
                canvas.sortingLayerID = actionsLayerId;
                canvas.sortingOrder = 20;

                foreach (ApplicationLauncherButton app in appLauncher.appList)
                {
                    if (app.spriteAnim.IsNotNullOrDestroyed() && app.spriteAnim.transform.TryGetComponent(out SpriteRenderer renderer))
                    {
                        renderer.sortingLayerID = actionsLayerId;
                        renderer.sortingOrder = 20;
                    }
                }

                foreach (ApplicationLauncherButton app in appLauncher.appListMod)
                {
                    if (app.spriteAnim.IsNotNullOrDestroyed() && app.spriteAnim.transform.TryGetComponent(out SpriteRenderer renderer))
                    {
                        renderer.sortingLayerID = actionsLayerId;
                        renderer.sortingOrder = 20;
                    }
                }
            }
            else
            {
                canvas.overrideSorting = false;
                canvas.sortingLayerID = defaultLayerId;
                canvas.sortingOrder = 0;
                
                foreach (ApplicationLauncherButton app in appLauncher.appList)
                {
                    if (app.spriteAnim.IsNotNullOrDestroyed() && app.spriteAnim.transform.TryGetComponent(out SpriteRenderer renderer))
                    {
                        renderer.sortingLayerID = appsLayerId;
                        renderer.sortingOrder = 1;
                    }
                }

                foreach (ApplicationLauncherButton app in appLauncher.appListMod)
                {
                    if (app.spriteAnim.IsNotNullOrDestroyed() && app.spriteAnim.transform.TryGetComponent(out SpriteRenderer renderer))
                    {
                        renderer.sortingLayerID = appsLayerId;
                        renderer.sortingOrder = 1;
                    }
                }
            }
        }

        static bool ApplicationLauncher_ShouldItShow_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                SetAppLauncherOnTopOfKSCApps(__instance, false);
                __instance.Show();
            }
            return false;
        }

        static bool ApplicationLauncher_ShouldItHide_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                SetAppLauncherOnTopOfKSCApps(__instance, true);
                __instance.Hide();
            }
            return false;
        }

        static bool ApplicationLauncher_ShouldItHideVesselSpawn_Prefix(ApplicationLauncher __instance)
        {
            if (__instance.ShouldBeVisible())
            {
                SetAppLauncherOnTopOfKSCApps(__instance, true);
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

        private static GameObject expand, collapse;
        private static GameObject spacer;

        static void ApplicationLauncher_SpawnSimpleLayout_Postfix(ApplicationLauncher __instance)
        {
            Transform appList = __instance.currentLayout.GetGameObject().transform.GetChild(0);
            if (__instance.IsPositionedAtTop)
            {
                expand = GameObject.Instantiate(appList.Find("BtnArrowDown").gameObject);
                collapse = GameObject.Instantiate(appList.Find("BtnArrowUp").gameObject);
                var lG = __instance.currentLayout.GetGameObject().AddComponent<VerticalLayoutGroup>();
                lG.childAlignment = appList.GetComponent<VerticalLayoutGroup>().childAlignment;
                lG.childForceExpandHeight = false;
            }
            else
            {
                expand = GameObject.Instantiate(appList.Find("BtnArrowLeft").gameObject);
                collapse = GameObject.Instantiate(appList.Find("BtnArrowRight").gameObject);
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

        static void ApplicationLauncher_OnDestroy_Postfix()
        {
            expand = null;
            collapse = null;
            spacer = null;
        }

        static void ApplicationLauncher_StartupSequence_Postfix(ApplicationLauncher __instance)
        {
            if (__instance.currentLayout == null)
                return;

            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                LayoutElement stockSpacerLayout = __instance.currentLayout.GetTopRightSpacer();
                stockSpacerLayout.transform.SetAsLastSibling();

                LayoutElement newSpacerLayout;
                spacer = __instance.currentLayout.GetGameObject().transform.Find("ExpandSpacer")?.gameObject;
                if (spacer == null)
                {
                    newSpacerLayout = GameObject.Instantiate(stockSpacerLayout);
                    spacer = newSpacerLayout.gameObject;
                    newSpacerLayout.name = "ExpandSpacer";
                    newSpacerLayout.transform.SetParent(__instance.currentLayout.GetGameObject().transform, false);
                }
                else
                {
                    newSpacerLayout = spacer.GetComponent<LayoutElement>();
                }
                newSpacerLayout.preferredHeight = stockSpacerLayout.preferredHeight;
                newSpacerLayout.preferredWidth = stockSpacerLayout.preferredWidth;
                newSpacerLayout.gameObject.SetActive(expand.activeSelf);
            }
            else
            {
                spacer = __instance.currentLayout.GetGameObject().transform.Find("ExpandSpacer")?.gameObject;
                if (spacer != null)
                    spacer.SetActive(false);
            }
        }

        private static void ToggleAppLauncher(bool show)
        {
            var list = ApplicationLauncher.Instance.currentLayout.GetGameObject().transform.GetChild(0);
            if (expand == null || list == null)
            {
                Debug.LogError($"Couldn't find button (null? {expand == null}) or list (null? {list == null})");
                ApplicationLauncher.Instance.launcherSpace.gameObject.SetActive(show);
                return;
            }

            expand.SetActive(!show);
            list.gameObject.SetActive(show);

            if (HighLogic.LoadedScene == GameScenes.EDITOR && spacer.IsNotNullOrDestroyed())
                spacer.SetActive(!show);
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
