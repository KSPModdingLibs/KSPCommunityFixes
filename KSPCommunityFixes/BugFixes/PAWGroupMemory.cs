using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes
{
    public class PAWGroupMemory : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        private static Dictionary<int, Dictionary<string, bool>> collapseState;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(UIPartActionGroup), nameof(UIPartActionGroup.Initialize)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(UIPartActionGroup), nameof(UIPartActionGroup.Collapse)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(UIPartActionGroup), nameof(UIPartActionGroup.Expand)),
                this));

            collapseState = new Dictionary<int, Dictionary<string, bool>>();

            GameEvents.onGameSceneSwitchRequested.Add(OnSceneSwitch);
        }

        private void OnSceneSwitch(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            collapseState.Clear();
        }

        static IEnumerable<CodeInstruction> UIPartActionGroup_Initialize_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            /*
            original :
            public void Initialize(string groupName, string groupDisplayName, bool startCollapsed, UIPartActionWindow pawWindow)
            {
                this.groupName = groupName;
                this.groupDisplayName = Localizer.Format(groupDisplayName);
                window = pawWindow;
                isContentCollapsed = startCollapsed || GameSettings.collpasedPAWGroups.Contains(groupName);
                groupHeader.text = this.groupDisplayName;
                SetUIState();
                initialized = true;
            }

            patched :
            public void Initialize(string groupName, string groupDisplayName, bool startCollapsed, UIPartActionWindow pawWindow)
            {
                this.groupName = groupName;
                this.groupDisplayName = Localizer.Format(groupDisplayName);
                window = pawWindow;
                if (!PAWGroupMemory.IsGroupCollapsed(pawWindow, groupName, out isContentCollapsed))
                {
                    isContentCollapsed = startCollapsed || GameSettings.collpasedPAWGroups.Contains(groupName);
                }
                groupHeader.text = this.groupDisplayName;
                SetUIState();
                initialized = true;
            }

            patched method IL :
            // this.groupName = groupName;
	            IL_0000: ldarg.0
	            IL_0001: ldarg.1
	            IL_0002: stfld string UIPartActionGroup::groupName
	            // this.groupDisplayName = Localizer.Format(groupDisplayName);
	            IL_0007: ldarg.0
	            IL_0008: ldarg.2
	            IL_0009: call string KSP.Localization.Localizer::Format(string)
	            IL_000e: stfld string UIPartActionGroup::groupDisplayName
	            // window = pawWindow;
	            IL_0013: ldarg.0
	            IL_0014: ldarg.s pawWindow
	            IL_0016: stfld class UIPartActionWindow UIPartActionItem::window
              
              // start of edit
              // if (!IsGroupCollapsed(pawWindow, groupName, out isContentCollapsed))
	            IL_001b: ldarg.s pawWindow
	            IL_001d: ldarg.1
	            IL_001e: ldarg.0
	            IL_001f: ldflda bool UIPartActionGroup::isContentCollapsed
	            IL_0024: call bool KSPCommunityFixes.UIPartActionGroupDummy::IsGroupCollapsed(class ['Assembly-CSharp']UIPartActionWindow, string, bool&)
	            IL_0029: brtrue.s IL_0042
              // end of edit
              
	            // isContentCollapsed = startCollapsed || GameSettings.collpasedPAWGroups.Contains(groupName);
	            IL_002b ldarg.0
	            IL_002c: ldarg.3
	            IL_002d: brtrue.s IL_003c

	            IL_002f: ldsfld class [mscorlib]System.Collections.Generic.List`1<string> GameSettings::collpasedPAWGroups
	            IL_0034: ldarg.1
	            IL_0035: callvirt instance bool class [mscorlib]System.Collections.Generic.List`1<string>::Contains(!0)
	            IL_003a: br.s IL_003d

	            IL_003c: ldc.i4.1

	            IL_003d: stfld bool UIPartActionGroup::isContentCollapsed
	            
              // groupHeader.text = this.groupDisplayName;
	            IL_0042: ldarg.0
	            IL_0043: ldfld class TMPro.TextMeshProUGUI UIPartActionGroup::groupHeader
	            IL_0048: ldarg.0
	            IL_0049: ldfld string UIPartActionGroup::groupDisplayName
	            IL_004e: callvirt instance void TMPro.TMP_Text::set_text(string)
	            // SetUIState();
	            IL_0053: ldarg.0
	            IL_0054: call instance void UIPartActionGroup::SetUIState()
	            // initialized = true;
	            IL_0059: ldarg.0
	            IL_005a: ldc.i4.1
	            IL_005b: stfld bool UIPartActionGroup::initialized
	            // }
	            IL_0060: ret
            */

            FieldInfo isContentCollapsed = AccessTools.Field(typeof(UIPartActionGroup), "isContentCollapsed");
            MethodInfo IsGroupCollapsed = AccessTools.Method(typeof(PAWGroupMemory), nameof(PAWGroupMemory.IsGroupCollapsed));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int insertionIndex = 0;
            Label jump = il.DefineLabel();

            for (int i = 0; i < code.Count - 2; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0
                    && code[i + 1].opcode == OpCodes.Ldarg_3
                    && code[i + 2].opcode == OpCodes.Brtrue_S)
                {
                    insertionIndex = i;
                    for (int j = i; j < code.Count - 1; j++)
                    {
                        if (code[j].opcode == OpCodes.Stfld && ReferenceEquals(code[j].operand, isContentCollapsed))
                        {
                            code[j + 1].labels.Add(jump);
                            break;
                        }
                    }
                    break;
                }
            }

            List<CodeInstruction> insert = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_S, 4),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldflda, isContentCollapsed),
                new CodeInstruction(OpCodes.Call, IsGroupCollapsed),
                new CodeInstruction(OpCodes.Brtrue_S, jump)
            };

            code.InsertRange(insertionIndex, insert);

            return code;
        }

        static bool IsGroupCollapsed(UIPartActionWindow pawWindow, string groupName, out bool collapsed)
        {
            int id = pawWindow.part?.GetInstanceID() ?? 0;
            if (id != 0 && collapseState.TryGetValue(id, out Dictionary<string, bool> groups) && groups.TryGetValue(groupName, out collapsed))
                return true;

            collapsed = false;
            return false;
        }

        static void UIPartActionGroup_Collapse_Postfix(UIPartActionGroup __instance, string ___groupName)
        {
            if (string.IsNullOrEmpty(___groupName))
                return;

            int id = __instance.Window?.part?.GetInstanceID() ?? 0;
            if (id == 0)
                return;

            if (!collapseState.TryGetValue(id, out Dictionary<string, bool> partGroups))
            {
                partGroups = new Dictionary<string, bool>();
                collapseState[id] = partGroups;
            }
            partGroups[___groupName] = true;
        }

        static void UIPartActionGroup_Expand_Postfix(UIPartActionGroup __instance, string ___groupName)
        {
            if (string.IsNullOrEmpty(___groupName))
                return;

            int id = __instance.Window?.part?.GetInstanceID() ?? 0;
            if (id == 0)
                return;

            if (!collapseState.TryGetValue(id, out Dictionary<string, bool> partGroups))
            {
                partGroups = new Dictionary<string, bool>();
                collapseState[id] = partGroups;
            }
            partGroups[___groupName] = false;
        }
    }
}
