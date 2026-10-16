param([string]$GameRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path,
      [string]$OutputRoot=$PSScriptRoot)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$coreOut=Join-Path $OutputRoot '1.6\Assemblies'
$mpOut=Join-Path $OutputRoot 'Multiplayer\Assemblies'
& dotnet build (Join-Path $PSScriptRoot 'Source\Monolyn.Mecha.csproj') -c Release "-p:GameRoot=$GameRoot" "-p:OutputPath=$coreOut/"
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$core=Join-Path $coreOut 'Monolyn.Mecha.dll'
& dotnet build (Join-Path $PSScriptRoot 'Source\Multiplayer\Monolyn.Mecha.Multiplayer.csproj') -c Release "-p:GameRoot=$GameRoot" "-p:MechaAssembly=$core" "-p:OutputPath=$mpOut/"
if($LASTEXITCODE -ne 0){throw 'Multiplayer build failed'}
Get-FileHash -LiteralPath $core,(Join-Path $mpOut 'Monolyn.Mecha.Multiplayer.dll') -Algorithm SHA256
