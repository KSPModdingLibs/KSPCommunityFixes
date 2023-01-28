# KSP Community Fixes

This plugin is a collection of code patches for fixing bugs and performance issues in the KSP codebase, and adding small QoL improvements. Entirely new features (especially those already covered by other mods) are out of scope, as well as patches that might alter the stock behaviors to minimize potential mod compatibility issues.

This mod is meant as community project, so feel free to propose additional patch ideas by opening an issue, or to contribute with a pull request.

### Download and installation

Compatible with **KSP 1.8.0** to **1.12.5** - Available on [CKAN]

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

Individual patches can be enabled or disabled by editing the `Settings.cfg` file. To make sure your changes persist when the mod is updated, it is recommended to make them in a ModuleManager patch. Open the `Extras\KSPCF_UserSettings.cfg.extra` file in a text editor for further instructions.

While all KSP versions from 1.8.0 to 1.12.5 are supported, using the latest one (1.12.5) is highly recommended, as many patches only apply to the most recent KSP versions. When a bug fix patch doesn't apply to an older KSP version, this **doesn't** mean those bugs don't exist there.

User options are available from the "ESC" in-game settings menu :<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/settings.gif"/>

#### Major bugfixes

- **RefundingOnRecovery** [KSP 1.11.0 - 1.12.5]<br/>Vessel recovery funds properly account for modules implementing IPartCostModifier. This bug affect stock fairings, cargo parts and many modules from various mods (part switchers and procedural parts mods, USI, Kerbalism, Tweakscale, etc).
- **KerbalInventoryPersistence** [KSP 1.12.2 - 1.12.5]<br/>Fix the whole kerbal inventory persistence system being inactive in KSP 1.12.2+. This cause multiple issues, like being able to bypass kerbal inventories mass/volume limits, and various cargo part duplication / disappearance issues when EVAing / boarding.
- **[RoboticsDrift](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/13)** [KSP 1.12.0 - 1.12.5]<br/>Prevent unrecoverable part position drift of Breaking Grounds DLC robotic parts and their children parts.
- **DockingPortRotationDriftAndFixes** [KSP 1.12.5]<br/>Make the stock docking port rotation feature actually useable :
  - Completely prevent unrecoverable position drift of children parts of docking ports.
  - Fix joint failure and phantom forces when a docking port pair is set to opposite extreme angles.
  - Allow tweaking the rotation in the editor and while not docked in flight.
  - Rotation can now be properly used in a robotic controller.
  - Remove the -86°/86° hardcoded limitation of `hardMinMaxLimits`, it is now -180°/180°.
  - Fix many issues and state inconsistencies.
  - An optional `DockingPortExtendedRotation.cfg.extra` MM patch extending rotation range to 360° is available in the `Extras` folder. Copy it to your `GameData` folder and remove the `.extra` extension to use it.
- **[AutoStrutDrift](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/21)** [KSP 1.8.0 - 1.12.5]<br/>Improves the overall physics stability when using autostruts and prevent autostrut induced deformations following vessel modification events (decoupling, docking/undocking, fairing separation...).
- **ModuleIndexingMismatch** [KSP 1.8.0 - 1.12.5]<br/>Prevent modules persisted state from being lost in existing saves/ships following a mod installation/uninstallation/update. Note that this won't handle all cases, but it massively reduce occurrences of that issue.
- **PackedPartsRotation** [KSP 1.8.0 - 1.12.5]<br/>Fix part rotations not being reset to their pristine value when a non-landed vessel is packed, resulting in permanent part rotation drift when docking and other minor/cosmetic issues.
- **[PartStartStability](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/9)** [KSP 1.8.0 - 1.12.5]<br/>Fix vessel deformation and kraken events on flight scene load, also prevent some kraken issues when placing parts with EVA construction.

#### Minor bugfixes
- **PAWGroupMemory** [KSP 1.8.0 - 1.12.5]<br/>Fix the expanded/retracted state of Part Action Window groups being reset when the PAW is closed or internally rebuilt (especially frequent in the editor).
- **PAWItemsOrder** [KSP 1.8.0 - 1.12.5]<br/>Fix PAW items position randomly changing and flickering.
- **KerbalTooltipMaxSustainedG** [KSP 1.8.0 - 1.12.5]<br/>Fix the kerbals tooltip giving wrong "Max sustainable G" information.
- **ROCValidationOOR** [KSP 1.8.0 - 1.12.5]<br/>Fix ROCManager crashing during loading with Kopernicus modified systems.
- **ReactionWheelsPotentialTorque** [KSP 1.8.0 - 1.12.5]<br/>Fix reaction wheels reporting incorrect available torque when "Wheel Authority" is set below 100%. Fix stock SAS (and possibly other attitude controllers) instability issues.
- **StockAlarmCustomFormatterDate** [KSP 1.12.0 - 1.12.5]<br/>Make the stock alarm respect the day/year length defined by mods like Kronometer. Fix the underlying AppUIMemberDateTime UI widget API to use the mod-provided IDateTimeFormatter if present.
- **[StockAlarmDescPreserveLineBreak](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/19)** [KSP 1.12.0 - 1.12.5]<br/>Stock alarm preserve line breaks (and tabs) in the description field.
- **ExtendedDeployableParts** [KSP 1.12.0 - 1.12.5]<br/>Fix deployable parts (antennas, solar panels, radiators...) always starting in the extended state when the model isn't exported in the retracted state. This bug affect parts from various mods (ex : Ven's stock revamp solar panels).
- **[DeltaVHideWhenDisabled](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/31)** [KSP 1.12.0 - 1.12.5]<br/>Hide the stock stage delta-v UI elements and navball extended burn info when `DELTAV_CALCULATIONS_ENABLED` and `DELTAV_APP_ENABLED` are disabled by another mod or in the KSP `settings.cfg` file.
- **AsteroidSpawnerUniqueFlightId** [KSP 1.8.0 - 1.12.5]<br/>Fix the asteroid/comet spawner generating non-unique `Part.flightId` identifiers. This has a few minor side effects in stock (mainly incorrect science bonuses), but this field is heavily relied upon by various mods and this can cause major issues for them.
- **PartListTooltipIconSpin** [KSP 1.8.0 - 1.12.5]<br/> Fix editor tooltip part icons not spinning anymore after hovering on a greyed out surface attachable only part while the editor is empty.
- **[ScatterDistribution](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/41)** [KSP 1.8.0 - 1.12.5]<br/>Fix incorrect terrain scatter distribution when a partial longitude range is defined in the PQSLandControl definition.
- **[LostSoundAfterSceneSwitch](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/42)** [KSP 1.12.0 - 1.12.5]<br/> Fix audio source not being centered/aligned with the current vessel after scene switches, causing loss of vessel effects audio and random volume or left/right channel weirdness.
- **[EVAKerbalRecovery](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/43)** [KSP 1.11.0 - 1.12.5]<br/> Fix recovery of EVAing kerbals either causing their inventory to be recovered twice or the science data they carry not being recovered, depending on the EVA kerbal variant/suit.
- **[StickySplashedFixer](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/44)** [KSP 1.8.0 - 1.12.5]<br/> Fix vessel never leaving the splashed state if it starts out splashed, and decouples from its only splashed parts. This also fixes an issue where Splashed overrides Prelaunch as a situation.
- **[RescaledRoboticParts](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/48)** [KSP 1.8.0 - 1.12.5]<br/> Fix rescaled robotics parts propagating their scale to childrens after actuating the servo in the editor
- **[EnginePlateAirstreamShieldedTopPart](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/52)** [KSP 1.11.0 - 1.12.5]<br/>Fix engine plates causing the part attached above them to be incorrectly shielded from airstream.
- **[AsteroidInfiniteMining](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/51)** [KSP 1.10.0 - 1.12.5]<br/>Fix asteroid/comet mass being restored to 100% when reloading after having mined it down to 0%.
- **[CometMiningNotRemovingMass](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/66)** [KSP 1.12.2 - 1.12.5]<br/>Fix mass of comets not actually reducing when mining them, despite the PAW saying so.
- **[DoubleCurvePreserveTangents](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/68)** [KSP 1.8.0 - 1.12.5]<br/>Fix DoubleCurve flattening the tangents of the first keyframe regardless of whether tangents are supplied.
- **[StrategyDuration](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/70)** [KSP 1.8.0 - 1.12.5]<br/>Fix Strategies not using Duration settings.
- **[UpgradeBugs](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/63)** [KSP 1.12.0 - 1.12.5]<br/>Fix various bugs with upgrades, like the part stats upgrade module breaking, upgrades not properly applying in the editor, upgrade cost not being applied to part cost, and various issues int the public API.
- **[MapSOCorrectWrapping](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/83)** [KSP 1.10.0 - 1.12.5]<br/>Fixes issues with biomes crossing the poles (South pole biome at north pole and vice versa). Fixes "polar spikes" in the terrain for 8-bit heightmaps.
- **[ChutePhantomSymmetry](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/107)** [KSP 1.10.0 - 1.12.5]<br/>Fix spread angle still being applied after decoupling symmetry-placed parachutes.

#### Quality of Life tweaks 

- **PAWCollapsedInventories** [KSP 1.11.0 - 1.12.5]<br/>Part Action Window inventory UI widgets in a collapsed group by default, group title show volume and mass usage. Applied to part and kerbal inventories.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWCollapsedInventories.gif" width="300"/>
- **AltimeterHorizontalPosition** [KSP 1.8.0 - 1.12.5]<br/>Altimeter widget horizontal position is now tweakable in the pause menu settings.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AltimeterHorizontalPosition.gif" width="500"/>
- **PAWStockGroups** [KSP 1.10.1 - 1.12.5]<br/>Part Action Window groups for a selection of stock items/modules<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWGroups.png" width="500"/>
- **[TweakableWheelsAutostrut](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/16)** [KSP 1.8.0 - 1.12.5]<br/>Allow tweaking the autostrut mode of wheels/landing legs. Still default to "Heaviest part".<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/TweakableWheelsAutostrut.gif"/>
- **AutostrutActions** [KSP 1.8.0 - 1.12.5]<br/>Allow autostrut mode to be toggled with action groups (requires advanced tweakables to be enabled).
- **UIFloatEditNumericInput** [KSP 1.8.0 - 1.12.5]<br/>Allow numeric input ("#" button) in "float edit" PAW items<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/UIFloatEditNumericInput.gif"/>
- **DisableManeuverTool** [KSP 1.12.0 - 1.12.5]<br/>Allow disabling the KSP 1.12 maneuver planner tool in the KSPCF in-game settings menu. It can cause stutter and freezes on scene load, when changing SOI or when editing maneuver nodes, especially with Kopernicus modified systems.
- **FairingMouseOverPersistence** [KSP 1.10.0 - 1.12.5]<br/>Make the "Fairing Expansion" state persistent when reloading a craft in the editor.
- **HidePartUpgradeExtendedInfo** [KSP 1.8.0 - 1.12.5]<br/>Hides irrelevant extended info on the part tooltip for PartUpgrades in the RnD screen.
- [**AutoSavedCraftNameAtLaunch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/30) [KSP 1.8.0 - 1.12.5]<br/>Append `[Auto-Saved Ship]` when relevant in the Launchpad / Runway UI.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AutoSavedCraftNameAtLaunch.png" width="200"/>
- [**ShowContractFinishDates**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/36) [KSP 1.12.0 - 1.12.5]<br/>For archived contracts, show accepted/finished dates.
- **NoIVA** [KSP 1.8.0 - 1.12.5]<br/>Allow to disable IVA functionality and prevent related assets from being loaded, reducing RAM/VRAM usage and slightly increasing performance in some cases. Has a "use placeholder IVA" option allowing to keep crew portraits. This patch is disabled by default and must be enabled from the KSP "ESC" settings menu. It has no entry in the `Settings.cfg` file and require a restart to take effect. Do not use this option alongside IVA mods like RPM or MAS.
- **DisableNewGameIntro** [KSP 1.8.0 - 1.12.5]<br/>Disable the "intro" popups appearing in the space center, VAB/SPH and tracking station upon creating a new career game. Disabled by default.
- [**ToolbarShowHide**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/53) [KSP 1.8.0 - 1.12.5]<br/>Add a button for hiding/showing the stock toolbar. Also allow accessing the toolbar while in the space center facilities windows (mission control, admin building, R&D...).
- **ResourceLockActions** [KSP 1.8.0 - 1.12.5]<br/>Add part actions for locking/unlocking resources flow state.

#### Performance tweaks 

- **SceneLoadSpeedBoost** [KSP 1.8.0 - 1.12.5]<br/>Reduce scene switches loading time with large/modded saves by caching the current save in memory instead of loading it from disk.
- **OnDemandPartBuoyancy** [KSP 1.8.0 - 1.12.5]<br/>Prevent the part buoyancy integrator from running when not needed. Improves performance for large part count vessels while in the SOI of a body that has an ocean (Kerbin, Eve, Laythe...)
- [**FastLoader**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/108) [KSP 1.12.3 - 1.12.5]<br/>Complete rewrite of the KSP asset/part loader : prevent GPU framerate limits from affecting loading speed, implement multithreaded asset loading (20% to 40% speedup depending on CPU & storage specs), provides an opt-in mechanism for caching PNG textures in the DXT5 format.
- **PQSUpdateNoMemoryAlloc** [KSP 1.11.0 - 1.12.5]<br/> Prevent huge memory allocations and resulting occasional stutter on PQS creation happening when moving around near a body surface.
- [**PQSCoroutineLeak**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/85) [KSP 1.8.0 - 1.12.5]<br/>Prevent KSP from spawning multiple PQS update coroutines for the same PQS after scene switches and on other occasions, causing large performance degradation over time.
- [**MemoryLeaks**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/49) [KSP 1.12.0 - 1.12.5]<br/>Fix a bunch of managed memory leaks, mainly by proactively removing `GameEvents` delegates originating from destroyed `UnityEngine.Object` instances on scene switches. Will log detected leaks and memory usage. Also see`Settings.cfg` to enable advanced logging options that can be useful to hunt down memory leaks in mods.
- [**ProgressTrackingSpeedBoost**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/57) [KSP 1.12.0 - 1.12.5]<br/>Remove unused ProgressTracking update handlers. Provides a very noticeable performance uplift in career games having a large amount of celestial bodies and/or vessels.
- [**DisableMapUpdateInFlight**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/59) [KSP 1.8.0 - 1.12.5]<br/>Disable the update of orbit lines and markers in flight when the map view isn't shown. Provides decent performance gains in games having a large amount of celestial bodies and/or vessels.
- [**CommNetThrottling**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/56) [KSP 1.12.0 - 1.12.5]<br/>Disabled by default, you can enable it with a MM patch. Prevent full CommNet network updates from happening every frame, but instead to happen at a regular real-world time interval of 5 seconds while in flight. Enabling this can provide a decent performance uplift in games having an large amount of celestial bodies and/or vessels, but has a detrimental impact on the precision of the simulation and can potentially cause issues with mods relying on the stock behavior.
- [**AsteroidAndCometDrillCache**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/67) [KSP 1.12.5]<br/>Reduce constant overhead of ModuleAsteroidDrill and ModuleCometDrill by using the cached asteroid/comet part lookup results from ModuleResourceHarvester. Improves performance with large part count vessels having at least one drill part.
- [**FewerSaves**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/80) [KSP 1.8.0 - KSP 1.12.5]<br/>Disables saving on exiting Space Center minor buildings (Mission Control etc) and when deleting vessels in Tracking Station. Disabled by default.
- [**ConfigNodePerf**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/88) [see also](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/90) [KSP 1.8.0 - KSP 1.12.5]<br/>Speeds up many ConfigNode methods, especially reading and writing ConfigNodes.
- [**RestoreMaxPhysicsDT**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/95) [KSP 1.8.0 - 1.12.5]<br/>When using physics warp, Unity will set the max physics dt to be at least as high as the scaled physics dt. But KSP will never restore it back to the normal value from the settings. This can degrade performance as it allows more FixedUpdates to run per frame.
- [**ContractProgressEnumCache**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/100) [KSP 1.8.0 - 1.12.5]<br/>Prevent performance drops when there are in-progress comet sample or rover construction contracts

#### API and modding tools
- **MultipleModuleInPartAPI** [KSP 1.8.0 - 1.12.5]<br/>This API allow other plugins to implement PartModules that can exist in multiple occurrence in a single part and won't suffer "module indexing mismatch" persistent data losses following part configuration changes. [See documentation on the wiki](https://github.com/KSPModdingLibs/KSPCommunityFixes/wiki/MultipleModuleInPartAPI).
- **DockingPortLockedEvents** [KSP 1.12.2 - 1.12.5]<br/>Disabled by default, you can enable it with a MM patch. Fire GameEvents onRoboticPartLockChanging/onRoboticPartLockChanged respectively before/after calls to ModuleDockingNode.ModifyLocked(), following a modification of the ModuleDockingNode.nodeIsLocked field.
- **OnSymmetryFieldChanged** [KSP 1.8.0 - 1.12.5]<br/> Disabled by default, you can enable it with a MM patch. Change the `UI_Control.onSymmetryFieldChanged` callback to behave identically to the `UI_Control.onFieldChanged` callback :
  - The callback will only be called when the field value has actually been modified.
  - The "object" argument will contain the previous field value (instead of the new value).
- **PersistentIConfigNode** [KSP 1.8.0 - 1.12.5]<br/>Implement `IConfigNode` members marked as `[Persistent]` serialization support when using the `CreateObjectFromConfig()`, `LoadObjectFromConfig()` and `CreateConfigFromObject()` methods. Also implements `Guid` serialization support for those methods. Includes a complete rewrite of underlying ConfigNode code (in line with ConfigNodePerf) for performance, lower GC impact, and to fix some stock bugs.
- **ReflectionTypeLoadExceptionHandler** [KSP 1.8.0 - 1.12.5]<br/>Patch the BCL `Assembly.GetTypes()` method to always handle (gracefully) an eventual `ReflectionTypeLoadException`. Since having an assembly failing to load is a quite common scenario, this ensure such a situation won't cause issues with other plugins. Those exceptions are logged (but not re-thrown), and detailed information about offending plugins is shown on screen during loading so users are aware there is an issue with their install. This patch is always enabled and has no entry in `Settings.cfg`.
- **[DepartmentHeadImage](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/47)** [KSP 1.8.0 - 1.12.5]<br/> Fix administration building custom departement head image not being used when added by a mod.
- **[ModUpgradePipeline](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/91)** [KSP 1.8.0 - 1.12.5]<br/>This will save mod versions in sfs and craft files, and use those versions for mods' SaveUpgradePipeline scripts so that mods can handle their own upgrade versioning using native KSP tools instead of having to always run their upgrade scripts.

#### Stock configs tweaks
- **[ManufacturerFixes](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/62)**<br/>Fix a bunch of stock parts not having manufacturers, add icons for the stock "Stratus Corporation" and "LightYear Tire Company" and two new agents, "FreeFall Parachutes" and "Clamp-O-Tron".

### License

MIT

### Localization

This mod supports localization. If you wish to contribute a localization file, you can have the mod generate or update a language template by editing the `Settings.cfg` (see instructions near the end of the file).

### Building

After loading the solution in your IDE, add a `ReferencePath` pointing to the root of your KSP install :

For Visual Studio, right-click on the `KSPCommunityFixes` project > `Properties` > `Reference Paths`, add the path to your KSP install and save the changes. Closing and re-opening the solution might be needed for the changes to propagate.

Alternatively, you can do that manually by creating a `KSPCommunityFixes.csproj.user` file next to the `KSPCommunityFixes.csproj` file, with the following content :
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <ReferencePath>Absolute\Path\To\Your\KSP\Install\Folder</ReferencePath>
  </PropertyGroup>
</Project>
```
Then close / re-open the solution.

Building in the `Debug` configuration will update the `GameData` folder in your KSP install, building in the `Release` configuration will additionally create a zipped release in the `Releases` repository root folder.

For incrementing the version, edit the `KSPCommunityFixes.version` KSP-AVC file, changes will be propagated to `AssemblyInfo.cs` when building in the `Release` configuration.

The `Start` action of the IDE will trigger a build, update the `GameData` files in the KSP install and launch KSP.
If doing so in the `Debug` configuration and if your KSP install is modified to be debuggable, you will be able to debug the code from within your IDE (if your IDE provides Unity debugging support).

### Changelog

##### 1.24.5
- Fixed [issue #113](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/117) : FastLoader patch now requires KSP version >= 1.12.3 due to PartLoader implementation differences in previous 1.12 patch versions

##### 1.24.4
- Fixed [issue #117](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/117) : FastLoader getting stuck on duplicated audio asset

##### 1.24.3
- Fixed [issue #115](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/115) : PNG texture cache isn't always invalidated when a PNG texture is modified
- Updated Krafs.Publicizer dependency to v2.2.0

##### 1.24.2
- Fixed [issue #114](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/114) : Drag cubes are incorrectly calculated on modules using `IMultipleDragCube.AssumeDragCubePosition()` due to FastLoader patch not skipping a frame to let the part animation(s) play

##### 1.24.1
- Fixed [issue #112](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/112) : Cannot dismiss or disable FastLoader opt-in popup on KSP < 1.12.0

##### 1.24.0
- Updated for KSP 1.12.5
- New KSP bugfix patch : [ChutePhantomSymmetry](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/107), fix parachutes in symmetry keeping their spread angle after decoupling.
- New performance patch : [ContractProgressEnumCache](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/100) (thanks to @abrenneke)
- New performance patch : [FastLoader](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/108),Complete rewrite of the KSP asset/part loader : prevent GPU framerate limits from affecting loading speed, implement multithreaded asset loading (20% to 40% speedup depending on CPU & storage specs), provides an opt-in mechanism for caching PNG textures in the DXT5 format. 
- The FastLoader patch also include a fix for [issue #69](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/69), KSP bug causing loading hangs when an invalid `*.png` file is being loaded.
- TextureLoaderOptimizations : the patch has been depreciated, equivalent functionality has been integrated in the more general FastLoader patch.
- ConfigNodePerf : Fixed the `Game.Updated()` patch not being applied due to an AmbiguousMatchException when finding the target method.
- StockAlarmCustomFormatterDate : issue was fixed in stock so the patch doesn't apply to KSP 1.12.4+
- RoboticsDrift : the patch now only apply to KSP 1.12+, as prior KSP versions have minor differences in the robotics code ultimately causing various bugs when the patch is applied, see [issue #72](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/72).

##### 1.23.0
- New performance / KSP bugfix patch : [RestoreMaxPhysicsDT](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/95) (contributed by @JonnyOThan).
- Fix typo in Strategy's SendStateMessage where bold was not disabled after title (as part of StrategyDuration fix, since that is the only place it would be used in stock KSP).
- Fix bug in ConfigNodePerf where it did not properly early-out of parsing if the cfg file had an extraneous } (stock parser aborts in that case).
- Added Chinese localization (contributed by @tinygrox).

##### 1.22.2
- Fix stock bug in SaveUpgradePipeline exposed by ModUpgradePipeline: the stock SaveUpgradePipeline blows up if an UpgradeScript does not apply to both sfs and craft contexts.

##### 1.22.1
- Add KSPAssembly attribute to MultipleModulePartAPI as well, and add the KSPAssemblyDependency to KSPCommunityFixes just in case.
- Set AssemblyVersion to 1.0 and only increment AssemblyFileVersion and KSPAssembly
- Refactor FieldData.WriteValue for ease in use by other mods.

##### 1.22.0
- New modding patch : [ModUpgradePipeline](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/91) (@NathanKell)
- Further improve ConfigNode performance. The tunable settings blocks have been removed since base performance is high enough now. They've been replaced by a single setting that enables not indenting save games and craft files on save, which offers a slight performance boost reading and writing and a fair amount of savings on disk. See https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/90 for full details. Note that some of this rewrite is done via PersistentIConfigNode. (@NathanKell)
- Add a KSPAssembly attribute to the dll.

##### 1.21.0
- New performance / KSP bugfix patch : [PQSCoroutineLeak](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/85) (issue discovered by @Gameslinx)
- Fixed the ToolbarShowHide patch partially failing due to an ambigious match exception when searching the no args `ApplicationLauncher.ShouldItHide()` method overload.
- PersistentIConfigNode patch : added support for nested Persistent IConfigNode, see associated [PR](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/86) (@NathanKell).
- New performance patch : [ConfigNodePerf](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/88) (@NathanKell)

##### 1.20.4
- Restrict UpgradeBugs to KSP 1.12+ since it is compiled against the new combined editor class.
- New KSP bufix : [MapSOCorrectWrapping](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/83) (contributed by @RCrockford)

##### 1.20.3
- New performance patch : [FewerSaves](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/80) (contributed by @siimav)
- StrategyDuration had an incorrect transpiler linkage.

##### 1.20.2
- Fix an issue with the DoubleCurve transpiler (contributed by @NathanKell, bug also contributed by @NathanKell)

##### 1.20.1
- Fix an issue with the DoubleCurve patch : [fix to DoubleCurvePreserveTangents](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/73) (contributed by @NathanKell)
- Fix a stock issue where PartModules added via code would have a null upgrades List : [extension to UpgradeBugs](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/74) (contributed by @NathanKell)

##### 1.20.0
- New KSP bugfix : [UpgradeBugs](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/63) (contributed by @NathanKell)
- New KSP bugfix : [DoubleCurvePreserveTangents](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/68) (contributed by @NathanKell)
- New KSP bugfix : [StrategyDuration](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/70) (contributed by @NathanKell)
- PersistentIConfigNode patch : fixed incorrect serialization in some corner cases(contributed by @NathanKell)
- New KSP bugfix : [CometMiningNotRemovingMass](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/66)
- New performance patch : [AsteroidAndCometDrillCache](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/67) (contributed by @JonnyOThan)
- OnSymmetryFieldChanged : fixed mistakenly inverted "changed" condition resulting in the patch not actually preventing symmetry events to be fired when the value hasn't changed.
- Added russian localization (contributed by @sunnypunny)
- New stock configs tweak : [ManufacturerFixes](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/64) (contributed by @sunnypunny)

##### 1.19.1
- DisableMapUpdateInFlight : fixed phantom map markers on flight scene load

##### 1.19.0
- New performance patch : [CommNetThrottling](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/56) (contributed by @JonnyOThan)
- New performance patch : [DisableMapOrbitsInFlight](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/59) (contributed by @JonnyOThan)
- New performance patch : [ProgressTrackingSpeedBoost](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/57) (contributed by @JonnyOThan)
- New QoL patch : [ToolbarShowHide](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/53) (contributed by @NathanKell)
- New QoL patch : ResourceLockActions
- New KSP bugfix : [EnginePlateAirstreamShieldedTopPart](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/52) (Thanks to @yalov (flart) for reporting and to @Aelfhe1m for coming up with a clever solution).
- New KSP bugfix : [AsteroidInfiniteMining](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/51) (Thanks to @Rodg88 for reporting).

##### 1.18.3
- MemoryLeaks : Only remove GameEvent delegates owned by destroyed UnityEngine.Object instances if they are declared by the stock assembly, or a PartModule, or a VesselModule. Some mods are relying on a singleton pattern by instantiating a KSPAddon once, registering GameEvents there and relying on those being called on the dead instance to manipulate static data (sigh...). Those cases will still be logged when the LogDestroyedUnityObjectGameEventsLeaks flag is set in settings.

##### 1.18.2
- Fixed MemoryLeaks patch causing KSC facilities upgrades being reverted after a scene change.

##### 1.18.1
- Fixed AutostrutActions patch causing nullrefs on part duplication and generally not working as intended.

##### 1.18.0
- New performance patch : [MemoryLeaks](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/49)
- New KSP bugfix : [RescaledRoboticParts](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/48) (thanks to @pap1723)
- New QoL patch : AutostrutActions (suggested by forum user @RealKerbal3x)

##### 1.17.0
- New KSP bugfix : [StickySplashedFixer](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/44) (thanks to @NathanKell)
- New modding bugfix : [DepartmentHeadImage](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/47) (thanks to @NathanKell)
- PersistentIConfigNode patch : added `Guid` serialization support to `CreateObjectFromConfig()`, `LoadObjectFromConfig()` and `CreateConfigFromObject()` methods (thanks to @NathanKell).

##### 1.16.1
- RoboticsDrift : fix "Servo info not found" log spam originating from servo parts for which the drift correction isn't enabled.

##### 1.16.0
- New KSP bugfix : [EVAKerbalRecovery](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/43) (thanks to @JonnyOThan for bug-reporting)
- FairingMouseOverPersistence : patch now only applies to KSP 1.10.0 and latter, as the field it relies on doesn't exists in prior versions, causing exceptions in OnLoad()/OnSave().
- Codebase cleaning pass, analyzers are now happy (or silenced).

##### 1.15.0
- New KSP bugfix : [ScatterDistribution](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/41) (thanks to @R-T-B)
- New KSP bugfix : [LostSoundAfterSceneSwitch](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/42) (thanks to @ensou04)
- Fixed KerbalInventoryPersistence patch not being applied on KSP 1.12.3

##### 1.14.1
- Fix KSPCF [issue #39](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/39) : AltimeterHorizontalPosition patch causes state inconsistencies with vessel filters.
- Fix KSPCF [issue #40](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/40) : UIFloatEditNumericInput patch breaking IR custom FloatEdit control. Don't alter the control prefab list, instead return our custom prefab on the fly in the KSP methods searching the prefab list.

##### 1.14.0
- New KSP bugfix : PartListTooltipIconSpin (investigation efforts by @StoneBlue).
- New KSP bugfix : AsteroidSpawnerUniqueFlightId
- New QoL patch : DisableNewGameIntro
- New performance patch : PQSUpdateNoMemoryAlloc (investigation and fix by @Linx).
- Updated Harmony to v2.2.1
- Fixed multiple RoboticsDrift patch issues: 
  - Improved general numerical stability by normalizing input/output Quaternions.
  - Fixed potential NRE spam happening after in-flight vessel hierarchy changes (ie, docking/undocking/decoupling...)
  - Fixed random child-parts-of-robotic-part displacement after timewarping/reloading (bug introduced with the changes made in 1.12.2)
  - Modded robotic parts whose `servoTransform` has a position offset relative to the part origin (either due to non-zero local position in the model hierarchy or a position offset in the part config `MODEL{}` node) are now unsupported. This mean drift correction won't be applied for them, and the stock behavior will apply. KSPCF will issue a log warning when unsupported parts are loaded in flight. Note that such a configuration isn't fully supported by stock either, and such parts will also have issues when manipulating their angle in the editor. And while I *might* find a way to fix this issue in the future, I strongly recommend mod authors to check/alter their models to ensure the `servoTransform` has a zero relative position/rotation from the model root. As of writing, this issue notably affect the BDB Skylab truss, the "More Servos" mod by @Angel-125 and possibly others. 

##### 1.13.2
- RoboticsDrift : fixed a rotation offset being wrongly applied to child parts of translation servos following the fix for issue #35 released in KSPCF 1.12.2 (see [report 1](https://forum.kerbalspaceprogram.com/index.php?/topic/204002-18-112-kspcommunityfixes-bugfixes-and-qol-tweaks/&do=findComment&comment=4132262), [report 2](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/13#issuecomment-1126797719))

##### 1.13.1
- Fixed NoIVA patch causing missing part textures when the part reuse/share IVA textures (ex : SXT). Note that this change negate the loading time gains of the original patch, and might even cause a small increase (a few seconds) if KSP isn't running from a SSD.

##### 1.13.0
- New modding patch : ReflectionTypeLoadExceptionHandler
- New QoL patch : ShowContractFinishDates, contributed by @NathanKell
- New QoL patch : NoIVA
- Added localization support
- Added tooltips to in-game settings

##### 1.12.2
- RoboticsDrift : fixed issue [#35](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/35), incorrect handling of non-stock servo parts having `MODEL{}` rotation/position offsets and/or a model hierarchy where the rotation transform has a position/rotation offset relative to the model root. Notably fixes incorrect behavior with the BDB [Hokulani OCO-RT90 Truss Structure](https://forum.kerbalspaceprogram.com/index.php?/topic/122020-1123-bluedog-design-bureau-stockalike-saturn-apollo-and-more-v1101-%D0%BB%D1%83%D0%BD%D0%B0-24apr2022/&do=findComment&comment=4128194).

##### 1.12.1
- HidePartUpgradeExtendedInfo : fixed wouldn't show extended info sometimes for regular parts. 

##### 1.12.0
- New QoL patch : [AutoSavedCraftNameAtLaunch](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/30)
- New KSP bugfix : [StockAlarmDescPreserveLineBreak](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/19)
- New KSP bugfix : [DeltaVHideWhenDisabled](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/31)

##### 1.11.2
- TextureLoaderOptimizations : restored compatibility with KSP 1.10 and 1.11
- DockingPortRotationDriftAndFixes : fixed autostruts crossing docking ports not being disabled if only one of the port pair is rotation-unlocked.

##### 1.11.1
- TextureLoaderOptimizations hotfix : was causing loading to hang on KSP 1.10 and 1.11 due to using an Unity method only available since KSP 1.12. Will restore compatbility latter, for now the patch is disabled for all versions below 1.12.
- AutoStrutDrift bugfix : fixed potential ArgumentOutOfRangeException.

##### 1.11.0
- New bugfix : ExtendedDeployableParts
- New performance tweak : TextureLoaderOptimizations
- new Qol tweak : HidePartUpgradeExtendedInfo, courtesy of @NathanKell (see [PR#29](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/29))

##### 1.10.1
DockingPortRotationDriftAndFixes refinements :
- Fixed the stock implementation being unable to handle a rotating docking ports when it is the root part (was throwing exceptions and generally wasn't working correctly)
- Fixed another stock bug : having a `rotationAxis` other than "Y" result in the docking port still rotating around the Y axis. This bug affect the 2 "inline" stock docking port parts.
- Fixed KSPCF bug : parent port not being able to rotate after re-docking
- Fixed KSPCF bug : things were not working as expected when using a rotating + non-rotating docking port pair
- Fixed KSPCF bug : prevent rotation being available when about to dock or after undocking when the other docking port is "acquired" but not docked.
- Various performance optimizations

##### 1.10.0
- New bugfix : DockingPortRotationDriftAndFixes. This patch contain several docking port fixes, and supersede the DockingPortLocking and DockingPortDrift patches, those patches have been removed.
- New bugfix : PackedPartsRotation. This patch is a generalization of a fix previously implemented in RoboticsDrift, and now cover all occurrences of that issue.
- New QoL patch : FairingMouseOverPersistence (suggested by forum user @dok_377)
- New mod API optional patch : OnSymmetryFieldChanged (thanks to @DRVeyl)
- New mod API optional patch : PersistentIConfigNode (thanks to @NathanKell)
- PartStartStability : fixed the patch causing an `ArgumentOutOfRangeException` on scene/vessel load in `FlightIntegrator.Update()`. As a side effect, this patch now make the FI first "valid" execution deterministic (will always be on the fourth `FixedUpdate()` cycle).
- RoboticsDrift : fixed incorrect handling when a robotic part is the vessel root part
- Prevent some patches failing with a `ReflectionTypeLoadException` when another plugin assembly fail to load (ex : the Sandcastle/EL integration assembly)

##### 1.9.1
- RoboticsDrift : fixed (harmless) `[RoboticsDrift] Servo info not found...` log spam when toggling the locked state of a robotic part in the editor

##### 1.9.0
- New bugfix : RoboticsDrift, see [issue #13](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/13).
- New mod API patch : DockingPortLockedEvents (added for KJR, see [related issue](https://github.com/KSP-RO/Kerbal-Joint-Reinforcement-Continued/issues/9))
- New bugfix : DockingPortLocking
- PAWCollapsedInventories : Fixed mass/volume info not updating correctly in the group title.
- Now using [Krafs.Publicizer](https://github.com/krafs/Publicizer) for cleaner/faster access to KSP internals. 

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
