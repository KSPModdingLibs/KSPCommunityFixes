using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.UI
{
    // Add a collapsed by default PAW group to all ModuleInventoryPart :
    [HarmonyPatch(typeof(ModuleInventoryPart))]
    [HarmonyPatch("OnStart")]
    class ModuleInventoryPart_OnStart
    {
        static void Postfix(ModuleInventoryPart __instance)
        {
            __instance.Fields["InventorySlots"].group = new BasePAWGroup("Inventory", "Inventory", true);
        }

    }

    // Kerbal inventories are already in a PAW group, use a transpiler to make it collapsed by default :
    [HarmonyPatch(typeof(UIPartActionWindow))]
    [HarmonyPatch("AddCrewInventory")]
    [HarmonyPatch(new Type[] { typeof(ProtoCrewMember) })]
    class UIPartActionWindow_AddCrewInventory
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
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
    }
}
