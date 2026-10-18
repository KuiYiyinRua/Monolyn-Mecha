param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Mecha,[Parameter(Mandatory=$true)][string]$GameRoot,[switch]$Perf)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $RunRoot){throw 'RunRoot must be new'}
$run=[IO.Path]::GetFullPath($RunRoot)
$game=(Resolve-Path -LiteralPath $GameRoot).Path
New-Item -ItemType Directory -Force "$run/Game/Mods/Mecha","$run/Game/Mods/Meow","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Single/Config","$run/Single/Saves" | Out-Null
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')) { New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$game/$name" | Out-Null }
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')) { Copy-Item -LiteralPath "$game/$name" -Destination "$run/Game/" }
foreach($name in @('2009463077','2934420800','Multiplayer','839005762','3742031864')) { Copy-Item -LiteralPath "$game/Mods/$name" -Destination "$run/Game/Mods/" -Recurse }
foreach($name in @('About','1.6','Patches','Languages','LoadFolders.xml')) { Copy-Item -LiteralPath "$game/Mods/MP-meow-online-shop/$name" -Destination "$run/Game/Mods/Meow/" -Recurse }
foreach($name in @('About','1.6','Textures','Languages','LoadFolders.xml','Multiplayer')) { if(Test-Path -LiteralPath "$Mecha/$name"){Copy-Item -LiteralPath "$Mecha/$name" -Destination "$run/Game/Mods/Mecha/" -Recurse} }
Copy-Item -LiteralPath "$PSScriptRoot/binMP/Monolyn.Mecha.MPProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
'<ModMetaData><name>Mecha isolated verification</name><author>Local</author><packageId>local.mecha.verification</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content -LiteralPath "$run/Game/Mods/Probe/About/About.xml" -Encoding UTF8
$extra=''
if($Perf) {
 Copy-Item -LiteralPath "$game/Mods/2038874626" -Destination "$run/Game/Mods/" -Recurse
 New-Item -ItemType Directory -Force "$run/Game/Mods/Perf/About","$run/Game/Mods/Perf/Assemblies" | Out-Null
 Copy-Item -LiteralPath "$PSScriptRoot/../PerfHarness/bin/Release/DpaPerfHarness.dll" -Destination "$run/Game/Mods/Perf/Assemblies/"
 '<ModMetaData><name>DPA Mecha verification</name><author>Local</author><packageId>local.dpa.perfharness</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content -LiteralPath "$run/Game/Mods/Perf/About/About.xml" -Encoding UTF8
 $extra='<li>dubwise.dubsperformanceanalyzer.steam</li><li>local.dpa.perfharness</li>'
}
$dlc='<li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li>'
('<ModsConfigData><version>1.6.4850</version><activeMods><li>zetrith.prepatcher</li><li>brrainz.harmony</li><li>ludeon.rimworld</li>'+$dlc+'<li>erdelf.humanoidalienraces</li><li>asel.monolynrace</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.monolyn.mecha</li><li>local.mecha.verification</li>'+$extra+'</activeMods><knownExpansions>'+$dlc+'</knownExpansions></ModsConfigData>') | Set-Content -LiteralPath "$run/Single/Config/ModsConfig.xml" -Encoding UTF8
'<PrefsData><volumeMaster>0</volumeMaster><runInBackground>True</runInBackground><devMode>False</devMode><langFolderName>English</langFolderName></PrefsData>' | Set-Content -LiteralPath "$run/Single/Config/Prefs.xml" -Encoding UTF8
Get-ChildItem -LiteralPath "$run/Game/Mods" -Recurse -File | Get-FileHash | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$run/inputs.json" -Encoding UTF8
Write-Output $run
