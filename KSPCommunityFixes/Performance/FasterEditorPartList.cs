/* 
The part list implements a caching mechanism to avoid re-instantiating already generated icons, which avoid the bulk of the cost of swapping the shown parts
in the part list. Once all icons were generated at least once, it just re-parent them between a disabled `partIconStorage` GameObject and the scrollview GameObject. 
Profiling show that roughly 70-80% of the time is spent doing this reparenting, which we avoid by :
- Preventing the icon objects from being parented to the `partIconStorage` in the EditorPartList.ClearAllItems() method (by removing the SetParent call)
- Preventing the icon objects from being parented back to the scrollview in the EditorPartList.UpdatePartIcon() method (by removing the SetParent call)
Doing this cause two issues : 
- It prevent the icon object from ever being parented to the scrollist, so they never appear
- The icons are not ordered anymore according to the sorting filter, as this was done by unparenting / reparenting them
We fix that by altering EditorPartList.UpdatePartIcons() in two ways :
- We call SetParent for newly instantiated icons so they are parented to the scrollview
- We set the already instantiated icons that are about to be re-activated to be the last sibling to respect the requested sorting.
Note that we do both *before* the call to EditorPartList.UpdatePartIcon(), as VABOrganizer is postfixing it and is relying on the icons being 
in their right place within the scrollview at this time.

This significantlly improve overall responsivness of the part list updates when switching between categories, changing the sorting or using the tag search
*/

using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static KSP.UI.Screens.BasePartCategorizer;

namespace KSPCommunityFixes.Performance
{
    internal class FasterEditorPartList : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(EditorPartList), nameof(EditorPartList.ClearAllItems), nameof(NoOpSetParentTranspiler));
            AddPatch(PatchType.Transpiler, typeof(EditorPartList), nameof(EditorPartList.UpdatePartIcon), nameof(NoOpSetParentTranspiler));
            AddPatch(PatchType.Transpiler, typeof(EditorPartList), nameof(EditorPartList.UpdatePartIcons));
            AddPatch(PatchType.Override, typeof(BasePartCategorizer), nameof(BasePartCategorizer.PartMatchesSearch));
        }

        private static void TransformSetParentNoOp(Transform instance, Transform parent, bool worldPositionStays) { }

        private static IEnumerable<CodeInstruction> NoOpSetParentTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_Transform_SetParent = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new Type[] { typeof(Transform), typeof(bool) });
            MethodInfo m_Transform_SetParentNoOp = AccessTools.Method(typeof(FasterEditorPartList), nameof(TransformSetParentNoOp));

            foreach (CodeInstruction il in instructions)
            {
                if ((il.opcode == OpCodes.Callvirt || il.opcode == OpCodes.Call) && ReferenceEquals(il.operand, m_Transform_SetParent))
                {
                    il.opcode = OpCodes.Call;
                    il.operand = m_Transform_SetParentNoOp;
                }
                yield return il;
            }
        }

        private static void SetIconParentHelper(EditorPartList editorPartList, EditorPartIcon editorPartIcon)
        {
            editorPartIcon.transform.SetParent(editorPartList.partGrid.transform, worldPositionStays: false);
        }

        private static void SetIconAsLastSiblingHelper(EditorPartIcon editorPartIcon)
        {
            editorPartIcon.transform.SetAsLastSibling();
        }

        private static IEnumerable<CodeInstruction> EditorPartList_UpdatePartIcons_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_EditorPartIcon_Create = AccessTools.Method(typeof(EditorPartIcon), nameof(EditorPartIcon.Create), 
                new Type[] { typeof(EditorPartList), typeof(AvailablePart), typeof(float), typeof(float), typeof(float) });
            MethodInfo m_SetIconParentHelper = AccessTools.Method(typeof(FasterEditorPartList), nameof(SetIconParentHelper));
            MethodInfo m_SetIconAsLastSiblingHelper = AccessTools.Method(typeof(FasterEditorPartList), nameof(SetIconAsLastSiblingHelper));

            foreach (CodeInstruction il in instructions)
            {
                if ((il.opcode == OpCodes.Callvirt || il.opcode == OpCodes.Call) && ReferenceEquals(il.operand, m_EditorPartIcon_Create))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldloc_2); // first EditorPartIcon local var
                    yield return new CodeInstruction(OpCodes.Call, m_SetIconParentHelper);
                    yield return il;
                    continue;
                }

                if (il.opcode == OpCodes.Stloc_3)
                {
                    yield return il;
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // second EditorPartIcon local var
                    yield return new CodeInstruction(OpCodes.Call, m_SetIconAsLastSiblingHelper);
                    continue;
                }

                yield return il;
            }
        }

        private static Dictionary<AvailablePart, PartTag[]> partsTags = new Dictionary<AvailablePart, PartTag[]>();

        private class PartTag
        {
            public readonly string tag;
            public readonly MatchType matchType;

            public PartTag(string tag, MatchType matchType)
            {
                this.tag = tag;
                this.matchType = matchType;
            }
        }

        /// <summary>
        /// Improve BasePartCategorizer.PartMatchesSearch() performance by building a static dictionary of pre-parsed tags 
        /// for every AvailablePart instead of re-parsing the whole AvailablePart.tags string every time.
        /// </summary>
        private static bool BasePartCategorizer_PartMatchesSearch_Override(BasePartCategorizer instance, AvailablePart part, string[] terms)
        {
            if (part.category == PartCategories.none)
                return false;

            if (terms.Length == 0)
                return true;

            if (!partsTags.TryGetValue(part, out PartTag[] tags))
            {
                string[] rawTags = SearchTagSplit(part.tags);
                tags = new PartTag[rawTags.Length];
                for (int i = 0; i < rawTags.Length; i++)
                {
                    string rawTag = rawTags[i];
                    MatchType matchType = instance.TagMatchType(ref rawTag);
                    tags[i] = new PartTag(rawTag, matchType);
                }

                partsTags.Add(part, tags);
            }

            for (int i = terms.Length; i-- > 0;)
            {
                string term = terms[i];
                int termLength = Math.Min(term.Length, 3);

                for (int j = tags.Length; j-- > 0;)
                {
                    string tag = tags[j].tag;
                    if (tag.Length < termLength)
                        continue;

                    bool match;
                    switch (tags[j].matchType)
                    {
                        case MatchType.TERM_CONTAINS_TAG:
                            match = term.Contains(tag);
                            break;
                        case MatchType.TAG_CONTAINS_TERM:
                            match = tag.Contains(term);
                            break;
                        case MatchType.EQUALS_ONLY:
                            match = term.Equals(tag);
                            break;
                        case MatchType.EITHER_ENDS_WITH_EITHER:
                            match = term.EndsWith(tag) || tag.EndsWith(term);
                            break;
                        case MatchType.EITHER_STARTS_WITH_EITHER:
                            match = term.StartsWith(tag) || tag.StartsWith(term);
                            break;
                        case MatchType.TERM_ENDS_WITH_TAG:
                            match = term.EndsWith(tag);
                            break;
                        case MatchType.TERM_STARTS_WITH_TAG:
                            match = term.StartsWith(tag);
                            break;
                        case MatchType.TAG_ENDS_WITH_TERM:
                            match = tag.EndsWith(term);
                            break;
                        case MatchType.TAG_STARTS_WITH_TERM:
                            match = tag.StartsWith(term);
                            break;
                        case MatchType.EITHER_CONTAINS_EITHER:
                        default:
                            match = term.Contains(tag) || tag.Contains(term);
                            break;
                    }

                    if (match)
                        return true;
                }
            }

            return false;
        }
    }
}
