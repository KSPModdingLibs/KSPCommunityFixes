# KSP Community Fixes

This plugin is a collection of code patches aiming at fixing internal bugs in the KSP codebase, as well as QoL improvements.
There is no defined scope for now, so feel free to propose additional patches ideas by opening an issue, or to contribute with a pull request.

### Download and installation

Compatible with **KSP 1.8.0** to **1.12.2** - Available on [CKAN]

**Required** and **must be downloaded separately** : 

- **HarmonyKSP** : **[Download](https://github.com/KSPModdingLibs/HarmonyKSP/releases)** - [Homepage](https://github.com/KSPModdingLibs/HarmonyKSP/) - Available on [CKAN]
- **ModuleManager** : **[Download](https://ksp.sarbian.com/jenkins/job/ModuleManager/lastSuccessfulBuild/artifact/)** - [Forum post](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-18x-110x-module-manager-414-july-7th-2020-locked-inside-edition/) - Available on [CKAN]

**Installation**

- Go to the **[GitHub release page](https://github.com/KSPModdingLibs/KSPCommunityFixes/releases)** and download the file named `KSPCommunityFixes_x.x.x.zip`
- Open the downloaded *.zip archive
- Open the `GameData` folder of your KSP installation
- Delete any existing `KSPCommunityFixes` folder in your `GameData` folder
- Copy the `KSPCommunityFixes` folder found in the archive into your `GameData` folder

### Features

Individual patches can be enabled or disabled by editing the `Settings.cfg` file.
Some patches will be applied only to specific KSP versions.

#### Bugfixes

- **RefundingOnRecovery** [KSP 1.11.0 - 1.12.2] : Vessel recovery funds properly account for modules implementing IPartCostModifier. This bug affect stock fairings, cargo parts and many modules from various mods (part switchers and procedural parts mods, USI, Kerbalism, Tweakscale, etc).
- **DockingPortDrift** [KSP 1.12.2] : Prevent persistent position drift of docking port connections, as long as the "Rotation locked" advanced tweakables PAW option is enabled (it is by default). Credit to [JPLRepo for the fix](https://forum.kerbalspaceprogram.com/index.php?/topic/204248-*).
- **ModuleIndexingMismatch** [KSP 1.8.0 - 1.12.2] : Prevent modules persisted state from being lost in existing saves/ships following a mod installation/uninstallation/update. Note that this won't handle all cases, but it massively reduce occurences of that issue.
- **StockAlarmCustomFormatterDate** [KSP 1.12.0 - 1.12.2] : Make the stock alarm respect the day/year length defined by mods like Kronometer. Fix the underlying AppUIMemberDateTime UI widget API to use the mod-provided IDateTimeFormatter if present.
- **PAWGroupMemory** [KSP 1.8.0 - 1.12.2] : Fix the expanded/retracted state of Part Action Window groups being reset when the PAW is closed or internally rebuilt (especially frequent in the editor).
- **KerbalInventoryPersistence** [KSP 1.12.2] : Fix the whole kerbal inventory persistence system being inactive in KSP 1.12.2. This cause multiple issues, like being able to bypass kerbal inventories mass/volume limits, and various cargo part duplication / disappearance issues when EVAing / boarding.
- **FlightSceneLoadKraken** [KSP 1.8.0 - 1.12.2] : Prevent kraken events on flight scene load in laggy situations

#### Quality of Life tweaks 

- **PAWCollapsedInventories** [KSP 1.11.0 - 1.12.2] : Part Action Window inventory UI widgets in a collapsed group by default, group title show slots usage and cargo mass. Applied to part and kerbal inventories.\
![](https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWCollapsedInventories.gif)
- **AltimeterHorizontalPosition** [KSP 1.8.0 - 1.12.2] : Altimeter widget horizontal position is now tweakable in the pause menu settings.\
![](https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AltimeterHorizontalPosition.gif)
- **PAWStockGroups** [KSP 1.11.1 - 1.12.2] : Part Action Window groups for a selection of stock modules
- **TweakableWheelsAutostrut** [KSP 1.8.0 - 1.12.2] : Allow tweaking the autostrut mode of wheels/landing legs. Still default to "heaviest".

#### Mod API
- **MultipleModuleInPartAPI** : This API allow other plugins to implement PartModules that can exist in multiple occurrence in a single part and won't suffer "module indexing mismatch" persistent data losses following part configuration changes. [See documentation on the wiki](https://github.com/KSPModdingLibs/KSPCommunityFixes/wiki/MultipleModuleInPartAPI).

### License

MIT

### Changelog

##### 1.2.0
- New QoL patch : TweakableWheelsAutostrut [issue #16](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/16)
- Fix PluginData folder being created in the Plugins folder

##### 1.1.0
- New bugfix : ModuleIndexingMismatch [issue #14](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/14)
- New bugfix : FlightSceneLoadKraken [issue #9](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/9)
- New bugfix : StockAlarmCustomFormatterDate (thanks to @LGG)
- New bugfix : PAWGroupMemory
- New bugfix : KerbalInventoryPersistence [Squad bugtracker](https://bugs.kerbalspaceprogram.com/issues/28569)
- New QoL patch : PAWStockGroups [issue #1](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/1)
- New API : MultipleModuleInPartAPI
- Small tweaks to the patching engine

##### 1.0.4
- new bugfix : DockingPortDrift

##### 1.0.3
- Fix persisted altimeter position not being correctly reloaded
- Fix map filters transition coroutine errors by disabling the nested gameobject instead of the top one

##### 1.0.2
- New QoL patch : AltimeterHorizontalPosition

##### 1.0.1
- New QoL patch : PAWCollapsedInventories
- Base infrastructure for patch managment and conditional activation based on the KSP version
- Patches can be enabled/disabled in configs
- Support for KSP-Recall / Tweakscale

##### 1.0.0
- Initial release : RefundingOnRecovery bugfix

[CKAN]: https://forum.kerbalspaceprogram.com/index.php?/topic/197082-ckan-the-comprehensive-kerbal-archive-network-v1304-hubble/
