// backups are created from :
// - KerbalFSM
//   - pod_select : root part dropped ?
//   - podDeleted : root part deleted ?
//   - partPicked : any attached part picked
//   - partDropped : any part dropped without attaching ?
//   - partAttached : any part attached
// - variant changed
// - action group edited
// - offset/rotate gizmo updated

// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/172
// In stock, undo state is captured after part events are complete, which is annoying as undoing will loose any tweaks made in between
// This patch invert the undo/redo state capture logic, by moving state capture before attaching / detaching instead of after
// Unfortunately, the crew assignement (VesselCrewManifest) is updated based on the last serialized undo state, so doing this notably
// result in the crew assignement window being out of sync with the ship current state, but this will likely have other weird side effects.
// My thanks to the spaghetti mess of the editor code...
// Not sure this is really fixable, this would likely require a complete rewrite of the VesselCrewManifest creation/update as well.
// Of course I could just double-save the ship and update the crew manifest separatly, but the whole point was trying to avoid the stutter
// induced by excessive ship state saving... 

using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.QoL
{
    internal class BetterEditorUndoRedo : BasePatch
    {
        private static bool editorPatched = false;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetBackup)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.RestoreState)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.RestoreState)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetupFSM)),
                this));
        }

        static void EditorLogic_SetBackup_Postfix(EditorLogic __instance)
        {
            if (__instance.ship.parts.Count == 0)
                return;

            Debug.Log($"[UNDO/REDO] backup created, undoLevel={__instance.undoLevel}, states={ShipConstruction.backups.Count}");
        }

        static void EditorLogic_RestoreState_Postfix(EditorLogic __instance, int offset)
        {
            Debug.Log($"[UNDO/REDO] state {offset} restored, undoLevel={__instance.undoLevel}, states={ShipConstruction.backups.Count}");
        }

        static void EditorLogic_RestoreState_Prefix(EditorLogic __instance, int offset)
        {
            if (__instance.ship.parts.Count == 0 || offset >= 0 || __instance.undoLevel < ShipConstruction.backups.Count)
                return;

            __instance.SetBackup();

            Debug.Log($"[UNDO/REDO] created backup for redo");
        }

        static void EditorLogic_SetupFSM_Postfix(EditorLogic __instance)
        {
            if (editorPatched)
                return;

            editorPatched = true;

            MethodInfo m_onPartPicked = __instance.on_partPicked.OnEvent.Method; // <SetupFSM>b__189_21()
            MethodInfo m_onPartAttached = __instance.on_partAttached.OnEvent.Method; // <SetupFSM>b__189_29()

            KSPCommunityFixes.Harmony.Patch(m_onPartPicked, null, null, new HarmonyMethod(AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPartPickedTranspiler))));
            KSPCommunityFixes.Harmony.Patch(m_onPartAttached, null, null, new HarmonyMethod(AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPartAttachedTranspiler))));
        }

        static IEnumerable<CodeInstruction> OnPartPickedTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            MethodInfo m_EditorLogic_SetBackup = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetBackup));
            MethodInfo m_ShipConstruct_Contains = AccessTools.Method(typeof(ShipConstruct), nameof(ShipConstruct.Contains), new[] { typeof(Part) });

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction il = code[i];
                if (il.opcode == OpCodes.Callvirt && ReferenceEquals(il.operand, m_ShipConstruct_Contains))
                {
                    yield return il;
                    Label label = ilGenerator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Dup); 
                    yield return new CodeInstruction(OpCodes.Brfalse_S, label);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, m_EditorLogic_SetBackup);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPickedMessage)));
                    CodeInstruction next = new CodeInstruction(OpCodes.Nop);
                    next.labels.Add(label);
                    yield return next;
                    continue;
                }

                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_SetBackup))
                {
                    il.opcode = OpCodes.Pop;
                    il.operand = null;
                }

                yield return il;
            }
        }

        static IEnumerable<CodeInstruction> OnPartAttachedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_EditorLogic_SetBackup = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetBackup));
            MethodInfo m_EditorLogic_attachPart = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.attachPart));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction il = code[i];
                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_attachPart))
                {
                    for (int j = i - 1; j-- > 0;)
                    {
                        if (code[j].opcode == OpCodes.Ldarg_0 && code[j + 1].opcode == OpCodes.Ldarg_0)
                        {
                            CodeInstruction callStart = code[j];
                            CodeInstruction newCallStart = new CodeInstruction(OpCodes.Ldarg_0);
                            int adds = 0;
                            code.Insert(j + adds++, newCallStart);
                            code.Insert(j + adds++, new CodeInstruction(OpCodes.Call, m_EditorLogic_SetBackup));
                            code.Insert(j + adds++, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnAttachedMessage))));
                            i += adds;

                            if (callStart.labels.Count > 0)
                            {
                                newCallStart.labels.AddRange(callStart.labels);
                                callStart.labels.Clear();
                            }

                            
                            break;
                        }
                    }
                }

                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_SetBackup))
                {
                    il.opcode = OpCodes.Pop;
                    il.operand = null;
                }
            }

            return code;
        }

        static void OnAttachedMessage() => Debug.Log("[UNDO/REDO] State captured before attaching");
        static void OnPickedMessage() => Debug.Log("[UNDO/REDO] State captured before detaching");
    }
}
