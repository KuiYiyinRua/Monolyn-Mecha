param([string]$GameRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path,
      [string]$OutputRoot=$PSScriptRoot,
      [ValidateSet('1.6','1.7')][string]$GameVersion='1.6',
      [string]$MonolynRoot,
      [string]$HarmonyAssembly)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$installedVersion=(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt') -Raw).Trim()
if(!$installedVersion.StartsWith($GameVersion+'.')){throw "Selected $GameVersion but GameRoot contains $installedVersion"}
$coreOut=Join-Path $OutputRoot "$GameVersion\Assemblies"
$mpOut=Join-Path $OutputRoot 'Multiplayer\Assemblies'
$coreProject=Join-Path $PSScriptRoot 'Source\Monolyn.Mecha.csproj'
$extra=@()
if($GameVersion -eq '1.7'){
    $coreProject=Join-Path $PSScriptRoot 'Source\1.7\Monolyn.Mecha.csproj'
    if(!$MonolynRoot){throw 'For 1.7, provide -MonolynRoot with the updated Monolyn Race installation.'}
    if(!$HarmonyAssembly){throw 'For 1.7, provide -HarmonyAssembly with Harmony/1.7/Assemblies/0Harmony.dll.'}
    $MonolynRoot=(Resolve-Path -LiteralPath $MonolynRoot).Path
    $HarmonyAssembly=(Resolve-Path -LiteralPath $HarmonyAssembly).Path
    $extra=@("-p:MonolynRoot=$MonolynRoot","-p:HarmonyAssembly=$HarmonyAssembly")
} elseif($HarmonyAssembly){
    $HarmonyAssembly=(Resolve-Path -LiteralPath $HarmonyAssembly).Path
    $extra=@("-p:HarmonyAssembly=$HarmonyAssembly")
}
& dotnet build $coreProject -c Release "-p:GameRoot=$GameRoot" "-p:OutputPath=$coreOut/" @extra
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$core=Join-Path $coreOut 'Monolyn.Mecha.dll'
$outputs=@($core)
if($GameVersion -eq '1.6'){
    & dotnet build (Join-Path $PSScriptRoot 'Source\Multiplayer\Monolyn.Mecha.Multiplayer.csproj') -c Release "-p:GameRoot=$GameRoot" "-p:MechaAssembly=$core" "-p:OutputPath=$mpOut/" @extra
    if($LASTEXITCODE -ne 0){throw 'Multiplayer build failed'}
    $outputs+=Join-Path $mpOut 'Monolyn.Mecha.Multiplayer.dll'
}
# Runtime XML changes can be staged alongside source without changing the live mod.
# Apply them only to the explicitly selected build output.
$overrides=Join-Path $PSScriptRoot 'Source\RuntimeOverrides'
if(Test-Path -LiteralPath $overrides){
    Get-ChildItem -LiteralPath $overrides | Where-Object { $_.Name -notmatch '^1\.[0-9]+$' -or $_.Name -eq $GameVersion } | Copy-Item -Destination $OutputRoot -Recurse -Force
}
Get-FileHash -LiteralPath $outputs -Algorithm SHA256
