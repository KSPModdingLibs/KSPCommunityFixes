# KSP Community Fixes

This plugin is a collection of code patches aiming at fixing internal bugs in the KSP codebase, as well as QoL improvements.
There is no defined scope for now, so feel free to propose additional patches ideas by opening an issue.

### Dowload and installation

Compatible with **KSP 1.12**

**Required** and **must be downloaded separately** : 

- **HarmonyKSP** : **[Download](https://github.com/KSPModdingLibs/HarmonyKSP/releases)** - [Homepage](https://github.com/KSPModdingLibs/HarmonyKSP/) - Available on [CKAN]
- **ModuleManager** : **[Download](https://ksp.sarbian.com/jenkins/job/ModuleManager/lastSuccessfulBuild/artifact/)** - [Forum post](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-18x-110x-module-manager-414-july-7th-2020-locked-inside-edition/) - Available on [CKAN]

**Installation**

- Go to the **[GitHub release page](https://github.com/KSPModdingLibs/KSPCommunityFixes/releases)** and download the file named `KSPCommunityFixes_x.x.x.zip`
- Open the downloaded *.zip archive
- Copy the `KSPCommunityFixes` folder found in the archive into the `GameData` folder of your KSP installation.

### Features

Individual patches can be enabled or disabled by editing the `Settings.cfg` file.

#### Bugfixes

- **RefundingOnRecovery** : Vessel recovery funds properly account for modules implementing IPartCostModifier (affect stock cargo parts and many modules from various mods)

#### Quality of Life tweaks 

- **PAWCollapsedInventories** : Part Action Window inventory UI widgets in a collapsed group by default, group title show slots usage and cargo mass. Applied to part and kerbal inventories. ![](https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/PAWCollapsedInventories.gif)
- **AltimeterHorizontalPosition** : Altimeter widget horizontal position is now tweakable in the pause menu settings. ![](https://github.com/KSPModdingLibs/KSPCommunityFixes/raw/master/Screenshots/AltimeterHorizontalPosition.gif)

### License

MIT

[CKAN]: https://forum.kerbalspaceprogram.com/index.php?/topic/197082-ckan-the-comprehensive-kerbal-archive-network-v1304-hubble/