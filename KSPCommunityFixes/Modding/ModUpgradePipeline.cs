using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using SaveUpgradePipeline;
using UniLinq;

namespace KSPCommunityFixes.Modding
{
    class ModUpgradePipeline : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        private static string _versionString;
        private static readonly Dictionary<string, Version> _versionsLoadedString = new Dictionary<string, Version>();
        private static readonly Dictionary<Assembly, Version> _versionsLoaded = new Dictionary<Assembly, Version>();
        private static readonly Dictionary<Assembly, Version> _versionsCurrent = new Dictionary<Assembly, Version>();
        private static readonly Dictionary<Assembly, Version> _versionsTemp = new Dictionary<Assembly, Version>();
        private static readonly Dictionary<UpgradeScript, Type> _scriptToType = new Dictionary<UpgradeScript, Type>();
        private static readonly Version _EmptyVersion = new Version(0, 0, 0, 0);
        private static Assembly _currentAsm = null;
        private static readonly Assembly _StockAssembly = typeof(UpgradeScript).Assembly;

        // Because the callback is compiler-generated, the callback and the event have the same name.
        // That means we can't get the callback directly, so we have to get it by reflection.
        private static FieldInfo OnSetCfgNodeVersionCallback = typeof(SaveUpgradePipeline.SaveUpgradePipeline).GetField(nameof(SaveUpgradePipeline.SaveUpgradePipeline.OnSetCfgNodeVersion), AccessTools.all);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ShipConstruct), nameof(ShipConstruct.SaveShip)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Game), nameof(Game.Save)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(KSPUpgradePipeline), nameof(KSPUpgradePipeline.Process)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(SaveUpgradePipeline.SaveUpgradePipeline), nameof(SaveUpgradePipeline.SaveUpgradePipeline.Init)),
                this, "SaveUpgradePipeline_Init_Postfix"));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(SaveUpgradePipeline.SaveUpgradePipeline), nameof(SaveUpgradePipeline.SaveUpgradePipeline.SanityCheck)),
                this, "SaveUpgradePipeline_SanityCheck_Prefix"));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(SaveUpgradePipeline.SaveUpgradePipeline), nameof(SaveUpgradePipeline.SaveUpgradePipeline.RunIteration)),
                this, "SaveUpgradePipeline_RunIteration_Prefix"));

            // For some reason this method is failing to be found normally.
            // So we'll find it manually.
            MethodBase runMethod = null;
            foreach (var m in typeof(SaveUpgradePipeline.SaveUpgradePipeline).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name == "Run")
                {
                    runMethod = m;
                    break;
                }
            }
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                runMethod,
                this, "SaveUpgradePipeline_Run_Postfix"));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UpgradeScript), nameof(UpgradeScript.Test)),
                this, "UpgradeScript_Test_Prefix"));

            SaveCurrentVersions();

            // Find event field, again have to do this manually??
            //foreach (FieldInfo fi in typeof(SaveUpgradePipeline.SaveUpgradePipeline).GetFields(AccessTools.all))
            //{
            //    if (fi.Name == "OnSetCfgNodeVersion")
            //    {
            //        OnSetCfgNodeVersionCallback = fi;
            //        break;
            //    }
            //}
        }

        private static void SaveCurrentVersions()
        {
            var sb = StringBuilderCache.Acquire();
            int aCount = 0;

            foreach (var assembly in AssemblyLoader.loadedAssemblies)
            {
                var asm = assembly.assembly;
                if (asm == _StockAssembly)
                    continue;

                var asmV = asm.GetName().Version;
                int vMajor = asmV.Major;
                int vMinor = asmV.Minor;
                int vBuild = asmV.Build;
                if (vBuild < 0)
                    vBuild = 0;

                // We prefer AssemblyFileVersion to AssemblyVersion
                var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                string fv = fileVersionInfo.FileVersion;
                if (!string.IsNullOrWhiteSpace(fv) && fv != asmV.ToString())
                {
                    var fvSplit = fv.Split('.');
                    if (fvSplit.Length > 1) // ignore bogus versions
                    {
                        int tmp;
                        if (int.TryParse(fvSplit[0], out tmp))
                        {
                            vMajor = tmp;
                            if (int.TryParse(fvSplit[1], out tmp))
                            {
                                vMinor = tmp;
                                tmp = 0;
                                if (fvSplit.Length > 2 && !int.TryParse(fvSplit[2], out tmp))
                                {
                                    List<char> chars = new List<char>();
                                    tmp = 0;
                                    for (int i = 0; i < fvSplit[2].Length; ++i)
                                    {
                                        if (char.IsDigit(fvSplit[2][i]))
                                        {
                                            tmp *= 10;
                                            tmp += (int)char.GetNumericValue(fvSplit[2][i]);
                                        }
                                    }
                                }
                                vBuild = tmp;
                            }
                        }
                    }
                }
                if (aCount++ > 0)
                    sb.Append("|");
                sb.Append(asm.GetName().Name);
                sb.Append("=");
                sb.Append(vMajor);
                sb.Append(".");
                sb.Append(vMinor);
                sb.Append(".");
                sb.Append(vBuild);
                _versionsCurrent[asm] = new Version(vMajor, vMinor, vBuild);
            }
            _versionString = sb.ToStringAndRelease();
        }

        private static Version GetVersion(Assembly asm)
        {
            if (_versionsLoaded.TryGetValue(asm, out Version v))
                return v;

            if (!_versionsLoadedString.TryGetValue(asm.GetName().Name, out v))
                v = _EmptyVersion;

            _versionsLoaded[asm] = v;
            return v;
        }

        private static void AddVersions(ConfigNode node, LoadContext loadContext)
        {
            var oldDoClean = Performance.ConfigNodePerf._doClean;
            Performance.ConfigNodePerf._doClean = false;
            if (loadContext == LoadContext.SFS)
                node.GetNode("GAME").SetValue("_modVersions", _versionString, true);
            else
                node.SetValue("_modVersions", _versionString, true);
            Performance.ConfigNodePerf._doClean = oldDoClean;
        }

        private static void ShipConstruct_SaveShip_Postfix(ref ConfigNode __result)
        {
            //UnityEngine.Debug.Log("$$ Saving versions to craft file");
            AddVersions(__result, LoadContext.Craft);
        }

        private static void Game_Save_Postfix(ConfigNode rootNode)
        {
            //UnityEngine.Debug.Log("$$ Saving versions to sfs file");
            AddVersions(rootNode, LoadContext.SFS);
        }

        private static bool TryLoadVersions(ConfigNode n, LoadContext loadContext)
        {
            _versionsLoadedString.Clear();
            _versionsLoaded.Clear();

            if (n != null)
                return false;

            string versionStr;
            if (loadContext == LoadContext.SFS)
                versionStr = n.GetNode("GAME")?.GetValue("_modVersions");
            else
                versionStr = n.GetValue("_modVersions");

            if (versionStr == null)
                return false;

            var allSplit = versionStr.Split('|');
            foreach (var s in allSplit)
            {
                //UnityEngine.Debug.Log("$$ Found version string " + s);
                var kvp = s.Split('=');
                if (kvp.Length == 2)
                {
                    Version v = new Version(kvp[1]);
                    _versionsLoadedString[kvp[0]] = v;
                }
            }
            //UnityEngine.Debug.Log($"$$ Loaded {allSplit.Length} mod versions");
            return true;   
        }

        private static void KSPUpgradePipeline_Process_Prefix(ConfigNode n, LoadContext loadContext)
        {
            TryLoadVersions(n, loadContext);
            //UnityEngine.Debug.Log("$$ Ready to process.");
        }

        private static void SaveUpgradePipeline_Init_Postfix(SaveUpgradePipeline.SaveUpgradePipeline __instance)
        {
            _scriptToType.Clear();
            foreach (var uSc in __instance.upgradeScripts)
            {
                Type t = uSc.GetType();
                if (t.Assembly != _StockAssembly)
                    _scriptToType[uSc] = t;
            }
        }

        private static bool SaveUpgradePipeline_SanityCheck_Prefix(UpgradeScript uSC, Version AppVersion, out bool __result)
        {
            if (uSC.TargetVersion <= uSC.EarliestCompatibleVersion)
            {
                UnityEngine.Debug.LogError("[SaveUpgradePipeline]: A script's target version should never be LEqual to its earliest-compat version. " + uSC.Name + " will be skipped.");
                __result = false;
            }
            else
            {
                _scriptToType.TryGetValue(uSC, out var usType);
                Version v = AppVersion;
                bool isStock = usType == null;
                if (!isStock)
                {
                    v = GetVersion(usType.Assembly);
                }
                if (v != _EmptyVersion && (uSC.TargetVersion > v || uSC.EarliestCompatibleVersion > v))
                {
                    UnityEngine.Debug.LogError("[SaveUpgradePipeline]: A script's versions should never exceed the current " + (isStock ? "application" : "mod") + " version. " + uSC.Name + " will be skipped.");
                    __result = false;
                }
                else
                {
                    __result = true;
                }
            }
            return false;
        }

        private static void SetAssembly(UpgradeScript uSc)
        {
            // Set the current assembly for use in overriding version,
            // if it's not a stock type
            _scriptToType.TryGetValue(uSc, out var type);
            if (type != null)
                _currentAsm = type.Assembly;
        }

        private static bool SaveUpgradePipeline_RunIteration_Prefix(SaveUpgradePipeline.SaveUpgradePipeline __instance, ConfigNode srcNode, ref ConfigNode node, LoadContext ctx, List<UpgradeScript> scripts, List<Dictionary<UpgradeScript, LogEntry>> log, out IterationResult __result)
        {
            Dictionary<UpgradeScript, LogEntry> lastRow = ((log.Count > 0) ? log[log.Count - 1] : null);
            Dictionary<UpgradeScript, LogEntry> row = new Dictionary<UpgradeScript, LogEntry>();
            log.Add(row);
            ConfigNode curNode = node ?? srcNode;
            for(int i = scripts.Count; i-- > 0;)
            {
                // Change: set assembly so VersionTest will use the right version
                var uSc = scripts[i];
                SetAssembly(uSc);
                var testResult = __instance.RunTest(uSc, curNode, ctx);
                _currentAsm = null;
                row.Add(uSc, new LogEntry(testResult, upgraded: false));
            }

            if (row.Values.All((LogEntry r) => r.testResult == TestResult.Pass))
            {
                __result = IterationResult.Pass;
                return false;
            }
            if (row.Values.All((LogEntry r) => r.testResult == TestResult.TooEarly))
            {
                __result = IterationResult.Fail;
                return false;
            }
            if (!SaveUpgradePipeline.SaveUpgradePipeline.TestExceptionCases(log))
            {
                __result = IterationResult.Fail;
                return false;
            }
            if (node == null)
            {
                //UnityEngine.Debug.Log("$$ Creating copy of node");
                node = srcNode.CreateCopy();
            }
            for(int i = scripts.Count; i-- > 0;)
            {
                if (row[scripts[i]].testResult == TestResult.Upgradeable)
                {
                    if (lastRow != null && lastRow[scripts[i]].upgraded)
                    {
                        row[scripts[i]].testResult = TestResult.Pass;
                        row[scripts[i]].upgraded = true;
                    }
                    else
                    {
                        // Change: Set assembly just in case (not used yet)
                        var uSc = scripts[i];
                        SetAssembly(uSc);
                        node = __instance.RunUpgrade(uSc, node, ctx);
                        _currentAsm = null;
                        row[uSc].upgraded = true;
                    }
                }
            }
            // Change: we have to handle stock pipelines and mod pipelines differently.
            __instance.lowestVersion = new Version(int.MaxValue, int.MaxValue, int.MaxValue);
            bool foundStock = false; // keep track of whether we need to set the game version
            UpgradeScript[] currentUpgrades = row.Keys.Where((UpgradeScript usc) => row[usc].testResult == TestResult.Upgradeable || row[usc].testResult == TestResult.TooEarly || row[usc].upgraded).ToArray();
            _versionsTemp.Clear();
            for(int i = currentUpgrades.Length; i-- > 0;)
            {
                // Branch based on whether it's a stock script or a mod script.
                var uSc = currentUpgrades[i];
                if (_scriptToType.TryGetValue(uSc, out var t))
                {
                    // if it's a mod script, get the current-lowest version (it will fail to find
                    // if we haven't set one yet). If it's higher than this version, or doesn't
                    // exist yet, set lowest version to this.
                    Version version = row[uSc].testResult == TestResult.TooEarly ? uSc.EarliestCompatibleVersion : uSc.TargetVersion;
                    if (!_versionsTemp.TryGetValue(t.Assembly, out var lowest) || version < lowest)
                        _versionsTemp[t.Assembly] = version;
                }
                else
                {
                    // Unchanged stock code, except we set a flag to tell us we need
                    // to set the stock cfg version.
                    Version version = row[uSc].testResult == TestResult.TooEarly ? uSc.EarliestCompatibleVersion : uSc.TargetVersion;
                    if (version < __instance.lowestVersion)
                    {
                        __instance.lowestVersion = version;
                        foundStock = true;
                    }
                }
            }

            // if we found a stock script, we need to set the cfg version.
            if (foundStock)
            {
                // We can't directly call the event. This is gross.
                // Further, we're going to reget the field now, because there's no guarantee
                // that nobody else has added to the callback after this class was instantiated.
                Callback<ConfigNode, LoadContext, Version> callback = (Callback<ConfigNode, LoadContext, Version>)OnSetCfgNodeVersionCallback.GetValue(__instance);
                callback(node, ctx, __instance.lowestVersion);
            }

            // If we found mod scripts that need to run again, update their mods' versions here.
            foreach (var kvp in _versionsTemp)
            {
                _versionsLoaded[kvp.Key] = kvp.Value;
                // We don't need the safe approach, because we know (a)
                // that we ran the script, so we ran SetAssembly so we put
                // a kvp in _versionsLoaded, and (b) we know the new version
                // is higher than the existing version because if it weren't
                // the script would not have run.
                //if (_versionsLoaded.TryGetValue(kvp.Key, out var v) && v < kvp.Value)
                //{
                //    _versionsLoaded[kvp.Key] = kvp.Value;
                //}
            }

            __result = IterationResult.Continue;
            return false;
        }

        private static void SaveUpgradePipeline_Run_Postfix(ConfigNode node, LoadContext ctx, ref ConfigNode __result)
        {
            // Only do this for craft.
            if (ctx != LoadContext.Craft)
                return;

            if (__result == node)
            {
                // this is annoyingly expensive, but eh.
                // We need to check if the node already has all loaded assemblies with upgrades
                // and that the versions equal the current loaded verions. We can short-circuit
                // by testing count: if we have more assemblies than the node, by definition we
                // can't match. We need to do this because otherwise we'll re-run the upgrade
                // pipeline every time we reload this craft.
                if (TryLoadVersions(__result, ctx) && _versionsCurrent.Count <= _versionsLoadedString.Count)
                {
                    bool ok = true;
                    foreach (var kvp in _versionsCurrent)
                    {
                        if (!_versionsLoadedString.TryGetValue(kvp.Key.GetName().Name, out var v) || v < kvp.Value)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                        return;
                }
                // If we got here, versions are unequal or missing
                __result = __result.CreateCopy(); // so it gets resaved
            }
            // else is unecessary; if they're not equal, we don't have to copy and we can just stomp versions in-place

            AddVersions(__result, ctx);
        }

        private static bool UpgradeScript_Test_Prefix(UpgradeScript __instance, ConfigNode n, LoadContext loadContext, out TestResult __result)
        {
            Version v;
            if (_currentAsm == null)
            {
                v = __instance.GetCfgNodeVersion(n, loadContext);
            }
            else
            {
                v = GetVersion(_currentAsm);
                UnityEngine.Debug.Log($"[KSPCommunityFixes] Testing UpgradeScript {_scriptToType[__instance].Name} from assembly {_scriptToType[__instance].Assembly.GetName().Name}, using version {v}");
            }
            TestResult tRst = __instance.VersionTest(v);
            if (tRst != TestResult.Upgradeable)
            {
                __result = tRst;
                return false;
            }
            string nodeName = string.Empty;
            string nodeURL = __instance.GetNodeURL(loadContext);
            if (string.IsNullOrEmpty(nodeURL))
            {
                tRst = __instance.OnTest(n, loadContext, ref nodeName);
                __instance.LogTestResults(nodeName, tRst);
                __result = tRst;
                return false;
            }
            tRst = TestResult.Pass;
            __instance.RecurseNodes(n, nodeURL.Split('/'), 0, delegate (ConfigNode node, ConfigNode parent)
            {
                nodeName = string.Empty;
                TestResult testResult = __instance.OnTest(node, loadContext, ref nodeName);
                __instance.LogTestResults(nodeName, testResult);
                switch (testResult)
                {
                    case TestResult.TooEarly:
                        throw new InvalidOperationException("Script-Level testing shouldn't return TooEarly. This value is only meaningful for Version testing. Override VersionTest if necessary.");
                    case TestResult.Upgradeable:
                        if (tRst != TestResult.Pass)
                        {
                            break;
                        }
                        goto case TestResult.Failed;
                    case TestResult.Failed:
                        tRst = testResult;
                        break;
                }
            });
            __result = tRst;
            return false;
        }
    }
}
