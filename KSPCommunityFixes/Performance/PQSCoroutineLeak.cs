// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/85

using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class PQSCoroutineLeak : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PQS), nameof(PQS.StartSphere)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PQS), nameof(PQS.ResetAndWait)),
                this));
        }

        // StartCoroutine(UpdateSphere());
        //IL_0391: ldarg.0
        //IL_0392: ldarg.0
        //IL_0393: call instance class [mscorlib] System.Collections.IEnumerator PQS::UpdateSphere()
        //IL_0398: dup
        //IL_0399: pop
        //IL_039a: call instance class [UnityEngine.CoreModule] UnityEngine.Coroutine[UnityEngine.CoreModule] UnityEngine.MonoBehaviour::StartCoroutine(class [mscorlib] System.Collections.IEnumerator)
        //IL_039f: dup
        //IL_03a0: pop

        static IEnumerable<CodeInstruction> PQS_StartSphere_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_UpdateSphere = AccessTools.Method(typeof(PQS), nameof(PQS.UpdateSphere));
            MethodInfo m_StartCoroutine_String = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new Type[] {typeof(string)});
            MethodInfo m_StartCoroutine_IEnumerator = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new Type[] {typeof(IEnumerator)});
            MethodInfo m_StopCoroutine_String = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), new Type[] { typeof(string) });

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = code.Count - 1; i >= 0; i--)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_StartCoroutine_IEnumerator))
                {
                    code[i].operand = m_StartCoroutine_String;
                    int j;
                    bool found = false;
                    for (j = i - 1; j >= i - 4; j--)
                    {
                        if (code[j].opcode == OpCodes.Call && ReferenceEquals(code[j].operand, m_UpdateSphere))
                        {
                            code[j].opcode = OpCodes.Ldstr;
                            code[j].operand = nameof(PQS.UpdateSphere);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                        throw new Exception("PQS.StartSphere transpiler patch failed, UpdateSphere() call not found");

                    if (code[j - 1].opcode == OpCodes.Ldarg_0 && code[j - 2].opcode == OpCodes.Ldarg_0)
                    {
                        int k = j - 1;
                        code.Insert(k, new CodeInstruction(OpCodes.Ldstr, nameof(PQS.UpdateSphere)));
                        code.Insert(k + 1, new CodeInstruction(OpCodes.Call, m_StopCoroutine_String));
                    }
                    else
                    {
                        throw new Exception("PQS.StartSphere transpiler patch failed, unexpected IL pattern");
                    }

                    break;
                }
            }

            return code;
        }

        static IEnumerable<CodeInstruction> PQS_ResetAndWait_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_UpdateSphere = AccessTools.Method(typeof(PQS), nameof(PQS.ResetAndWaitCoroutine));
            MethodInfo m_StartCoroutine_String = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new Type[] { typeof(string) });
            MethodInfo m_StartCoroutine_IEnumerator = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new Type[] { typeof(IEnumerator) });

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = code.Count - 1; i >= 0; i--)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_StartCoroutine_IEnumerator))
                {
                    code[i].operand = m_StartCoroutine_String;
                    int j;
                    bool found = false;
                    for (j = i - 1; j >= i - 4; j--)
                    {
                        if (code[j].opcode == OpCodes.Call && ReferenceEquals(code[j].operand, m_UpdateSphere))
                        {
                            code[j].opcode = OpCodes.Ldstr;
                            code[j].operand = nameof(PQS.ResetAndWaitCoroutine);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                        throw new Exception("PQS.ResetAndWait transpiler patch failed, UpdateSphere() call not found");

                    if (code[j - 1].opcode == OpCodes.Ldarg_0 && code[j - 2].opcode == OpCodes.Ldarg_0)
                        code[j - 2].opcode = OpCodes.Nop;
                    else
                        throw new Exception("PQS.ResetAndWait transpiler patch failed, unexpected IL pattern");

                    break;
                }
            }

            return code;
        }
    }
}
