# KSP Community Fixes

This plugin is a collection of code patches for fixing bugs and performance issues in the KSP codebase, and adding small QoL improvements. Entirely new features (especially those already covered by other mods) are out of scope, as well as patches that might alter the stock behaviors to minimize potential mod compatbility issues.

This mod is meant as community project, so feel free to propose additional patch ideas by opening an issue, or to contribute with a pull request.

### Download and installation

Compatible with **KSP 1.8.0** to **1.12.3** - Available on [CKAN]

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

Individual patches can be enabled or disabled by editing the `Settings.cfg` file. Some patches will be applied only to specific KSP versions.

In-game options are available from the KSP settings menu :<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/settings.png"/>

#### Bugfixes

- **RefundingOnRecovery** [KSP 1.11.0 - 1.12.3]<br/>Vessel recovery funds properly account for modules implementing IPartCostModifier. This bug affect stock fairings, cargo parts and many modules from various mods (part switchers and procedural parts mods, USI, Kerbalism, Tweakscale, etc).
- **DockingPortDrift** [KSP 1.12.2]<br/>Prevent part position drift of vessels having docking ports, as long as the "Rotation locked" advanced tweakables PAW option is enabled (it is by default). Credit to [JPLRepo for the fix](https://forum.kerbalspaceprogram.com/index.php?/topic/204248-*).
- **ModuleIndexingMismatch** [KSP 1.8.0 - 1.12.3]<br/>Prevent modules persisted state from being lost in existing saves/ships following a mod installation/uninstallation/update. Note that this won't handle all cases, but it massively reduce occurences of that issue.
- **StockAlarmCustomFormatterDate** [KSP 1.12.0 - 1.12.3]<br/>Make the stock alarm respect the day/year length defined by mods like Kronometer. Fix the underlying AppUIMemberDateTime UI widget API to use the mod-provided IDateTimeFormatter if present.
- **KerbalInventoryPersistence** [KSP 1.12.2 - 1.12.3]<br/>Fix the whole kerbal inventory persistence system being inactive in KSP 1.12.2+. This cause multiple issues, like being able to bypass kerbal inventories mass/volume limits, and various cargo part duplication / disappearance issues when EVAing / boarding.
- **PAWGroupMemory** [KSP 1.8.0 - 1.12.3]<br/>Fix the expanded/retracted state of Part Action Window groups being reset when the PAW is closed or internally rebuilt (especially frequent in the editor).
- **PAWItemsOrder** [KSP 1.8.0 - 1.12.3]<br/>Fix PAW items position randomly changing and flickering.
- **KerbalTooltipMaxSustainedG** [KSP 1.8.0 - 1.12.3]<br/>Fix the kerbals tooltip giving wrong "Max sustainable G" information.
- **ROCValidationOOR** [KSP 1.8.0 - 1.12.3]<br/>Fix ROCManager crashing during loading with Kopernicus modified systems.
- **ReactionWheelsPotentialTorque** [KSP 1.8.0 - 1.12.3]<br/>Fix reaction wheels reporting incorrect available torque when "Wheel Authority" is set below 100%. Fix stock SAS (and possibly other attitude controllers) instability issues.
- **[RoboticsDrift](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/13)** [KSP 1.8.0 - 1.12.3]<br/>Prevent unrecoverable part position drift over time for children parts of Breaking Grounds DLC robotic parts.
- **AutoStrutDrift** [KSP 1.8.0 - 1.12.3]<br/>Improve the overall physics stability when using autostruts and prevent autostrut induced deformations following vessel modification events (decoupling, docking/undocking, fairing separation...).
- **PartStartStability** [KSP 1.8.0 - 1.12.3]<br/>Fix vessel deformation and kraken events on flight scene load, also prevent some kraken issues when placing parts with EVA construction.
#### Quality of Life tweaks 

- **PAWCollapsedInventories** [KSP 1.11.0 - 1.12.3]<br/>Part Action Window inventory UI widgets in a collapsed group by default, group title show slots usage and cargo mass. Applied to part and kerbal inventories.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWCollapsedInventories.gif" width="300"/>
- **AltimeterHorizontalPosition** [KSP 1.8.0 - 1.12.3]<br/>Altimeter widget horizontal position is now tweakable in the pause menu settings.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AltimeterHorizontalPosition.gif" width="500"/>
- **PAWStockGroups** [KSP 1.10.1 - 1.12.3]<br/>Part Action Window groups for a selection of stock items/modules<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWGroups.png" width="500"/>
- **TweakableWheelsAutostrut** [KSP 1.8.0 - 1.12.3]<br/>Allow tweaking the autostrut mode of wheels/landing legs. Still default to "Heaviest part".<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/TweakableWheelsAutostrut.gif"/>
- **UIFloatEditNumericInput** [KSP 1.8.0 - 1.12.3]<br/>Allow numeric input ("#" button) in "float edit" PAW items<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/UIFloatEditNumericInput.gif"/>
- **DisableManeuverTool** [KSP 1.12.0 - 1.12.3]<br/>Allow disabling the stock maneuver tool in the in-game settings menu (it can cause severe lag/stutter, especially with Kopernicus modified systems)

#### Performance tweaks 

- **SceneLoadSpeedBoost** [KSP 1.8.0 - 1.12.3]<br/>Reduce scene switches loading time with large/modded saves by caching the current save in memory instead of loading it from disk.
- **OnDemandPartBuoyancy** [KSP 1.8.0 - 1.12.3]<br/>Prevent the part buoyancy integrator from running when not needed. Improves performance for large part count vessels while in the SOI of a body that has an ocean (Kerbin, Eve, Laythe...)

#### Mod API
- **MultipleModuleInPartAPI**<br/>This API allow other plugins to implement PartModules that can exist in multiple occurrence in a single part and won't suffer "module indexing mismatch" persistent data losses following part configuration changes. [See documentation on the wiki](https://github.com/KSPModdingLibs/KSPCommunityFixes/wiki/MultipleModuleInPartAPI).

### License

MIT

### Changelog

##### 1.9.0
- New bugfix : RoboticsDrift, see [issue #13](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/13).

##### 1.8.0
- New bugfix : AutoStrutDrift, see [issue #21](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/21). Thanks to @Lisias for [investigation efforts](https://github.com/net-lisias-ksp/KSP-Recall/issues/27#issuecomment-1022167916).
- New bugfix : PartStartStability, see [issue #9](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/9).
- The FlightSceneLoadKraken patch is superseded by the PartStartStability patch, which is now disabled by default
- Fixed a silly mistake with the OnDemandPartBuoyancy patch where it would prevent part buoyancy from running if the vessel is already below water at scene load. Thanks to @DRVeyl for catching that.

##### 1.7.0
- New performance patch : OnDemandPartBuoyancy (thanks to @siimav)
- New bugfix : ROCValidationOOR (thanks to @R-T-B)

##### 1.6.1
- Fixed version file for 1.12.3

##### 1.6.0
- Updated for KSP 1.12.3
- DockingPortDrift bugfix doesn't apply in 1.12.3 (identical fix was ported to stock)
- Moved KSPCommunityFixes in-game settings to a dedicated category in the KSP settings menu
- New performance patch : SceneLoadSpeedBoost
- New QoL patch : DisableManeuverTool

##### 1.5.0
- New bugfix : KerbalTooltipMaxSustainedG
- Fixed (again...) some patches not being applied

##### 1.4.2
- Fixed ModuleIndexingMismatch patch causing issues with modules dynamically adding/removing resources. Specifically, the patch was causing part resources to be loaded before (instead of after in stock) the `PartModule.OnLoad()` call when loading a `ShipConstruct`. This notably fixes RealFuel resources being lost when reloading/launching a craft file, but that mistake likely had consequences for other fuel/resource switchers mods (B9PS, Firespitter...)

##### 1.4.1
- Fixed UIFloatEditNumericInput patch causing various errors and generally not working as intended.

##### 1.4.0
- New QoL patch : UIFloatEditNumericInput
- Fixed some patches not being applied in KSP versions below 1.12 : PAWItemsOrder, TweakableWheelsAutostrut, ModuleIndexingMismatch, FlightSceneLoadKraken.
- PAWStockGroups patch now applicable to KSP 1.10.1 (min version was 1.11.1 before)

##### 1.3.0
- New bugfix : PAWItemsOrder

##### 1.2.0
- New QoL patch : TweakableWheelsAutostrut, see [issue #16](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/16)
- Fix PluginData folder being created in the Plugins folder

##### 1.1.0
- New bugfix : ModuleIndexingMismatch, see [issue #14](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/14)
- New bugfix : FlightSceneLoadKraken, see [issue #9](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/9)
- New bugfix : StockAlarmCustomFormatterDate (thanks to @LGG)
- New bugfix : PAWGroupMemory
- New bugfix : KerbalInventoryPersistence, see [Squad bugtracker](https://bugs.kerbalspaceprogram.com/issues/28569)
- New QoL patch : PAWStockGroups, see [issue #1](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/1)
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
