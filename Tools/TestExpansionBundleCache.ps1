$ErrorActionPreference = 'Stop'
$testDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../Tests/bin")
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$compiler = Get-ChildItem "$dotnetRoot/sdk/*/Roslyn/bincore/csc.dll" |
    Sort-Object { [version]($_.Directory.Parent.Parent.Name -split '-')[0] } -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $compiler) { throw 'The .NET SDK Roslyn compiler was not found.' }
$framework = "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319"
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:exe "/out:$testDirectory/ExpansionBundleCacheTests.exe" `
    "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" `
    "$PSScriptRoot/../Tests/ExpansionBundleCacheTests.cs" "$PSScriptRoot/../KSPCommunityFixes/Performance/ExpansionBundleCache.cs"
if ($LASTEXITCODE -ne 0) { throw 'Expansion cache test build failed.' }
Push-Location $testDirectory
try {
    & .\ExpansionBundleCacheTests.exe
    if ($LASTEXITCODE -ne 0) { throw "Expansion cache tests failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
