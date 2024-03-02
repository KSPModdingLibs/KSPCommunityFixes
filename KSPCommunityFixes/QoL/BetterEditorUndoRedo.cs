// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/172

// In stock, undo state is captured after part events are complete, and undoing will restore that state captured before that.
// This make the user experience quite poor as undoing will loose all PAW tweaks made in between attach/detach actions.
// This patch invert the undo/redo state capture logic, by moving state capture before attaching / detaching instead of after, 
// and by capturing the current state when undoing is requested, in case a redo is requested next (see the RestoreState() patch)
// Unfortunately, the crew assignement (VesselCrewManifest) is updated based on the last serialized undo state, so doing this notably
// result in the crew assignement window being out of sync with the ship current state. To fix this, after attaching or detaching,
// we call a reimplementation of the VesselCrewManifest update using the live ship state instead of the serialized state.

// Still, due to many mostly unrelated code paths being triggered from the undo/redo code, this patch introduce a bunch of unavoidable
// behavior changes. We fix the most obvious, ie GameEvents.onEditorShipModified.Fire() being called before attach/detach, but
// this still introduce other more subtle changes, which might cause weird side effects in plugins overly relying on the notably
// messy editor code paths.

// enable additional debug logging
#define BEUR_DEBUG

// use replacement callbacks with modified stock code instead of stock code transpilers
// #define BEUR_REPLACE_CALLBACKS

using System;
using HarmonyLib;
using KSP.UI;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using EditorGizmos;
using KSP.UI.Screens;

namespace KSPCommunityFixes.QoL
{
    internal class BetterEditorUndoRedo : BasePatch
    {
        private static bool editorPatched = false;

        private static MethodInfo m_EditorLogic_SetBackup;
        private static MethodInfo m_EditorLogicSetBackupNoShipModifiedEvent;
        private static MethodInfo m_EditorShipModifiedGameEvent;
        private static MethodInfo m_EditorLogic_attachPart;
        private static MethodInfo m_EditorLogic_RefreshCrewAssignment;
        private static MethodInfo m_RefreshCrewAssignmentFromLiveState;
        private static MethodInfo m_ShipConstruct_Contains;
        private static MethodInfo m_Dummy_SetBackup;

        protected override Version VersionMin => new Version(1, 12, 3); // too many changes in previous versions, too lazy to check

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            m_EditorLogic_SetBackup = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetBackup));
            m_EditorLogicSetBackupNoShipModifiedEvent = AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(EditorLogicSetBackupNoShipModifiedEvent));
            m_EditorShipModifiedGameEvent = AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(EditorShipModifiedGameEvent));
            m_EditorLogic_attachPart = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.attachPart));
            m_EditorLogic_RefreshCrewAssignment = AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.RefreshCrewAssignment));
            m_RefreshCrewAssignmentFromLiveState = AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(RefreshCrewAssignmentFromLiveState));
            m_ShipConstruct_Contains = AccessTools.Method(typeof(ShipConstruct), nameof(ShipConstruct.Contains), new[] { typeof(Part) });
            m_Dummy_SetBackup = AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(Empty));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.RestoreState)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetupFSM)),
                this));

            // Create backup before moving a part with the rotate/offset tools, instead of after
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(GizmoOffset), nameof(GizmoOffset.OnHandleMoveStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(GizmoRotate), nameof(GizmoRotate.OnHandleRotateStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.onRotateGizmoUpdated)),
                this, nameof(RemoveSetBackupTranspiler)));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.onOffsetGizmoUpdated)),
                this, nameof(RemoveSetBackupTranspiler)));

            // Create backup before selecting a variant, instead of after

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UIPartActionVariantSelector), nameof(UIPartActionVariantSelector.SelectVariant)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModulePartVariants), nameof(ModulePartVariants.onVariantChanged)),
                this, nameof(RemoveSetBackupTranspiler)));

            // Move backup creation at the begining of the methods :

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(Part), nameof(Part.RemoveFromSymmetry)),
                this, nameof(MoveConditionalSetBackupTranspiler)));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EditorActionGroups), nameof(EditorActionGroups.ResetPart), new []{typeof(EditorActionPartSelector)}),
                this, nameof(MoveConditionalSetBackupTranspiler)));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EditorActionGroups), nameof(EditorActionGroups.AddActionToGroup)),
                this, nameof(MoveConditionalSetBackupTranspiler)));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(EditorActionGroups), nameof(EditorActionGroups.RemoveActionFromGroup)),
                this, nameof(MoveConditionalSetBackupTranspiler)));

#if BEUR_DEBUG
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SetBackup)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.RestoreState)),
                this));
#endif
        }

#if BEUR_DEBUG
        static void EditorLogic_SetBackup_Postfix(EditorLogic __instance)
        {
            if (__instance.ship.parts.Count == 0)
                return;

            Debug.Log($"[UNDO/REDO] backup created, undoLevel={__instance.undoLevel}, states={ShipConstruction.backups.Count}");
        }
#endif

#if BEUR_DEBUG
        static void EditorLogic_RestoreState_Postfix(EditorLogic __instance, int offset)
        {
            Debug.Log($"[UNDO/REDO] state {offset} restored, undoLevel={__instance.undoLevel}, states={ShipConstruction.backups.Count}");
        }
#endif

        static void EditorLogic_RestoreState_Prefix(EditorLogic __instance, int offset)
        {
            if (__instance.ship.parts.Count == 0 || offset >= 0 || __instance.undoLevel < ShipConstruction.backups.Count)
                return;

            __instance.SetBackup();

#if BEUR_DEBUG
            Debug.Log($"[UNDO/REDO] created backup for redo");
#endif
        }

        static void EditorLogic_SetupFSM_Postfix(EditorLogic __instance)
        {

#if BEUR_REPLACE_CALLBACKS
            __instance.on_partPicked.OnEvent = OnPartPickedReplacement;
            __instance.on_partAttached.OnEvent = OnPartAttachedReplacement;
#else
            if (editorPatched)
                return;

            editorPatched = true;

            MethodInfo m_onPartPicked = __instance.on_partPicked.OnEvent.Method; // <SetupFSM>b__189_21()
            MethodInfo m_onPartAttached = __instance.on_partAttached.OnEvent.Method; // <SetupFSM>b__189_29()

            KSPCommunityFixes.Harmony.Patch(m_onPartPicked, null, null, new HarmonyMethod(AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPartPickedTranspiler))));
            KSPCommunityFixes.Harmony.Patch(m_onPartAttached, null, null, new HarmonyMethod(AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPartAttachedTranspiler))));
#endif
        }

        static void Empty(EditorLogic elInstance) { }

        static IEnumerable<CodeInstruction> RemoveSetBackupTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(m_EditorLogic_SetBackup))
                    instruction.operand = m_Dummy_SetBackup;

                yield return instruction;
            }
        }

        static void GizmoOffset_OnHandleMoveStart_Prefix(GizmoOffset __instance)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            EditorLogic elInstance = EditorLogic.fetch;

            if (elInstance.gizmoOffset.RefNotEquals(__instance))
                return;

            if (!elInstance.ship.Contains(elInstance.selectedPart))
                return;

            elInstance.SetBackup();
        }

        static void GizmoRotate_OnHandleRotateStart_Prefix(GizmoRotate __instance)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            EditorLogic elInstance = EditorLogic.fetch;

            if (elInstance.gizmoRotate.RefNotEquals(__instance))
                return;

            if (!elInstance.ship.Contains(elInstance.selectedPart))
                return;

            elInstance.SetBackup();
        }

        static void UIPartActionVariantSelector_SelectVariant_Prefix(UIPartActionVariantSelector __instance)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            EditorLogic elInstance = EditorLogic.fetch;

            if (!elInstance.ship.Contains(__instance.part))
                return;

            elInstance.SetBackup();
        }

        static IEnumerable<CodeInstruction> MoveConditionalSetBackupTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen)
        {
            FieldInfo f_HighLogic_LoadedSceneIsEditor = AccessTools.Field(typeof(HighLogic), nameof(HighLogic.LoadedSceneIsEditor));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<CodeInstruction> movedCode = new List<CodeInstruction>();

            Label jmp = ilGen.DefineLabel();
            code[0].labels.Add(jmp);

            for (int i = code.Count; i-- > 0;)
            {
                if (code[i].Calls(m_EditorLogic_SetBackup))
                {
                    do
                    {
                        if (code[i].opcode == OpCodes.Brfalse_S)
                            code[i].operand = jmp;

                        movedCode.Insert(0, code[i]);
                        code.RemoveAt(i);
                        i--;
                    } 
                    while (!movedCode[0].LoadsField(f_HighLogic_LoadedSceneIsEditor));
                    
                    break;
                }
            }

            code.InsertRange(0, movedCode);
            return code;
        }

        /// <summary>
        /// Reimplementation of the EditorLogic.RefreshCrewAssignment() method using the live ship state
        /// instead of the last serialized ship state found at ShipConstruction.ShipManifest
        /// As a bonus, this is significantly faster...
        /// </summary>
        static void RefreshCrewAssignmentFromLiveState()
        {
            if (CrewAssignmentDialog.Instance == null)
                return;

            VesselCrewManifest oldVesselCrewManifest = ShipConstruction.ShipManifest;
            VesselCrewManifest newVesselCrewManifest = new VesselCrewManifest();

            List<Part> shipParts = EditorLogic.fetch.ship.parts;
            int shipPartsCount = shipParts.Count;
            for (int i = 0; i < shipPartsCount; i++)
            {
                Part part = shipParts[i];

                if (part.partInfo == null)
                    continue;

                PartCrewManifest partCrewManifest = new PartCrewManifest(newVesselCrewManifest);
                partCrewManifest.partInfo = part.partInfo;
                partCrewManifest.partID = part.craftID;

                int crewCapacity = partCrewManifest.partInfo.partPrefab.CrewCapacity;
                partCrewManifest.partCrew = new string[crewCapacity];
                for (int j = 0; j < crewCapacity; j++)
                    partCrewManifest.partCrew[j] = string.Empty;

                newVesselCrewManifest.SetPartManifest(partCrewManifest.PartID, partCrewManifest);
            }

            List<Part> allParts = Part.allParts;
            HashSet<uint> allPartIdsHashSet = null;
            int count = oldVesselCrewManifest.partManifests.Count;
            for (int i = 0; i < count; i++)
            {
                PartCrewManifest oldPartCrewManifest = oldVesselCrewManifest.partManifests[i];

                if (oldPartCrewManifest.partCrew.Length == 0)
                    continue;

                if (allPartIdsHashSet == null)
                {
                    allPartIdsHashSet = new HashSet<uint>(allParts.Count);
                    for (int j = allParts.Count; j-- > 0;)
                        allPartIdsHashSet.Add(allParts[j].craftID);
                }

                if (allPartIdsHashSet.Contains(oldPartCrewManifest.partID))
                    newVesselCrewManifest.UpdatePartManifest(oldPartCrewManifest.partID, oldPartCrewManifest);
            }

            ShipConstruction.ShipManifest = newVesselCrewManifest;
            CrewAssignmentDialog.Instance.RefreshCrewLists(newVesselCrewManifest, setAsDefault: false, updateUI: false);
            GameEvents.onEditorShipCrewModified.Fire(newVesselCrewManifest);
        }

        static void EditorLogicSetBackupNoShipModifiedEvent()
        {
            EditorLogic el = EditorLogic.fetch;

            if (el.ship.parts.Count == 0)
                return;

            if (el.undoLevel < ShipConstruction.backups.Count)
            {
                Debug.Log($"Clearing undo states from #{el.undoLevel} forward ({ShipConstruction.backups.Count - el.undoLevel} entries)");
                ShipConstruction.backups.RemoveRange(el.undoLevel, ShipConstruction.backups.Count - el.undoLevel);
            }

            el.ship.shipName = el.shipNameField.text;
            el.ship.shipDescription = el.shipDescriptionField.text;
            el.ship.missionFlag = EditorLogic.FlagURL;

            if (ShipConstruction.backups.Count >= el.undoLimit)
                ShipConstruction.ShiftAndCreateBackup(el.ship);
            else
                ShipConstruction.CreateBackup(el.ship);

            el.undoLevel = ShipConstruction.backups.Count;
            GameEvents.onEditorSetBackup.Fire(el.ship);
        }

        static void EditorShipModifiedGameEvent(EditorLogic editorLogic)
        {
            GameEvents.onEditorShipModified.Fire(editorLogic.ship);
        }

#if BEUR_DEBUG
        static void OnAttachedMessage() => Debug.Log("[UNDO/REDO] State captured before attaching");
        static void OnPickedMessage() => Debug.Log("[UNDO/REDO] State captured before detaching");
#endif

        static IEnumerable<CodeInstruction> OnPartPickedTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            foreach (CodeInstruction il in instructions)
            {
                if (il.opcode == OpCodes.Callvirt && ReferenceEquals(il.operand, m_ShipConstruct_Contains))
                {
                    yield return il;
                    Label label = ilGenerator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Dup); 
                    yield return new CodeInstruction(OpCodes.Brfalse_S, label);
                    yield return new CodeInstruction(OpCodes.Call, m_EditorLogicSetBackupNoShipModifiedEvent);
#if BEUR_DEBUG
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnPickedMessage)));
#endif
                    CodeInstruction next = new CodeInstruction(OpCodes.Nop);
                    next.labels.Add(label);
                    yield return next;
                    continue;
                }

                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_RefreshCrewAssignment))
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Call, m_RefreshCrewAssignmentFromLiveState);
                    continue;
                }

                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_SetBackup))
                {
                    il.operand = m_EditorShipModifiedGameEvent;
                }

                yield return il;
            }
        }

        static IEnumerable<CodeInstruction> OnPartAttachedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
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
                            CodeInstruction newCallStart = new CodeInstruction(OpCodes.Call, m_EditorLogicSetBackupNoShipModifiedEvent);
                            int adds = 0;
                            code.Insert(j + adds++, newCallStart);
#if BEUR_DEBUG
                            code.Insert(j + adds++, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BetterEditorUndoRedo), nameof(OnAttachedMessage))));
#endif
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
                    il.operand = m_EditorShipModifiedGameEvent;
                }

                if (il.opcode == OpCodes.Call && ReferenceEquals(il.operand, m_EditorLogic_RefreshCrewAssignment))
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Pop));
                    code.Insert(i, new CodeInstruction(OpCodes.Pop));
                    code.Insert(i, new CodeInstruction(OpCodes.Pop));
                    il.operand = m_RefreshCrewAssignmentFromLiveState;
                    i += 3;
                    continue;
                }
            }

            return code;
        }

#if BEUR_REPLACE_CALLBACKS
        static void OnPartPickedReplacement()
        {
            EditorLogic el = EditorLogic.fetch;

            if (el.selectedPart != el.selectedPart.localRoot)
            {
                bool pickedPartIsOnShip = el.ship.Contains(el.selectedPart);

                if (pickedPartIsOnShip) // added
                {
                    EditorLogicSetBackupNoShipModifiedEvent(); // added
#if BEUR_DEBUG
                    OnPickedMessage(); // added
#endif
                }

                el.detachPart(el.selectedPart);
                el.deleteSymmetryParts();

                if (pickedPartIsOnShip)
                {
                    GameEvents.onEditorPartPicked.Fire(el.selectedPart);
                    //el.SetBackup(); // removed
                    EditorShipModifiedGameEvent(el); // added
                    if (el.selectedPart.CrewCapacity > 0)
                    {
                        //el.RefreshCrewAssignment(ShipConstruction.ShipConfig, el.GetPartExistsFilter()); // removed
                        RefreshCrewAssignmentFromLiveState(); // added
                    }
                    GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartDetached, el.selectedPart);
                    return;
                }
            }
            else
            {
                el.SetBackup();
            }
            if (el.selectedPart.frozen)
            {
                el.selectedPart.unfreeze();
            }
            el.isCurrentPartFlag = el.selectedPart != null && el.selectedPart.GetComponent<FlagDecalBackground>() != null;
            if (el.selectedPart != null && el.selectedPart.FindModuleImplementing<ModuleCargoPart>() != null && UIPartActionControllerInventory.Instance != null)
            {
                UIPartActionControllerInventory.Instance.CurrentCargoPart = el.selectedPart;
            }
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartPicked, el.selectedPart);
        }

        static void OnPartAttachedReplacement()
        {
            EditorLogic el = EditorLogic.fetch;

            el.isCurrentPartFlag = false;
            if (el.selectedPart.symmetryCounterparts.Count > 0)
            {
                el.RestoreSymmetryState();
                bool flag = true;
                int num3 = el.cPartAttachments.Length;
                while (num3-- > 0)
                {
                    if (!el.cPartAttachments[num3].possible)
                    {
                        flag = false;
                        break;
                    }
                }
                if (!flag)
                {
                    el.audioSource.PlayOneShot(el.cannotPlaceClip);
                    el.on_partAttached.GoToStateOnEvent = el.st_place;
                    if (UIPartActionControllerInventory.Instance != null)
                    {
                        UIPartActionControllerInventory.Instance.DestroyHeldPartAsIcon();
                    }
                    return;
                }
                EditorLogicSetBackupNoShipModifiedEvent(); // added
#if BEUR_DEBUG
                OnAttachedMessage();  // added
#endif
                el.attachPart(el.selectedPart, el.attachment);
                el.attachSymParts(el.cPartAttachments);
            }
            else
            {
                EditorLogicSetBackupNoShipModifiedEvent(); // added
#if BEUR_DEBUG
                OnAttachedMessage();  // added
#endif
                el.attachPart(el.selectedPart, el.attachment);
                if (el.symmetryModeBeforeNodeAttachment >= 0)
                {
                    el.RestoreSymmetryModeBeforeNodeAttachment();
                }
            }

            //el.SetBackup(); // removed
            EditorShipModifiedGameEvent(el); // added

            if (el.selectedPart.CrewCapacity > 0)
            {
                //el.RefreshCrewAssignment(ShipConstruction.ShipConfig, el.GetPartExistsFilter()); // removed
                RefreshCrewAssignmentFromLiveState(); // added
            }

            ModuleCargoPart moduleCargoPart = el.selectedPart.FindModuleImplementing<ModuleCargoPart>();
            if (UIPartActionControllerInventory.Instance != null)
            {
                if (moduleCargoPart != null && !moduleCargoPart.IsDeployedSciencePart())
                {
                    UIPartActionControllerInventory.Instance.CurrentCargoPart = null;
                    UIPartActionControllerInventory.Instance.CurrentInventory = null;
                }
                UIPartActionControllerInventory.Instance.DestroyHeldPartAsIcon();
            }
            el.audioSource.PlayOneShot(el.attachClip);
            el.on_partAttached.GoToStateOnEvent = el.st_idle;
            el.CenterDragPlane(el.selectedPart.transform.position + el.selPartGrabOffset);
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartAttached, el.selectedPart);
        }
#endif
    }
}
