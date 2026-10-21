param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$MonolynRoot,
    [Parameter(Mandatory=$true)][string]$HarmonyRoot,
    [Parameter(Mandatory=$true)][string]$AlienRaceRoot,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [Parameter(Mandatory=$true)][string]$EvidenceRoot
)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$EvidenceRoot=[IO.Path]::GetFullPath($EvidenceRoot)
$modRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game=Join-Path $EvidenceRoot 'Game'
if(Test-Path -LiteralPath $game){throw 'Use a new evidence directory; never overwrite a frozen game.'}
New-Item -ItemType Directory -Path "$game\Mods" -Force | Out-Null
Get-ChildItem -LiteralPath $GameRoot -File | Copy-Item -Destination $game
foreach($name in @('Data','Assets','Rendering','vulkan')){
    $source=Join-Path $GameRoot $name
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $game -Recurse}
}
function Copy-Package([string]$source,[string]$name){
    $item=Get-Item -LiteralPath $source
    while($item.LinkType -eq 'Junction'){$item=Get-Item -LiteralPath $item.Target}
    Copy-Item -LiteralPath $item.FullName -Destination "$game\Mods\$name" -Recurse
}
Copy-Package $HarmonyRoot 'Harmony'
Copy-Package $AlienRaceRoot 'AlienRace'
Copy-Package $MonolynRoot 'MonolynRace'
$candidate=Join-Path $game 'Mods\MonolynMecha'
New-Item -ItemType Directory -Path "$candidate\1.7\Assemblies" -Force | Out-Null
foreach($name in @('About','Languages','Textures')){Copy-Item -LiteralPath "$modRoot\$name" -Destination $candidate -Recurse}
foreach($name in @('Defs','Patches')){Copy-Item -LiteralPath "$modRoot\1.7\$name" -Destination "$candidate\1.7" -Recurse}
Copy-Item -LiteralPath $CandidateAssembly -Destination "$candidate\1.7\Assemblies\Monolyn.Mecha.dll"
Copy-Item -LiteralPath "$modRoot\LoadFolders.xml" -Destination $candidate
New-Item -ItemType Directory -Path "$game\Mods\Probe\About","$game\Mods\Probe\Assemblies" -Force | Out-Null
[IO.File]::WriteAllText("$game\Mods\Probe\About\About.xml",'<ModMetaData><name>Mecha 1.7 isolated validation</name><author>Validation</author><packageId>local.monolyn.mecha.versionprobe</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>')
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net10.0\Monolyn.Mecha.VersionProbe.dll" -Destination "$game\Mods\Probe\Assemblies"
New-Item -ItemType Directory -Path "$game\Mods\FunctionalProbe\About","$game\Mods\FunctionalProbe\Assemblies","$game\Mods\FunctionalProbe\Defs" -Force | Out-Null
[IO.File]::WriteAllText("$game\Mods\FunctionalProbe\About\About.xml",'<ModMetaData><name>Mecha 1.7 functional probe</name><author>Validation</author><packageId>local.monolyn.mecha.functionalprobe</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>')
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net10.0\Monolyn.Mecha.FunctionalProbe.dll" -Destination "$game\Mods\FunctionalProbe\Assemblies"
Copy-Item -LiteralPath "$PSScriptRoot\..\GuardianAutonomy\TestDefs.xml" -Destination "$game\Mods\FunctionalProbe\Defs"
$files=@(Get-ChildItem -LiteralPath "$game\Mods" -Recurse -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -eq 'About.xml' -or $_.Name -eq 'LoadFolders.xml'})
$files+=Get-Item -LiteralPath "$game\Assembly-CSharp.dll","$game\Version.txt","$game\UnityStubs.dll","$game\Waylith.dll"
$files | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$EvidenceRoot\inputs.json" -Encoding utf8
Write-Output "PREPARED $game"
