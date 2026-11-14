param([switch]$RunTests)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio C++ build tools are required.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$toolset = Get-ChildItem "$installation/MSBuild/Microsoft/VC/*/Platforms/x64/PlatformToolsets/*" -Directory |
    Where-Object Name -Match '^v\d+$' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $toolset) { throw 'No installed x64 platform toolset found.' }
& $msbuild "$PSScriptRoot/../KSPCommunityFixes/Native/DragReduction.vcxproj" /nologo /v:minimal /p:Configuration=Release /p:Platform=x64 "/p:PlatformToolset=$($toolset.Name)"
if ($LASTEXITCODE -ne 0) { throw 'Drag reduction native build failed.' }

if ($RunTests) {
    $testDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../Tests/bin")
    & $msbuild "$PSScriptRoot/../KSPCommunityFixes/Native/DragReduction.vcxproj" /nologo /v:minimal /p:Configuration=Release /p:Platform=x64 "/p:PlatformToolset=$($toolset.Name)" `
        /p:ConfigurationType=Application /p:TargetName=DragReductionTests "/p:DragReductionSource=$PSScriptRoot/../Tests/DragReductionTests.cpp" `
        "/p:OutDir=$testDirectory/" "/p:IntDir=$testDirectory/obj/"
    if ($LASTEXITCODE -ne 0) { throw 'Drag reduction test build failed.' }
    & "$testDirectory/DragReductionTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "Drag reduction tests failed: $LASTEXITCODE" }
}
