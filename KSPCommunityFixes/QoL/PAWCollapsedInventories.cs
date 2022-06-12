using HarmonyLib;
using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace KSPCommunityFixes.UI
{
    class PAWCollapsedInventories : BasePatch
    {
        private static readonly StringBuilder sb = new StringBuilder();

        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleInventoryPart), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleInventoryPart), "UpdateMassVolumeDisplay"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(UIPartActionInventory), "Setup"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(UIPartActionWindow), "AddCrewInventory", new[] { typeof(ProtoCrewMember) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(UIPartActionWindow), "AddCrewInventory", new[] { typeof(ProtoCrewMember) }),
                this));
        }

        static string GetGroupName(ModuleInventoryPart inventory)
        {
            if (inventory.kerbalMode && inventory.kerbalReference != null)
            {
                return inventory.kerbalReference.name;
            }

            return inventory.part.partInfo.name + "_Inventory";
        }


        static string GetGroupTitle(ModuleInventoryPart inventory)
        {
            sb.Clear();
            if (inventory.kerbalMode && inventory.kerbalReference != null)
            {
                sb.Append(inventory.kerbalReference.displayName);
            }
            else
            {
                sb.Append(Localizer.Format("#autoLOC_8320000"));
            }

            if (inventory.storedParts.Count == 0)
                return sb.ToString();

            sb.Append(" (");
            sb.Append(inventory.volumeOccupied.ToString("0.# L"));
            sb.Append(" - ");
            sb.Append(inventory.massOccupied.ToString("0.### t"));
            sb.Append(")");

            return sb.ToString();
        }

        // Add a collapsed by default PAW group to all ModuleInventoryPart :
        static void ModuleInventoryPart_OnStart_Postfix(ModuleInventoryPart __instance)
        {
            __instance.Fields["InventorySlots"].group = new BasePAWGroup(GetGroupName(__instance), GetGroupTitle(__instance), true);
        }

        static void ModuleInventoryPart_UpdateMassVolumeDisplay_Postfix(ModuleInventoryPart __instance)
        {
            UIPartActionWindow paw;
            if (__instance.kerbalMode)
                paw = __instance.grid?.pawInventory.DestroyedAsNull()?.Window.DestroyedAsNull();
            else
                paw = __instance.part.DestroyedAsNull()?.PartActionWindow.DestroyedAsNull();

            if (paw.IsNullRef())
                return;

            if (!paw.parameterGroups.TryGetValue(GetGroupName(__instance), out UIPartActionGroup uiGroup))
                return;

            BasePAWGroup group = __instance.Fields["InventorySlots"].group;

            if (uiGroup.groupHeader.IsNotNullOrDestroyed())
            {
                string title = GetGroupTitle(__instance);
                group.displayName = title;
                uiGroup.groupHeader.text = title;
            }
        }

        // Hide the "guiName" header text (redundant with the group title)
        static void UIPartActionInventory_Setup_Postfix(UIPartActionInventory __instance)
        {
            __instance.inventoryNameText.transform.parent.gameObject.SetActive(false);
        }

        // Kerbal inventories are already in a PAW group, use a transpiler to make it collapsed by default :
        static IEnumerable<CodeInstruction> UIPartActionWindow_AddCrewInventory_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            MethodInfo addGroup = AccessTools.Method(typeof(UIPartActionWindow), "AddGroup", new[] { typeof(UnityEngine.Transform), typeof(string), typeof(bool) });

            for (int i = 0; i < code.Count - 1; i++) // -1 since we will be checking i + 1
            {
                // We want to set startCollapsed to true :

                // AddGroup(uIPartActionFieldItem.transform, displayName, startCollapsed: false);
                // IL_00c5: ldarg.0
                // IL_00c6: ldloc.3
                // IL_00c7: callvirt instance class [UnityEngine.CoreModule]
                // UnityEngine.Transform[UnityEngine.CoreModule] UnityEngine.Component::get_transform()
                // IL_00cc: ldloc.s 4
                // IL_00ce: ldc.i4.0
                // IL_00cf: call instance void UIPartActionWindow::AddGroup(class [UnityEngine.CoreModule] UnityEngine.Transform, string, bool)

                if (code[i].opcode == OpCodes.Ldc_I4_0 && code[i + 1].opcode == OpCodes.Call && (MethodInfo)code[i + 1].operand == addGroup)
                {
                    code[i].opcode = OpCodes.Ldc_I4_1;
                    break;
                }
            }

            return code;
        }


        // Set the slots information in the kerbal inventories
        static void UIPartActionWindow_AddCrewInventory_Postfix(UIPartActionWindow __instance, ProtoCrewMember crewMember)
        {
            if (!__instance.parameterGroups.TryGetValue(crewMember.name, out UIPartActionGroup uiGroup))
            {
                return;
            }

            if (crewMember.KerbalInventoryModule.IsNotNullOrDestroyed() && uiGroup.groupHeader.IsNotNullOrDestroyed())
            {
                string title = GetGroupTitle(crewMember.KerbalInventoryModule);
                uiGroup.groupHeader.text = title;
            }

        }
    }
}
