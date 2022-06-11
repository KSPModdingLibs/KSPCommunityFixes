using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KSPCommunityFixes
{
    // note on the flickering issue :
    // - pin 2 PAWS that have at least a group and a few other ungrouped fields
    // - trigger a rebuild by grabbing a part then destroying it
    // - only one of the 2 PAW flickers


    class PAWItemsOrder : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UIPartActionWindow), "AddGroup", new Type[] {typeof(Transform), typeof(BasePAWGroup)}),
                this, nameof(UIPartActionWindow_AddGroup_1_Prefix)));

            if (KSPCommunityFixes.KspVersion >= new Version(1, 11, 0))
            {
                patches.Add(new PatchInfo(
                    PatchMethodType.Prefix,
                    AccessTools.Method(typeof(UIPartActionWindow), "AddGroup", new Type[] { typeof(Transform), typeof(string), typeof(bool) }),
                    this, nameof(UIPartActionWindow_AddGroup_2_Prefix)));
            }

#if DEBUG
            if (KSPCommunityFixes.KspVersion >= new Version(1, 12, 2))
            {
                patches.Add(new PatchInfo(
                    PatchMethodType.Prefix,
                    AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.CreatePartList)),
                    this));
            }
#endif
        }

        static bool UIPartActionWindow_AddGroup_1_Prefix(UIPartActionWindow __instance, Transform t, BasePAWGroup group, ref int ___controlIndex)
        {
            if (__instance.parameterGroups.TryGetValue(group.name, out UIPartActionGroup groupUI))
            {
                groupUI.AddItemToContent(t);
                ___controlIndex++;
                return false;
            }
            groupUI = Object.Instantiate(UIPartActionController.Instance.groupPrefab);
            groupUI.gameObject.SetActive(value: true);
            groupUI.transform.SetParent(__instance.layoutGroup.transform, worldPositionStays: false);
            groupUI.transform.SetSiblingIndex(___controlIndex++);
            __instance.parameterGroups.Add(group.name, groupUI);
            groupUI.AddItemToContent(t);
            ___controlIndex++; // this was the missing bit
            groupUI.Initialize(group.name, group.displayName, group.startCollapsed, __instance);
            return false;
        }

        static bool UIPartActionWindow_AddGroup_2_Prefix(UIPartActionWindow __instance, Transform t, string groupName, bool startCollapsed, ref int ___controlIndex)
        {
            if (__instance.parameterGroups.TryGetValue(groupName, out UIPartActionGroup groupUI))
            {
                groupUI.AddItemToContent(t);
                ___controlIndex++;
                return false;
            }
            groupUI = Object.Instantiate(UIPartActionController.Instance.groupPrefab);
            groupUI.gameObject.SetActive(value: true);
            groupUI.transform.SetParent(__instance.layoutGroup.transform, worldPositionStays: false);
            groupUI.transform.SetSiblingIndex(___controlIndex++);
            __instance.parameterGroups.Add(groupName, groupUI);
            groupUI.AddItemToContent(t);
            ___controlIndex++; // this was the missing bit
            groupUI.Initialize(groupName, groupName, startCollapsed, __instance);
            return false;
        }

#if DEBUG

        // This is a replica of the 1.12.2 CreatePartList() method, for debugging purposes

        private static readonly MethodInfo RemoveItemAt = AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.RemoveItemAt));
        private static readonly MethodInfo HasKerbalInventory = AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.HasKerbalInventory));
        private static readonly MethodInfo GetPartCrew = AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.GetPartCrew));
        private static readonly MethodInfo AddCrewInventory = AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.AddCrewInventory), new Type[] { typeof(ProtoCrewMember) });
        private static readonly MethodInfo RemoveCrewInventory = AccessTools.Method(typeof(UIPartActionWindow), nameof(UIPartActionWindow.RemoveCrewInventory));

        static bool UIPartActionWindow_CreatePartList_Prefix(UIPartActionWindow __instance, bool clearFirst, ref bool __result, ref int ___controlIndex, ref UIPartActionWindow.DisplayType ___displayType, ref UI_Scene ___scene)
        {
            if (__instance.part.IsNullOrDestroyed())
            {
                __result = false;
                return false;
            }
            if (clearFirst)
            {
                __instance.ClearList();
            }
            else
            {
                int count = __instance.ListItems.Count;
                while (count-- > 0)
                {
                    if (!__instance.ListItems[count].IsItemValid())
                    {
                        if (__instance.ListItems[count].IsNotNullOrDestroyed())
                        {
                            Object.DestroyImmediate(__instance.ListItems[count].gameObject);
                        }

                        RemoveItemAt.Invoke(__instance, new object[] {count});
                        //__instance.RemoveItemAt(count);
                    }
                }
            }
            GameEvents.onPartActionUICreate.Fire(__instance.part);
            ___controlIndex = 0;
            if (___displayType != UIPartActionWindow.DisplayType.ResourceOnly)
            {
                int i = 0;
                for (int count2 = __instance.part.Fields.Count; i < count2; i++)
                {
                    BaseField baseField = __instance.part.Fields[i];
                    if (__instance.CanActivateField(baseField, __instance.part, ___scene))
                    {
                        if (clearFirst || !__instance.TrySetFieldControl(baseField, __instance.part, null))
                        {
                            __instance.AddFieldControl(baseField, __instance.part, null);
                        }
                    }
                    else if (!clearFirst && !__instance.Pinned)
                    {
                        __instance.RemoveFieldControl(baseField, __instance.part, null);
                    }
                }
                int j = 0;
                for (int count3 = __instance.part.Modules.Count; j < count3; j++)
                {
                    PartModule partModule = __instance.part.Modules[j];
                    if (!partModule.isEnabled)
                    {
                        continue;
                    }
                    int k = 0;
                    for (int count4 = partModule.Fields.Count; k < count4; k++)
                    {
                        BaseField baseField = partModule.Fields[k];
                        if (__instance.CanActivateField(baseField, __instance.part, ___scene))
                        {
                            if (clearFirst || !__instance.TrySetFieldControl(baseField, __instance.part, partModule))
                            {
                                __instance.AddFieldControl(baseField, __instance.part, partModule);
                            }
                        }
                        else if (!clearFirst && !__instance.Pinned)
                        {
                            __instance.RemoveFieldControl(baseField, __instance.part, partModule);
                        }
                    }
                }

                bool hasCrewInventory = (bool)HasKerbalInventory.Invoke(__instance, new object[] {__instance.part});
                if (hasCrewInventory)
                {
                    bool flag = FlightGlobals.ActiveVessel == __instance.part.vessel || (__instance.part.transform.position - FlightGlobals.ActiveVessel.transform.position).magnitude < GameSettings.EVA_INVENTORY_RANGE;
                    List<ProtoCrewMember> partCrew = (List<ProtoCrewMember>)GetPartCrew.Invoke(__instance, new object[] {__instance.part});// __instance.GetPartCrew(__instance.part);
                    for (int l = 0; l < partCrew.Count; l++)
                    {
                        ProtoCrewMember protoCrewMember = partCrew[l];
                        if (flag)
                        {
                            if (clearFirst || !__instance.TrySetFieldControl(protoCrewMember.KerbalInventoryModule.Fields["InventorySlots"], __instance.part, protoCrewMember.KerbalInventoryModule))
                            {
                                AddCrewInventory.Invoke(__instance, new object[] {partCrew[l]});
                                //__instance.AddCrewInventory(partCrew[l]);
                            }
                        }
                        else if (!clearFirst && !__instance.Pinned)
                        {
                            RemoveCrewInventory.Invoke(__instance, new object[] { __instance.part });
                            //__instance.RemoveCrewInventory(__instance.part);
                        }
                    }
                }
                int m = 0;
                for (int count5 = __instance.part.Events.Count; m < count5; m++)
                {
                    BaseEvent byIndex = __instance.part.Events.GetByIndex(m);
                    if (__instance.CanActivateEvent(byIndex, __instance.part, ___scene))
                    {
                        if (clearFirst || !__instance.TrySetEventControl(byIndex, __instance.part, null))
                        {
                            __instance.AddEventControl(byIndex, __instance.part, null);
                        }
                    }
                    else if (!clearFirst && !__instance.Pinned)
                    {
                        __instance.RemoveEventControl(byIndex, __instance.part, null);
                    }
                }
                int n = 0;
                for (int count6 = __instance.part.Modules.Count; n < count6; n++)
                {
                    PartModule partModule = __instance.part.Modules[n];
                    if (!partModule.isEnabled)
                    {
                        continue;
                    }
                    int num = 0;
                    for (int count7 = partModule.Events.Count; num < count7; num++)
                    {
                        BaseEvent byIndex = partModule.Events.GetByIndex(num);
                        if (__instance.CanActivateEvent(byIndex, __instance.part, ___scene))
                        {
                            if (clearFirst || !__instance.TrySetEventControl(byIndex, __instance.part, partModule))
                            {
                                __instance.AddEventControl(byIndex, __instance.part, partModule);
                            }
                        }
                        else if (!clearFirst && !__instance.Pinned)
                        {
                            __instance.RemoveEventControl(byIndex, __instance.part, partModule);
                        }
                    }
                }
                int num2 = 0;
                for (int count8 = __instance.part.Resources.Count; num2 < count8; num2++)
                {
                    PartResource r = __instance.part.Resources[num2];
                    __instance.SetupResourceControls(r, clearFirst, ___scene, ref ___controlIndex);
                }
                if (__instance.CanActivateResourcePriorityDisplay(__instance.part))
                {
                    if (!__instance.TrySetResourcePriorityControl(__instance.part))
                    {
                        __instance.AddResourcePriorityControl(__instance.part);
                    }
                }
                else
                {
                    __instance.RemoveResourcePriorityControl(__instance.part);
                }
                if (__instance.CanActivateFuelFlowOverlay(__instance.part))
                {
                    if (!__instance.TrySetFuelFlowOverlay(__instance.part))
                    {
                        __instance.AddFuelFlowOverlay(__instance.part);
                    }
                }
                else
                {
                    __instance.RemoveFuelFlowOverlay(__instance.part);
                }
                if (__instance.CanActivateAeroDisplay(__instance.part))
                {
                    if (!__instance.TrySetAeroControl(__instance.part))
                    {
                        __instance.AddAeroControl(__instance.part);
                    }
                }
                else
                {
                    __instance.RemoveAeroControl(__instance.part);
                }
                if (__instance.CanActivateThermalDisplay(__instance.part))
                {
                    if (!__instance.TrySetThermalControl(__instance.part))
                    {
                        __instance.AddThermalControl(__instance.part);
                    }
                }
                else
                {
                    __instance.RemoveThermalControl(__instance.part);
                }
                if (__instance.CanActivateRoboticJointDisplay(__instance.part))
                {
                    if (!__instance.TrySetRoboticJointControl(__instance.part))
                    {
                        __instance.AddRoboticJointControl(__instance.part);
                    }
                }
                else
                {
                    __instance.RemoveRoboticJointControl(__instance.part);
                }
            }
            else
            {
                int num3 = 0;
                for (int count9 = __instance.part.Resources.Count; num3 < count9; num3++)
                {
                    PartResource r = __instance.part.Resources[num3];
                    if (UIPartActionController.Instance.resourcesShown.Contains(r.info.id))
                    {
                        __instance.SetupResourceControls(r, clearFirst: false, ___scene, ref ___controlIndex);
                    }
                    else
                    {
                        __instance.RemoveResourceControlFlight(r);
                    }
                }
            }
            if (__instance.ListItems.Count == 0 && !__instance.Pinned)
            {
                __result = false;
                return false;
            }
            int num4 = 0;
            for (int count10 = __instance.ListItems.Count; num4 < count10; num4++)
            {
                __instance.ListItems[num4].UpdateItem();
            }
            __result = true;
            return false;
        }
#endif

    }
}
