param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [switch]$Baseline,
    [string]$Flags='-mm17-startup',
    [string]$ColdSource,
    [int]$TimeoutSeconds=600
)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $RunRoot){throw 'Do not overwrite runtime evidence.'}
New-Item -ItemType Directory -Path "$RunRoot\Config" -Force | Out-Null
$ids=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','asel.monolynrace')
if(!$Baseline){$ids+='local.monolyn.mecha'}
$ids+='local.monolyn.mecha.versionprobe'
if($Flags.Contains('-mm17-functional')){$ids+='local.monolyn.mecha.functionalprobe'}
$version=(Get-Content -LiteralPath "$GameRoot\Version.txt" -Raw).Trim()
$cfg='<ModsConfigData><version>'+$version+'</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+(@($ids|Where-Object {$_ -like 'ludeon.rimworld.*'})|ForEach-Object {'<li>'+$_+'</li>'})+'</knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText("$RunRoot\Config\ModsConfig.xml",$cfg)
[IO.File]::WriteAllText("$RunRoot\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode><screenWidth>1280</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><uiScale>1</uiScale></PrefsData>')
if($ColdSource){
    if(!(Test-Path -LiteralPath "$ColdSource\complete")){throw 'Cold source did not complete.'}
    Copy-Item -LiteralPath "$ColdSource\Saves" -Destination $RunRoot -Recurse
    Copy-Item -LiteralPath "$ColdSource\expected-state.txt" -Destination $RunRoot
}
$inputs=@(Get-ChildItem -LiteralPath "$GameRoot\Mods" -Recurse -File | Where-Object {$_.Extension -in '.dll','.xml'})
$inputs+=Get-Item -LiteralPath "$GameRoot\Assembly-CSharp.dll","$GameRoot\Version.txt"
$hashes=@($inputs | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$RunRoot\inputs.json" -Encoding utf8
New-Item -ItemType Directory -Path "$RunRoot\HarnessSource" -Force | Out-Null
Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination "$RunRoot\HarnessSource"
$argsText='-savedatafolder="'+$RunRoot+'" -NoCrashDialog '+$Flags
$proc=Start-Process -FilePath "$GameRoot\RimWorld.exe" -ArgumentList $argsText -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$RunRoot\stdout.log" -RedirectStandardError "$RunRoot\stderr.log"
$identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
$identity | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content -LiteralPath "$RunRoot\process.json" -Encoding utf8
$watch=[Diagnostics.Stopwatch]::StartNew()
try {
    while(!$proc.HasExited -and $watch.Elapsed.TotalSeconds -lt $TimeoutSeconds){
        Start-Sleep -Seconds 5
        $proc.Refresh()
        if(([int]$watch.Elapsed.TotalSeconds % 30) -lt 5){Write-Output "RUNNING pid=$($proc.Id) seconds=$([int]$watch.Elapsed.TotalSeconds)"}
    }
    if(!$proc.HasExited){throw 'Runtime timeout'}
    if(Test-Path -LiteralPath "$RunRoot\failed"){throw ([IO.File]::ReadAllText("$RunRoot\failed"))}
    if(!(Test-Path -LiteralPath "$RunRoot\complete")){throw 'Exited without completion marker'}
    foreach($input in $hashes){if((Get-FileHash -LiteralPath $input.Path).Hash -cne $input.Hash){throw ('Frozen input changed: '+$input.Path)}}
    Write-Output "COMPLETED $RunRoot"
} finally {
    if(!$proc.HasExited){
        $now=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
        if($now.ExecutablePath -ceq $identity.ExecutablePath -and $now.CommandLine -ceq $identity.CommandLine -and $now.CreationDate -eq $identity.CreationDate){Stop-Process -Id $proc.Id}
    }
}
