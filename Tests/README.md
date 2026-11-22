# Encounter display regression checks

These tests use the real KSP 1.12.5 managed API and the compiled KSPCF DLL.
The recorded transfer replay additionally uses official PatchedConicFixes 0.2.0;
the production KSPCF patch has no PCF dependency.

Build KSPCF first, then run:

```powershell
dotnet test Tests/KSPCommunityFixes.EncounterDisplay.Tests.csproj -c Release -p:KSPRoot="<KSP installation>"
```

`KSPRoot` defaults to `KSPDIR`. The tests expect the installed Harmony, KSPBurst
and PCF DLLs there. Override `PCFReferencePath` or `KSPCFReferencePath` to test
other artifact locations. Running the test project does not build or install mods.

The checks cover future encounter selection, missing/inactive/cyclic patch chains,
periapsis validity, maneuver preview chain selection, KSPCF postfix registration,
the stock UI fields, and a recorded transfer orbit. The replay periapsis rounds
to the map's displayed 24,973 m.
