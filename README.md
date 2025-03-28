# KSP Community Fixes

This plugin is a collection of code patches for fixing bugs and performance issues in the KSP codebase, and adding small QoL improvements. Entirely new features (especially those already covered by other mods) are out of scope, as well as patches that might alter the stock behaviors to minimize potential mod compatibility issues.

This mod is meant as community project, so feel free to propose additional patch ideas by opening an issue, or to contribute with a pull request.

### Download and installation

Compatible with **KSP 1.8.0** to **1.12.5** - Available on the [CKAN] mod manager

**Required** and **must be downloaded separately** : 

- **HarmonyKSP** : **[Download](https://github.com/KSPModdingLibs/HarmonyKSP/releases)** - [Homepage](https://github.com/KSPModdingLibs/HarmonyKSP/) - Available on [CKAN]
- **ModuleManager** : **[Download](https://ksp.sarbian.com/jenkins/job/ModuleManager/lastSuccessfulBuild/artifact/)** - [Forum post](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-18x-110x-module-manager-414-july-7th-2020-locked-inside-edition/) - Available on [CKAN]

[CKAN]: https://github.com/KSP-CKAN/CKAN/releases

**Installation**

Installation with [CKAN] is recommended.  Otherwise:

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
- [**ModuleIndexingMismatch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/264) [KSP 1.8.0 - 1.12.5]<br/>Prevent modules persisted state from being lost in existing saves/ships following a mod installation/uninstallation/update. Note that this won't handle all cases, but it massively reduce occurrences of that issue.
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
- **[CorrectDragForFlags](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/126)** [KSP 1.12.3 - 1.12.5]<br/>Fix the "panel" variants of the flag parts using a single drag cube, causing excessive drag for the smaller options.
- **LadderToggleableLight** [KSP 1.8.0 - 1.12.5]<br/>Fix for the stock "Kelus-LV Bay Mobility Enhancer" light being always active even when the ladder is retracted, and implements manual control of the light.
- [**ReRootPreserveSurfaceAttach**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/142) [KSP 1.8.0 - 1.12.5]<br/>Disable the stock behavior of altering surface attachment nodes on re-rooting, a questionable QoL feature that doesn't work correctly, leading to permanently borked attachement nodes.
- [**ThumbnailSpotlight**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/149) [KSP 1.12.0 - 1.12.5], fix rogue spotlight staying in the scene when a part thumbnail fails to be generated.
- [**FixGetUnivseralTime**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/155) [KSP 1.8.0 - 1.12.5]<br/>Fix Planetarium.GetUniversalTime returning bad values in the editor.
- [**DockingPortConserveMomentum**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/160) [KSP 1.12.3 - 1.12.5]<br/>Make docking ports conserve momentum by averaging the acquire force between the two ports. Notably, docking port Kraken drives will no longer work.
- [**PropellantFlowDescription**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/165) [KSP 1.8.0 - KSP 1.12.5]<br/>Fix printing the resource's base flow mode instead of the (potentially overridden) propellant's flow mode when printing propellants in an engine's info panel in the Part Tooltip.
- [**ModuleAnimateGenericCrewModSpawnIVA**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/169) [KSP 1.8.0 - 1.12.5]<br/>Fix IVA & crew portrait not spawning/despawning when ModuleAnimateGeneric is used to change the part crew capacity. Notably affect the stock inflatable airlock.
- [**TimeWarpOrbitShift**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/170) [KSP 1.8.0 - 1.12.5]<br/>Fix active vessel orbit moving randomly when engaging timewarp while under heavy CPU load.
- [**InventoryPartMass**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/182) [KSP 1.12.0 - 1.12.5]<br/>Fixes bugs where parts stored in inventories would not have the correct mass or volume when their resource levels were modified or variants changed.
- [**EVAConstructionMass**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/185) [KSP 1.12.3 - 1.12.5]<br/>Fixes a bug where picking up a part in EVA construction would set its mass to the wrong value when mass modifiers are involved (e.g. part variants).
- [**RespawnDeadKerbals**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/104) [KSP 1.12.3 - 1.12.5]<br/>When respawning is enabled, starts the respawn timer for any dead kerbals (changing their state to "missing") when loading a save.  This addresses stock bugs where kerbals could be set to dead even when respawning is enabled.
- [**ZeroCostTechNode**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/180) [KSP 1.12.3 - 1.12.5]<br/>Fixes a bug where parts in tech nodes that have 0 science cost would become unusable.
- [**ModulePartVariantsNodePersistence**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/179) [KSP 1.12.3 - 1.12.5]<br/>Fixes an issue with ModulePartVariants where attachnodes would use their default state when resuming flight on a vessel from a saved game.  This would lead to different behavior in part joints and flexibility between initial launch and loading a save.
- [**PartBoundsIgnoreDisabledTransforms**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/208) [KSP 1.12.3 - 1.12.5]<br/>Fix disabled renderers by mesh switchers (B9PartSwitch...) still being considered for part bounds evaluation, resulting in various issues like parts not being occluded from drag in cargo bays, wrong vessel size being reported, etc...
- [**DragCubeLoadException**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/232) [KSP 1.8.0 - 1.12.5]<br/>Fix loading of drag cubes without a name failing with an IndexOutOfRangeException
- [**TimeWarpBodyCollision**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/259) [KSP 1.12.0 - 1.12.5]<br/>Fix timewarp rate not always being limited on SOI transistions, sometimes resulting in failure to detect an encounter/collision with the body in the next SOI.
- [**ModuleActiveRadiatorNoParentException**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/249) [KSP 1.12.3 - 1.12.5]<br/>Fix exception spam when a radiator set to `parentCoolingOnly` is detached from the vessel

#### Quality of Life tweaks 

- **PAWCollapsedInventories** [KSP 1.11.0 - 1.12.5]<br/>Part Action Window inventory UI widgets in a collapsed group by default, group title show volume and mass usage. Applied to part and kerbal inventories.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWCollapsedInventories.gif" width="300"/>
- **AltimeterHorizontalPosition** [KSP 1.8.0 - 1.12.5]<br/>Altimeter widget horizontal position is now tweakable in the pause menu settings.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AltimeterHorizontalPosition.gif" width="500"/>
- **PAWStockGroups** [KSP 1.10.1 - 1.12.5]<br/>Part Action Window groups for a selection of stock items/modules<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWGroups.png" width="500"/>
- **[TweakableWheelsAutostrut](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/16)** [KSP 1.8.0 - 1.12.5]<br/>Allow tweaking the autostrut mode of wheels/landing legs. Still default to "Heaviest part".<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/TweakableWheelsAutostrut.gif"/>
- **AutostrutActions** [KSP 1.8.0 - 1.12.5]<br/>Allow autostrut mode to be toggled with action groups (requires advanced tweakables to be enabled).
- **UIFloatEditNumericInput** [KSP 1.8.0 - 1.12.5]<br/>Allow numeric input ("#" button) in "float edit" PAW items<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/UIFloatEditNumericInput.gif"/>
- **DisableManeuverTool** [KSP 1.12.0 - 1.12.5]<br/>Allow disabling the KSP 1.12 maneuver/trip planner tool in the KSPCF in-game settings menu. It can cause stutters and freezes on scene load, when changing SOI or when editing maneuver nodes, especially with Kopernicus modified systems.
- **FairingMouseOverPersistence** [KSP 1.10.0 - 1.12.5]<br/>Make the "Fairing Expansion" state persistent when reloading a craft in the editor.
- [**AutoSavedCraftNameAtLaunch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/30) [KSP 1.8.0 - 1.12.5]<br/>Append `[Auto-Saved Ship]` when relevant in the Launchpad / Runway UI.<br/><img src="https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AutoSavedCraftNameAtLaunch.png" width="200"/>
- [**ShowContractFinishDates**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/36) [KSP 1.12.0 - 1.12.5]<br/>For archived contracts, show accepted/finished dates.
- **NoIVA** [KSP 1.8.0 - 1.12.5]<br/>Allow to disable IVA functionality and prevent related assets from being loaded, reducing RAM/VRAM usage and slightly increasing performance in some cases. Has a "use placeholder IVA" option allowing to keep crew portraits. This patch is disabled by default and must be enabled from the KSP "ESC" settings menu. It has no entry in the `Settings.cfg` file and require a restart to take effect. Do not use this option alongside IVA mods like RPM or MAS.
- **DisableNewGameIntro** [KSP 1.8.0 - 1.12.5]<br/>Disable the "intro" popups appearing in the space center, VAB/SPH and tracking station upon creating a new career game. Disabled by default.
- [**ToolbarShowHide**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/53) [KSP 1.8.0 - 1.12.5]<br/>Add a button for hiding/showing the stock toolbar. Also allow accessing the toolbar while in the space center facilities windows (mission control, admin building, R&D...).
- **ResourceLockActions** [KSP 1.8.0 - 1.12.5]<br/>Add part actions for locking/unlocking resources flow state.
- [**BetterEditorUndoRedo**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/172) [KSP 1.12.3 - 1.12.5]<br/>Invert the editor undo state capturing logic so part tweaks aren't lost when undoing.  NOTE: this patch is disabled when TweakScale/L is installed.
- [**OptionalMakingHistoryDLCFeatures**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/218) [KSP 1.12.3 - 1.12.5]<br/>Allow to disable the Making History DLC mission editor and additional launch sites features to decrease memory usage and increase loading speed. The Making History parts will still be available. Can be toggled from the KSPCF in-game settings (requires a restart), or from a MM patch (see `Settings.cfg`).
- **TargetParentBody** [KSP 1.8.0 - 1.12.5]<br/>Allow targeting the parent body of the current craft, or any body in the parent hierarchy.

#### Performance tweaks 

- **SceneLoadSpeedBoost** [KSP 1.8.0 - 1.12.5]<br/>Reduce scene switches loading time with large/modded saves by caching the current save in memory instead of loading it from disk.
- [**ForceSyncSceneSwitch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/250) [KSP 1.12.0 - 1.12.5]<br/>Forces all scene transitions to happen synchronously. Mainly benefits highly modded installs by reducing asset cleanup run count from 3 to 1. 
- **OnDemandPartBuoyancy** [KSP 1.8.0 - 1.12.5]<br/>Prevent the part buoyancy integrator from running when not needed. Improves performance for large part count vessels while in the SOI of a body that has an ocean (Kerbin, Eve, Laythe...)
- [**FastLoader**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/108) [KSP 1.12.3 - 1.12.5]<br/>Complete rewrite of the KSP asset/part loader : prevent GPU framerate limits from affecting loading speed, implement multithreaded asset loading (20% to 40% speedup depending on CPU & storage specs), provides an opt-in mechanism for caching PNG textures in the DXT5 format, also implements loading of additional DDS formats (see **BetterDDSSupport** patch in the API/modding tools section).
- **PQSUpdateNoMemoryAlloc** [KSP 1.11.0 - 1.12.5]<br/> Prevent huge memory allocations and resulting occasional stutter on PQS creation happening when moving around near a body surface.
- [**PQSCoroutineLeak**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/85) [KSP 1.8.0 - 1.12.5]<br/>Prevent KSP from spawning multiple PQS update coroutines for the same PQS after scene switches and on other occasions, causing large performance degradation over time.
- [**MemoryLeaks**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/49) [KSP 1.12.0 - 1.12.5]<br/>Fix a bunch of managed memory leaks, mainly by proactively removing `GameEvents` delegates originating from destroyed `UnityEngine.Object` instances on scene switches. Will log detected leaks and memory usage. Also see`Settings.cfg` to enable advanced logging options that can be useful to hunt down memory leaks in mods.
- [**ProgressTrackingSpeedBoost**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/57) [KSP 1.12.0 - 1.12.5]<br/>Remove unused ProgressTracking update handlers. Provides a very noticeable performance uplift in career games having a large amount of celestial bodies and/or vessels.
- [**DisableMapUpdateInFlight**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/59) [KSP 1.8.0 - 1.12.5]<br/>Disable the update of orbit lines and markers in flight when the map view isn't shown. Provides decent performance gains in games having a large amount of celestial bodies and/or vessels.
- [**CommNetThrottling**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/299) [KSP 1.12.0 - 1.12.5]<br/>Implement a throttling mechanism preventing CommNet network updates from happening every frame. When this patch is enabled, network updates will only happen at a set interval of in-game seconds (default is 2.5s, configurable in `Settings.cfg`). This patch will cause events such as line of sight loss or acquisition, or comm link changes to happen with a slight delay, but provide a significant performance uplift in games having a large amount of celestial bodies and/or vessels.
- [**AsteroidAndCometDrillCache**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/67) [KSP 1.12.5]<br/>Reduce constant overhead of ModuleAsteroidDrill and ModuleCometDrill by using the cached asteroid/comet part lookup results from ModuleResourceHarvester. Improves performance with large part count vessels having at least one drill part.
- [**FewerSaves**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/80) [KSP 1.8.0 - KSP 1.12.5]<br/>Disables saving on exiting Space Center minor buildings (Mission Control etc) and when deleting vessels in Tracking Station. Disabled by default.
- [**ConfigNodePerf**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/88) [see also](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/90) [KSP 1.8.0 - KSP 1.12.5]<br/>Speeds up many ConfigNode methods, especially reading and writing ConfigNodes.
- [**RestoreMaxPhysicsDT**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/95) [KSP 1.8.0 - 1.12.5]<br/>When using physics warp, Unity will set the max physics dt to be at least as high as the scaled physics dt. But KSP will never restore it back to the normal value from the settings. This can degrade performance as it allows more FixedUpdates to run per frame.
- [**ContractProgressEnumCache**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/100) [KSP 1.8.0 - 1.12.5]<br/>Prevent performance drops when there are in-progress comet sample or rover construction contracts
- [**DragCubeGeneration**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/139) [KSP 1.12.0 - 1.12.5]<br/> Faster and more reliable implementation of drag cube generation. Improves overall loading times (both game load and scene/vessel/ship load times), prevent occasional lag spikes (in the editor mostly) and fix some issues causing incorrect drag cubes to be generated (notable examples are the stock inflatable heat shield, the 1.25m and 2.5m nose cones and the Mainsail shroud). Note that by design, this patch results in a small deviation from the stock behavior for buyoancy, aerodynamics and thermodynamics, as the generated drag cubes will be slightly different.
- **LocalizerPerf** [KSP 1.8.0 - 1.12.5]<br/>Faster and minimal-allocation replacements for the `Localizer.Format()` methods, can provide significant speedup for GUI-heavy mods using localized strings.
- [**DisableHiddenPortraits**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/84) [KSP 1.8.0 - 1.12.5]<br/>Prevent non-visible crew portraits from being rendered after a switch back from the map view (and other cases), causing a significant perf hit when there are many kerbals in the vessel.
- **IMGUIOptimization** [KSP 1.8.0 - 1.12.5]<br/>Eliminate structural GC allocations and reduce performance overhead of OnGUI() methods. Can provide significant performance gains when having many mods using IMGUI heavily.
- [**CollisionManagerFastUpdate**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/174) [KSP 1.11.0 - 1.12.5]<br/>3-4 times faster update of parts inter-collision state, significantly reduce stutter on docking, undocking, decoupling and joint failure events.
- [**LowerMinPhysicsDTPerFrame**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/175) [KSP 1.12.3 - 1.12.5]<br/>Allow a min value of 0.02 instead of 0.03 for the "Max Physics Delta-Time Per Frame" main menu setting.  This allows for higher and smoother framerate at the expense of the game lagging behind real time.
- [**OptimizedModuleRaycasts**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/216) [KSP 1.12.3 - 1.12.5]<br/>Improve engine exhaust damage and solar panel line of sight raycasts performance by avoiding extra physics state synchronization and caching solar panels scaled space raycasts results.
- [**ModuleDockingNodeFindOtherNodesFaster**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) [KSP 1.12.3 - 1.12.5]<br/>Faster lookup of other docking nodes.
- [**CollisionEnhancerFastUpdate**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) [KSP 1.12.3 - 1.12.5]<br/>Optimization of the `CollisionEnhancer` component (responsible for part to terrain collision detection).
- [**PartSystemsFastUpdate**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) [KSP 1.12.3 - 1.12.5]<br/>Optimization of various flight scene auxiliary subsystems : temperature gauges, highlighter, strut position tracking...
- [**MinorPerfTweaks**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) [KSP 1.12.3 - 1.12.5]<br/>Various small performance patches (volume normalizer, eva module checks, faster `FlightGlobals.fetch` accessor)
- [**FloatingOriginPerf**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) [KSP 1.12.3 - 1.12.5]<br/>General micro-optimization of floating origin shifts. Main benefit is in large particle count situations (ie, launches with many engines) but this helps a bit in other cases as well.
- [**FasterPartFindTransform**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/255) [KSP 1.12.3 - 1.12.5]<br/>Faster, and minimal GC alloc relacements for the Part FindModelTransform* and FindHeirarchyTransform* methods.
- [**CraftBrowserOptimisations**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/284) [KSP 1.12.0 - 1.12.5]<br/>Significantly reduces the time it takes to open the craft browser and to search by name. Most noticeable with lots of craft.
- [**OptimisedVectorLines**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/281) [KSP 1.12.0 - 1.12.5]<br/>Improve performance in the Map View when a large number of vessels and bodies are visible via faster drawing of orbit lines and CommNet lines.
- [**GameDatabasePerf**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/269) [KSP 1.12.3 - 1.12.5]<br/>Faster dictionary backed version of the stock `GameDatabase.GetModel*` / `GameDatabase.GetTexture*` methods. This patch is always enabled and has no entry in `Settings.cfg`.
- [**PartParsingPerf**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/269) [KSP 1.8.0 - 1.12.5]<br/>Faster part icon generation and `Part` fields parsing.

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
- **BetterDDSSupport** [KSP 1.12.3 - 1.12.5]<br/>Implement compatibility with the DXT10/DXGI specification, and support of loading additional texture formats :
  - `BC4` : single channel (R) compressed 4 bpp 
  - `BC5` : 2 channels (RG) compressed 8 bpp
  - `BC6H` : 3 channels (RGB) compressed 8 bpp with HDR color range (**Not available on MacOS**)
  - `BC7` : 4 channels (RGBA) compressed 8 bpp (**Not available on MacOS**)
  - `R16G16B16A16` : 4 channels (RGBA) uncompressed 64 bpp
  - `R16_FLOAT` / `R32_FLOAT` : single channel (R) uncompressed 16/32 bpp 
  - `R16G16_FLOAT` / `R32G32_FLOAT` : 2 channels (RG) uncompressed 32/64 bpp
  - `R16G16B16A16_FLOAT` / `R32G32B32A32_FLOAT` : 4 channels (RGBA) uncompressed 64/128 bpp
- [**KSPFieldEnumDesc**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/243) [KSP 1.12.2 - 1.12.5]<br/>Disabled by default, you can enable it with a MM patch. Adds display name and localization support for enum KSPFields. To use add `Description` attribute to the field.
- [**BaseFieldListUseFieldHost**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/278) [KSP 1.12.3 - 1.12.5]<br/>Allow `BaseField` and associated features (PAW controls, persistence, etc) to work when a custom `BaseField` is added to a `BaseFieldList` (ie, a `Part` or `PartModule`) with a `host` instance other than the `BaseFieldList` owner. See the linked PR and code for use cases and example usage.

#### Stock configs tweaks
- **[ManufacturerFixes](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/62)**<br/>Fix a bunch of stock parts not having manufacturers, add icons for the stock "Stratus Corporation" and "LightYear Tire Company" and two new agents, "FreeFall Parachutes" and "Clamp-O-Tron".
- **[LandingGearLights](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/122)**<br/>Fix the lights on the "LY-10" and "LY-35" landing gears not automatically turning on/off when extending/retracting the landing gear.

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

##### 1.37.1
**Bug fixes**
- **PartParsingPerf** : Fixed [issue #305](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/305), incorrect parsing of `dragModelType` part config values resulting in various parts such as wings to generate additional drag.

##### 1.37.0
**New / improved patches**
- New performance patch : [**CraftBrowserOptimisations**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/284), significantly reduces the time it takes to open the craft browser and to search by name. Most noticeable with lots of craft. Thanks to @Halbann for this contribution.
- New performance patch : [**OptimisedVectorLines**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/281), improve performance in the Map View and tracking station when a large number of vessels and bodies are visible via faster drawing of orbit lines and CommNet lines. Thanks to @Halbann and @CharleRoger for this contribution.
- New QoL patch : [**TargetParentBody**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/285), allow targeting the parent body of the current craft, or any body in the parent hierarchy. Thanks to @jamespglaze for this contribution.
- Improved **PartSystemsFastUpdate** performance patch with a [complete reimplementation of `TemperatureGaugeSystem`](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/270), also see [issue #194](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/194). Mostly eliminate the background processing of (hidden and visible) temperature gauges, and massively reduce the overhead of instantiating them, reducing scene load time and stutter on part count change events such as decoupling, docking, undocking, crashes, etc.
- Various initial **loading performance optimizations**, see [PR #269](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/269):
  - Added performance metrics logging upon reaching the main menu
  - Added (almost) entirely custom MU model parser, roughly 3 times the throughput of the stock parser (~300 MB/s on my machine). Will only really benefit to people having fast NVME drives with good random read performance.
  - New patch, **GameDatabasePerf** : KSPCF now maintains dictionaries of loaded models and texture assets by their url/name, and patch the stock `GameDatabase.GetModel*` / `GameDatabase.GetTexture*` method to use them instead of doing a linear search. This was especially bad with models, as the method would compare the requested string to the `GameObject.name` property for every model in the database.
  - As a part of the **MinorPerfTweaks** patch, patched the `FlightGlobals.fetch` property to not fallback to a `FindObjectOfType()` call when the `FlightGlobals._fetch` field is null, which is always the case during loading. In a stock + BDB test case, this alone was about 10% of the total loading time, 7+ seconds.
  - New patch, **PartParsingPerf**, featuring slightly faster part icon generation and faster `Part` fields parsing by creating a dictionary of IL-emitted parser delegates.
- Improved [**CommNetThrottling**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/299) performance patch. The patch now implement a custom rate limiting logic for CommNet updates, with a more balanced take between performance improvements and simulation precision. As a result, the patch is now enabled by default. Thanks to @JonnyOThan for insisting on that one.
- Improved [**ModuleIndexingMismatch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/264) patch. Will now restore persisted data following a config change (mod updated/added/removed) when the module now present on a part is a base or derived module. Notably allow action group customizations to be kept when sharing craft files between Waterfall / non-Waterfall installs. Thanks to @BrettRyland for detailed reporting.
- New modding API patch : [**BaseFieldListUseFieldHost**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/278). Allow `BaseField` and associated features (PAW controls, persistence, etc) to work when a custom `BaseField` is added to a `BaseFieldList` (ie, a `Part` or `PartModule`) with a `host` instance other than the `BaseFieldList` owner. Potential use cases for this are having a part or module-level PAW item associated to and sharing the state of a common field, for example a field in a `KSPAddon`, or extending external (typically stock) modules with additional PAW UI controls and/or persisted fields.

**Bug fixes**
- **CollisionEnhancerFastUpdate** : Fixed [issue #282](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/282), fixed exception spam when launching fireworks and possibly in other situations with non-part physical objects such as fairing / shroud debris. Thanks to @JonnyOThan for reporting.
- **PersistentIConfigNode** : Fixed [issue #297](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/297), incorrect return value from the `LoadObjectFromConfig()` KSP API method, notably resulting in the PAPI Lights mod failing to load. Thanks to @svm420 for reporting.

**Internal changes**
- Added a `[ManualPatch]` attribute. When applied to a class derived from `BasePatch`, the patch won't be automatically applied by the default patching infrastructure. To apply the patch, call `BasePatch.Patch()` manually. 

##### 1.36.1
Hotfix release for [issue #273](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/273) : [**ForceSyncSceneSwitch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/250) patch incompatibility with [Universal Storage 2](https://github.com/linuxgurugamer/universal-storage-2/). The patch will now be disabled when US2 is installed. 

Note that this patch [might be causing other issues](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/260), but so far we haven't been able to confirm them.

##### 1.36.0
**User facing changes**
- New KSP performance patch : [**FasterPartFindTransform**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/255) [KSP 1.12.3 - 1.12.5] : Faster, and minimal GC alloc relacements for the Part FindModelTransform* and FindHeirarchyTransform* methods.
- New KSP performance patch : [**ForceSyncSceneSwitch**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/250) [KSP 1.12.0 - 1.12.5] : Forces all scene transitions to happen synchronously. Benefits scene transition time by reducing asset cleanup run count from 3 to 1 (contributed by @siimav).
- New KSP performance patches : this update introduce a collection of patches intended to fix various performance bottlenecks mainly relevant in high part count situations. See [PR #257](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/257) and [PR #256](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/256) :
  - **ModuleDockingNodeFindOtherNodesFaster** : Faster lookup of other docking nodes.
  - **CollisionEnhancerFastUpdate** : Optimization of the `CollisionEnhancer` component (responsible for part to terrain collision detection).
  - **PartSystemsFastUpdate** : Optimization of various flight scene auxiliary subsystems : temperature gauges, highlighter, strut position tracking...
  - **MinorPerfTweaks** : Various small performance patches (volume normalizer, eva module checks)
  - **FloatingOriginPerf** : General micro-optimization of floating origin shifts. Main benefit is in large particle count situations (ie, launches with many engines) but this helps a bit in other cases as well.
- New KSP bufix : [**DragCubeLoadException**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/232) [KSP 1.8.0 - 1.12.5] : Fix loading of drag cubes without a name failing with an IndexOutOfRangeException (contributed by @Nazfib).
- New KSP bufix : [**TimeWarpBodyCollision**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/259) [KSP 1.12.0 - 1.12.5] : Fix timewarp rate not always being limited on SOI transistions, sometimes resulting in failure to detect an encounter/collision with the body in the next SOI (contributed by @JonnyOThan).
- New modding API improvement : [**KSPFieldEnumDesc**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/243) [KSP 1.12.2 - 1.12.5] : Disabled by default, you can enable it with a MM patch. Adds display name and localization support for enum KSPFields. To use add `Description` attribute to the field (contributed by @siimav).
- New KSP bugfix : [**ModuleActiveRadiatorNoParentException**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/249) [KSP 1.12.3 - 1.12.5] : Fix exception spam when a radiator set to `parentCoolingOnly` is detached from the vessel (reported by @BrettRyland).
- **PAWStockGroups** : [Added PAW groups for generators](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/235), making the UI less confusing when multiple generators are present (contributed by @yalov).
- **ModuleIndexingMismatch** : [Improved patch](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/236), now will still load a module data when the mismatched module is of a known base or derived type. Notably prevent engine state such as action groups configuration from being lost when installing/uninstalling Waterfall, or when exchanging craft files between stock and Waterfall installs.

**Internal changes**
- Patching now always run as the first ModuleManagerPostLoad callback, ensuring other callbacks can benefit from the patches (contributed by @al2me6).
- Small internal refactor of the patching infrastructure for less verbose patch declaration.
- Introduced a new "override" patch type, basically an automatic transpiler allowing to replace a method body with another. This has a little less overhead than a prefix doing the same thing, and allow for other patches (including non-KSPCF ones) to prefix the patched method as usual.

##### 1.35.2
- **FastLoader** : Fixed a regression introduced in 1.35.1, causing PNG normal maps to be generated with empty mipmaps.

##### 1.35.1
- **FastLoader** : fixed the PNG loader behavior not being similiar as in stock. It was wrongly generating mipmaps, notably resulting in NPOT textures not showing when texture quality wasn't set to full resolution ([see issue #224](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/224)).
- **FastLoader** : fixed cached PNG textures loading not using the data loaded by the threaded reader, but instead reading the file again synchronously (!). Unsurprisingly, fixing that is massively improving texture loading time.

##### 1.35.0
- New KSP performance patch : [**OptimizedModuleRaycasts**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/216) [KSP 1.12.3 - 1.12.5] : Improve engine exhaust damage and solar panel line of sight raycasts performance by avoiding extra physics state synchronization and caching solar panels scaled space raycasts results.
- New KSP QoL/performance patch : [**OptionalMakingHistoryDLCFeatures**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/218) [KSP 1.12.3 - 1.12.5] : Allow to disable the Making History DLC mission editor and additional launch sites features to decrease memory usage and increase loading speed. The Making History parts will still be available. Can be toggled from the KSPCF in-game settings (requires a restart), or from a MM patch (see `Settings.cfg`)
- New KSP bugfix : [**PartBoundsIgnoreDisabledTransforms**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/208) [KSP 1.12.3 - 1.12.5] : Fix disabled renderers by mesh switchers (B9PartSwitch...) still being considered for part bounds evaluation, resulting in various issues like parts not being occluded from drag in cargo bays, wrong vessel size being reported, etc...
- **BetterUndoRedo** : Fixed "too much undoing" when undoing offset/rotate editor actions, and other incoherent behavior (see [related issue](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/206))
- **FastLoader** : Improved DDS loading performance by avoiding an extra copy of the DDS data
- **MemoryLeaks** : More stock memory leaks fixed, and additional reporting of leaked handlers

##### 1.34.1
- Disable BetterEditorUndoRedo when TweakScale/L is installed due to introducing a bug with part attachments in the editor.

##### 1.34.0
- New KSP QoL/performance patch : [**LowerMinPhysicsDTPerFrame**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/175) : Allow a min value of 0.02 instead of 0.03 for the "Max Physics Delta-Time Per Frame" main menu setting. This allows for higher and smoother framerate at the expense of the game lagging behind real time.  This was already possible by manually editing the `settings.cfg` file, but changes would revert when going into the settings screen.
- New KSP QoL patch : [**BetterEditorUndoRedo**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/172) : Invert the editor undo state capturing logic so part tweaks aren't lost when undoing.
- New KSP bugfix: [**InventoryPartMass**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/182) : Fixes bugs where parts stored in inventories would not have the correct mass or volume when their resource levels were modified or variants changed.
- New KSP bugfix: [**EVAConstructionMass**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/185) : Fixes a bug where picking up a part in EVA construction would set its mass to the wrong value when mass modifiers are involved (e.g. part variants).
- New KSP bugfix: [**RespawnDeadKerbals**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/104) : When respawning is enabled, starts the respawn timer for any dead kerbals (changing their state to "missing") when loading a save.  This addresses stock bugs where kerbals could be set to dead even when respawning is enabled.
- New KSP bugfix: [**ZeroCostTechNode**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/180) : Fixes a bug where parts in tech nodes that have 0 science cost would become unusable.
- New KSP bugfix: [**ModulePartVariantsNodePersistence**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/179) : Fixes an issue with ModulePartVariants where attachnodes would use their default state when resuming flight on a vessel from a saved game.  This would lead to different behavior in part joints and flexibility between initial launch and loading a save.
- Changed patch behavior: PAWGroupMemory now tracks group state globally instead of per-window.
- Added zh-cn localization for ManufacturerFixes.cfg (thanks @zhangyuesai)

##### 1.33.0
- New KSP performance patch : [**CollisionManagerFastUpdate**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/174) : 3-4 times faster update of parts inter-collision state, significantly reduce stutter on docking, undocking, decoupling and joint failure events.

##### 1.32.1
- **IMGUIOptimization** : fixed issue where the patch was breaking some mods UIs due to not resetting some internal state correctly.

##### 1.32.0
- New KSP bugfix : [**TimeWarpOrbitShift**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/170) [KSP 1.8.0 - 1.12.5], fix active vessel orbit moving randomly when engaging timewarp while under heavy CPU load.
- New KSP bugfix : [**ModuleAnimateGenericCrewModSpawnIVA**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/169) [KSP 1.8.0 - 1.12.5], fix IVA & crew portrait not spawning/despawning when ModuleAnimateGeneric is used to change the part crew capacity. Notably affect the stock inflatable airlock.
- New KSP bugfix : [**PropellantFlowDescription**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/165) [KSP 1.8.0 - KSP 1.12.5], fix printing the resource's base flow mode instead of the (potentially overridden) propellant's flow mode when printing propellants in an engine's info panel in the Part Tooltip.
- Fix KSP [issue #153](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/153), prevent flight related events to be registered from ModuleScienceExperiment on the prefabs and in other scenes. This was added to the **MemoryLeaks** patch, since that patch already handle a bunch of GameEvents related issues/leaks.
- New KSP performance patch : **IMGUIOptimization** [KSP 1.8.0 - 1.12.5], eliminate structural GC allocations and reduce performance overhead of OnGUI() methods. Can provide significant performance gains when having many mods using IMGUI heavily.
- **ConfigNodePerf** : fixed [issue #167](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/167), incorrect parsing of config files using `cr` only (old Mac style) line endings.

##### 1.31.1
- **DragCubeGeneration** : Actually enable patch by default, I somehow failed to push that change in the last release (Thanks @dok_377 for reporting)
- **ReflectionTypeLoadExceptionHandler** : Fixed the exception handler itself throwing an exception in a corner case situation where a dynamic assembly is loaded, causing a call to `Assembly.Location` to throw (Thanks @Lisias for reporting).

##### 1.31.0
- New KSP bugfix : [**DockingPortConserveMomentum**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/160) [KSP 1.12.3 - 1.12.5], make docking ports conserve momentum by averaging the acquire forces between the two ports.
- **DragCubeGeneration** : fixed [issue #154](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/154), fix broken map view (and various other issues) when a drag cube is generated on a root part having multiple drag cubes.
- **DragCubeGeneration** : fixed [issue #162](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/162), fix incorrect drag cubes generated for stock procedural fairings (and potentially mods doing procedural mesh generation).
- **DragCubeGeneration** : patch enabled by default, hopefully all bugs fixed :)
- **PersistentIConfigNode** : adress [issue #159](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/159), add some logging when an enum parsing error happens instead of swallowing it silently.

##### 1.30.0
- **DragCubeGeneration** : disabled by default since it continues to cause issues with fairings and some other parts. Will be reenabled by default when issues are fixed.
- New KSP bugfix : [**FixGetUnivseralTime**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/155) [KSP 1.8.0 - 1.12.5], fix Planetarium.GetUniversalTime returning bad values in the editor.

##### 1.29.2
- **ModUpgradePipeline** : fixed [issue #156](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/156), fix to correctly load mod versions on reading sfs/craft file and to correctly sanity-check UpgradeScripts vs current mod version not loaded version.

##### 1.29.1
- **ConfigNodePerf** : fixed [issue #152](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/152), fix CopyTo in overwrite mode not allowing subnodes with duplicate values.

##### 1.29.0
- **DragCubeGeneration** : fixed [issue #150](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/150), fix for in-editor detached parts can cause incorrect drag cubes with modded shaders, notably fix incorrect drag for parachutes and solar panels when the Shaddy + TU mods are installed.
- New KSP bugfix : [**ThumbnailSpotlight**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/149) [KSP 1.12.0 - 1.12.5], fix rogue spotlight staying in the scene when a part thumbnail fails to be generated. Contributed by @JonnyOThan.


##### 1.28.1
- **DragCubeGeneration** : fixed [issue #146](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/146), cache concurrency issues causing `InvalidOperation` exceptions and more generally incorrect drag cubes on dynamic editor/flight drag cube generation.

##### 1.28.0
- New KSP bugfix : [**ReRootPreserveSurfaceAttach**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/142) [KSP 1.8.0 - 1.12.5], disable the stock behavior of altering surface attachment nodes on re-rooting, a questionable QoL feature that doesn't work correctly, leading to permanently borked attachement nodes.
- New API/modding patch : **BetterDDSSupport** [KSP 1.12.3 - 1.12.5] (actually part of the **FastLoader** patch), implement support of loading additional DDS formats.
- New performance patch : [**DisableHiddenPortraits**](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/84) [KSP 1.8.0 - 1.12.5], prevent non-visible crew portraits from being rendered after a switch back from the map view (and other cases), causing a significant perf hit when there are many kerbals in the vessel.
- New performance/bugfix patch : [**DragCubeGeneration**](https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/139) [KSP 1.12.0 - 1.12.5], faster and more reliable implementation of drag cube generation. Improves overall loading times (both game load and scene/vessel/ship load times), prevent occasional lag spikes (in the editor mostly) and fix some issues causing incorrect drag cubes to be generated (notable examples are the stock inflatable heat shield, the 1.25m and 2.5m nose cones and the Mainsail shroud). Note that by design, this patch results in a small deviation from the stock behavior for buyoancy, aerodynamics and thermodynamics, as the generated drag cubes will be slightly different.
- **DisableManeuverTool** : added MM-patcheable flags to set the default enabled state of the maneuver tool or to forcefully disable it, see `Settings.cfg`.

##### 1.27.0
- New performance patch : **LocalizerPerf** [KSP 1.8.0 - 1.12.5] Faster and minimal-allocation replacements for the Localizer.Format() methods, can provide significant speedup for GUI-heavy mods using localized strings.
- **FastLoader** : Faster implementation of the stock `translateLoadedNodes` method, can reduce loading time by up to 30 seconds in a heavily modded install (thanks to @siimav for spotting this).
- **LandingGearLights** : fixed the patch not applying to the smaller LY-10 landing gear (Thanks to @JonnyOThan).
- Fixed [issue #98](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/98) : **ConfigNodePerf** patch ignoring the last key/value pair when parsing a node-less, values only input. Notably fix a compatibility issue with the SimpleConstruction mod causing missing vessel construction costs.

##### 1.26.0
- New stock configs fix, **[LandingGearLights](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/122)** : fix the lights on the "LY-10" and "LY-35" landing gears not automatically turning on/off when extending/retracting the landing gear (Thanks to @JonnyOThan for reporting)
- New KSP bugfix/QoL patch, **LadderToggleableLight** : fix for the stock "Kelus-LV Bay Mobility Enhancer" light being always active even when the ladder is retracted, and implements manual control of the light.

##### 1.25.5
- Fixed [issue #134](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/133) : fix for [issue #133](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/133) was overly conservative, causing loading to stop on exceptions that don't cause a fatal error in stock.

##### 1.25.4
- Fixed [issue #133](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/133) : FastLoader allow the game to launch even when unrecoverable errors happen during loading.

##### 1.25.3
- Fixed [issue #128](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/128) : Fairing Expansion disable not retained after revert to hangar from launch
- Fixed [issue #125](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/125) : Altimeter position was accounting for the AppLauncher width when using the settings slider, but didn't anymore after a scene reload, resulting in a slightly offset position.

##### 1.25.2
- Fixed [issue #96](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/96) : the AutoStrutDrift patch now correctly ignore autostruts originating from a servo part.

##### 1.25.1
- CorrectDragForFlags : fixed flags in landscape mode using the same drag cube as for portrait mode.

##### 1.25.0
- New KSP bugfix patch : [CorrectDragForFlags](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/126), fix the "panel" variants of the flag parts using a single drag cube, causing excessive drag for the smaller options.
- Fixed [issue #124](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/124) : avoid disabling the KSP-Recall "refunding" hack when the user has disabled the KSPCF "RefundingOnRecovery" patch, thus making our friend Lisias happy again.

##### 1.24.6
- Fixed [issue #121](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/121) : MapSOCorrectWrapping patch cause the Mohole to disappear

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
