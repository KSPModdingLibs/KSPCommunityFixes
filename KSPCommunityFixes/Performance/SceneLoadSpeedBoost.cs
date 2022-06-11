using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Debug = UnityEngine.Debug;
using Version = System.Version;

namespace KSPCommunityFixes
{
    public class SceneLoadSpeedBoost : BasePatch
    {
        private const string VALUENAME_TIMESTAMP = "persistentTimestamp";
        private static string lastPersistentSerializedTimestamp;
        private static ConfigNode lastPersistent;

        private static readonly Stopwatch watch = new Stopwatch();

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(GamePersistence), "SaveGame", new Type[] { typeof(Game), typeof(string), typeof(string), typeof(SaveMode) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(GamePersistence), "LoadGame", new Type[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }),
                this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(GamePersistence), "SaveGame", new Type[] {typeof(Game), typeof(string), typeof(string), typeof(SaveMode)}),
            //    this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(GamePersistence), "LoadGame", new Type[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }),
            //    this));
        }

        static IEnumerable<CodeInstruction> GamePersistence_SaveGame_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo ConfigNode_Save = AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.Save), new Type[]{typeof(string)});
            MethodInfo CachePersistentSFS = AccessTools.Method(typeof(SceneLoadSpeedBoost), nameof(SceneLoadSpeedBoost.CachePersistentSFS));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            //IL_02dd: call string[mscorlib] System.String::Concat(string[])
            //IL_02e2: stloc.1
            //// CachePersistentSFS(saveFileName, saveFolder, configNode);
            //IL_02e3: ldarg.1
            //IL_02e4: ldarg.2
            //IL_02e5: ldloc.0
            //IL_02e6: call void KSPCommunityFixes.BugFixes.SceneLoadSpeedBoost::CachePersistentSFS(string, string, class ['Assembly-CSharp'] ConfigNode)
            //// configNode.Save(fileFullName);
            //IL_02eb: ldloc.0
            //IL_02ec: ldloc.1
            //IL_02ed: callvirt instance bool['Assembly-CSharp'] ConfigNode::Save(string)

            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, ConfigNode_Save))
                {
                    int k = i;
                    code.Insert(k++, new CodeInstruction(OpCodes.Stloc_1));
                    code.Insert(k++, new CodeInstruction(OpCodes.Ldarg_1));
                    code.Insert(k++, new CodeInstruction(OpCodes.Ldarg_2));
                    code.Insert(k++, new CodeInstruction(OpCodes.Ldloc_0));
                    code.Insert(k++, new CodeInstruction(OpCodes.Call, CachePersistentSFS));
                    code.Insert(k++, new CodeInstruction(OpCodes.Ldloc_0));
                    code.Insert(k++, new CodeInstruction(OpCodes.Ldloc_1));
                    break;
                }
            }

            return code;
        }

        static IEnumerable<CodeInstruction> GamePersistence_LoadGame_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo GamePersistence_LoadSFSFile = AccessTools.Method(typeof(GamePersistence), nameof(GamePersistence.LoadSFSFile));
            MethodInfo LoadSFSFileCached = AccessTools.Method(typeof(SceneLoadSpeedBoost), nameof(SceneLoadSpeedBoost.LoadSFSFileCached));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            //// ConfigNode configNode = LoadSFSFile(filename, saveFolder);
            //IL_0000: ldarg.0
            //IL_0001: ldarg.1
            //IL_0002: call class ConfigNode GamePersistence::LoadSFSFile(string, string)
            //IL_0007: stloc.0

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, GamePersistence_LoadSFSFile))
                {
                    code[i].operand = LoadSFSFileCached;
                    break;
                }
            }

            return code;
        }

        private static void CachePersistentSFS(string saveFileName, string saveFolder, ConfigNode configNode)
        {
            lastPersistent = null;
            lastPersistentSerializedTimestamp = null;

            if (configNode.nodes.Count != 0)
            {
                ConfigNode gameNode = configNode.nodes[0];

                if (gameNode.name == "GAME" && saveFileName == "persistent" && saveFolder == HighLogic.SaveFolder)
                {
                    lastPersistent = configNode;
                    lastPersistentSerializedTimestamp = DateTime.UtcNow.ToString("O");

                    gameNode.SetValue(VALUENAME_TIMESTAMP, lastPersistentSerializedTimestamp, true);
#if DEBUG
                    Debug.Log($"[SceneLoadSpeedBoost] Persistent save cached\n{new StackTrace()}");
#endif
                }
            }
        }

        private static ConfigNode LoadSFSFileCached(string filename, string saveFolder)
        {
            ConfigNode persistent;
            bool canUseCache = lastPersistent != null && lastPersistentSerializedTimestamp != null && filename == "persistent" && saveFolder == HighLogic.SaveFolder;

            if (canUseCache)
            {
                try
                {
                    watch.Restart();

                    canUseCache = false;
                    string path = KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/" + filename + ".sfs";

                    using (StreamReader reader = new StreamReader(path))
                    {
                        string line;
                        int nodeCount = 0;
                        int lineCount = 0;

                        while ((line = reader.ReadLine()) != null)
                        {
                            lineCount++;
                            if (line.Contains("{"))
                            {
                                nodeCount++;
                            }

                            if (nodeCount == 1)
                            {
                                if (!line.Contains(VALUENAME_TIMESTAMP))
                                    continue;

                                string[] keyValue = line.Split('=');
                                if (keyValue.Length != 2)
                                    break;

                                canUseCache = lastPersistentSerializedTimestamp == keyValue[1].Trim();
                                break;
                            }

                            if (nodeCount > 1 || lineCount > 100)
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SceneLoadSpeedBoost] Error reloading cached persistent.sfs\n{e}");
                    canUseCache = false;
                }
                finally
                {
                    watch.Stop();
                }
            }

            if (canUseCache)
            {
                persistent = lastPersistent;
                Debug.Log($"[SceneLoadSpeedBoost] Save loaded from cache in {watch.ElapsedMilliseconds}ms");
            }
            else
            {
                watch.Start();
                persistent = GamePersistence.LoadSFSFile(filename, saveFolder);
                watch.Stop();
                Debug.Log($"[SceneLoadSpeedBoost] Save loaded from disk in {watch.ElapsedMilliseconds}ms");
            }

            lastPersistent = null;
            lastPersistentSerializedTimestamp = null;

            return persistent;
        }

#if DEBUG

        static bool GamePersistence_SaveGame_Prefix(Game game, string saveFileName, string saveFolder, SaveMode saveMode, ref string __result)
        {
            lastPersistent = null;
            if (saveFileName == "persistent" && !game.Parameters.Flight.CanAutoSave)
            {
                Debug.LogWarning("[GamePersistence]: Saving to persistent.sfs is disabled in FLIGHT scenario options (disabled autosave)");
                __result = string.Empty;
                return false;
            }
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            foreach (char oldChar in invalidFileNameChars)
            {
                saveFileName = saveFileName.Replace(oldChar, '_');
            }
            if (!Directory.Exists(KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/"))
            {
                Directory.CreateDirectory(KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/");
            }
            switch (saveMode)
            {
                case SaveMode.APPEND:
                    {
                        int num2 = 0;
                        string text4;
                        do
                        {
                            text4 = saveFileName + num2++.ToString("000");
                        }
                        while (File.Exists(KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/" + text4 + ".sfs"));
                        saveFileName = text4;
                        break;
                    }
                case SaveMode.ABORT:
                    if (File.Exists(KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/" + saveFileName + ".sfs"))
                    {
                        Debug.LogWarning("Save Aborted! File already exists!");
                        __result = string.Empty;
                        return false;
                    }
                    break;
                case SaveMode.BACKUP:
                    {
                        string text = KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/";
                        string text2 = text + "/Backup/";
                        string text3 = text + saveFileName + ".sfs";

                        DateTime dateTime;
                        try
                        {
                            dateTime = DateTime.Now;
                        }
                        catch (Exception)
                        {
                            dateTime = DateTime.UtcNow;
                        }

                        string destFileName = text2 + saveFileName + " (" + dateTime.ToString("yyyy_MM_dd_HH_mm_ss") + ").sfs";
                        if (GameSettings.SAVE_BACKUPS <= 0)
                        {
                            if (Directory.Exists(text2))
                            {
                                Directory.Delete(text2, recursive: true);
                            }
                        }
                        else
                        {
                            if (!File.Exists(text3))
                            {
                                break;
                            }
                            if (!Directory.Exists(text2))
                            {
                                Directory.CreateDirectory(text2);
                                File.Copy(text3, destFileName, overwrite: true);
                                break;
                            }
                            List<FileInfo> list = new List<FileInfo>(new DirectoryInfo(text2).GetFiles("*.sfs"));
                            list.Sort(SaveCompareToDate);
                            if (list.Count < GameSettings.SAVE_BACKUPS)
                            {
                                File.Copy(text3, destFileName, overwrite: true);
                                break;
                            }
                            File.Copy(text3, destFileName, overwrite: true);
                            int num = list.Count + 1 - GameSettings.SAVE_BACKUPS;
                            for (int j = 0; j < num; j++)
                            {
                                list[j].Delete();
                            }
                        }
                        break;
                    }
            }
            ConfigNode configNode = new ConfigNode();
            game.Save(configNode);
            
            string path = KSPUtil.ApplicationRootPath + "saves/" + saveFolder + "/" + saveFileName + ".sfs";

            CachePersistentSFS(saveFileName, saveFolder, configNode);

            configNode.Save(path);
            Debug.Log("Game State Saved to saves/" + saveFolder + "/" + saveFileName);

            try
            {
                //new LoadGameDialog.PlayerProfileInfo(saveFileName, saveFolder, game).SaveToMetaFile(saveFileName, saveFolder);
                Type playerProfileInfoType = AccessTools.TypeByName("LoadGameDialog+PlayerProfileInfo");
                object playerProfileInfo = Activator.CreateInstance(playerProfileInfoType, new object[] {saveFileName, saveFolder, game});
                AccessTools.Method(playerProfileInfoType, "SaveToMetaFile").Invoke(playerProfileInfo, new object[] {saveFileName, saveFolder});
                __result = saveFileName;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Unable to save .loadmeta for filename\n" + ex.Message);
                __result = saveFileName;
                return false;
            }
        }

        private static int SaveCompareToDate(FileInfo a, FileInfo b)
        {
            try
            {
                return a.LastWriteTime.CompareTo(b.LastWriteTime);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static bool GamePersistence_LoadGame_Prefix(string filename, string saveFolder, bool nullIfIncompatible, bool suppressIncompatibleMessage, ref Game __result)
        {
            ConfigNode configNode = LoadSFSFileCached(filename, saveFolder);

            // FlightGlobals.ClearpersistentIdDictionaries();
            AccessTools.Method(typeof(FlightGlobals), "ClearpersistentIdDictionaries").Invoke(null, null);

            if (configNode != null)
            {
                __result = GamePersistence.LoadGameCfg(configNode, filename, nullIfIncompatible, suppressIncompatibleMessage);
                return false;
            }

            __result = null;
            return false;
        }
#endif
    }
}

/* scene switches stacktraces :





*/