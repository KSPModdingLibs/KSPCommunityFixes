param(
    [string]$KspRoot = $env:KSP_ROOT,
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This release command requires Windows.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK 8 or newer.' }
if (-not $KspRoot) { throw 'Pass -KspRoot with the path to KSP 1.12.5, or set KSP_ROOT.' }
$KspRoot = (Resolve-Path -LiteralPath $KspRoot).Path
if (-not (Test-Path -LiteralPath "$KspRoot/KSP_x64_Data/Managed/Assembly-CSharp.dll")) {
    throw "KSP assemblies not found under $KspRoot/KSP_x64_Data/Managed."
}
if (-not (Get-ChildItem -LiteralPath "$KspRoot/GameData" -Filter 'ModuleManager.*.dll' -File)) {
    throw 'Install ModuleManager in the reference KSP installation before building.'
}

$project = "$PSScriptRoot/KSPCommunityFixes/KSPCommunityFixes.csproj"
& dotnet build $project --configuration Release "-p:KSPBT_GameRoot=$KspRoot" -p:DeployToGame=false -p:CopyReleaseBinariesToRepo=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

if ($RunTests) {
    foreach ($test in Get-ChildItem -LiteralPath "$PSScriptRoot/Tools" -Filter 'Test*.ps1' -File | Sort-Object Name) {
        & $test.FullName
    }
}

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup[0].Version
$archivePath = "$PSScriptRoot/Releases/KSPCommunityFixes_$version.zip"
$requiredFiles = @(
    'GameData/KSPCommunityFixes/Plugins/KSPCommunityFixes.dll',
    'GameData/KSPCommunityFixes/Plugins/0_MultipleModuleInPartAPI.dll',
    'GameData/KSPCommunityFixes/Settings.cfg'
)
$nativeDirectory = "$PSScriptRoot/KSPCommunityFixes/Native"
if (Test-Path -LiteralPath $nativeDirectory) {
    foreach ($nativeProject in Get-ChildItem -LiteralPath $nativeDirectory -Filter '*.vcxproj' -File) {
        [xml]$nativeXml = Get-Content -LiteralPath $nativeProject.FullName -Raw
        $targetName = [string]($nativeXml.Project.PropertyGroup | Where-Object { $_.TargetName } | Select-Object -First 1).TargetName
        if (-not $targetName) { throw "Native project has no target name: $($nativeProject.Name)" }
        $requiredFiles += "GameData/KSPCommunityFixes/Plugins/x86_64/$targetName.dll"
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($required in $requiredFiles) {
        if (-not $archive.GetEntry($required)) { throw "Release archive is missing $required" }
    }
} finally {
    $archive.Dispose()
}
Write-Output "Release ready: $archivePath"
