param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [string]$SettingsFrom
)
$ErrorActionPreference='Stop'
if(!$SettingsFrom){
    & "$PSScriptRoot\..\RimWorld17\Run.ps1" -GameRoot $GameRoot -RunRoot $RunRoot -Flags '-quicktest -mm-light-blink' -TimeoutSeconds 240
    return
}
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$SettingsFrom=(Resolve-Path -LiteralPath $SettingsFrom).Path
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $RunRoot){throw 'Use a new evidence directory.'}
if(!(Test-Path -LiteralPath "$SettingsFrom\complete")){throw 'Settings source did not complete.'}
New-Item -ItemType Directory -Path "$RunRoot\Config" -Force | Out-Null
Copy-Item -LiteralPath "$SettingsFrom\Config\ModsConfig.xml","$SettingsFrom\Config\Prefs.xml" -Destination "$RunRoot\Config"
$settings=@(Get-ChildItem -LiteralPath "$SettingsFrom\Config" -Filter '*MechaMod.xml' -File)
if($settings.Count -ne 1){throw 'Expected exactly one saved Mecha settings file.'}
Copy-Item -LiteralPath $settings[0].FullName -Destination "$RunRoot\Config"
$arguments='-savedatafolder="'+$RunRoot+'" -NoCrashDialog -mm-blink-settings-cold'
$proc=Start-Process -FilePath "$GameRoot\RimWorld.exe" -ArgumentList $arguments -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$RunRoot\stdout.log" -RedirectStandardError "$RunRoot\stderr.log"
$identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
$identity | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content -LiteralPath "$RunRoot\process.json" -Encoding utf8
$watch=[Diagnostics.Stopwatch]::StartNew()
try{
    while(!$proc.HasExited -and $watch.Elapsed.TotalSeconds -lt 180){Start-Sleep -Seconds 2; $proc.Refresh()}
    if(!$proc.HasExited){throw 'Cold settings startup timed out.'}
    if(Test-Path -LiteralPath "$RunRoot\failed"){throw ([IO.File]::ReadAllText("$RunRoot\failed"))}
    if(!(Test-Path -LiteralPath "$RunRoot\complete")){throw 'Cold startup exited without completion.'}
    Write-Output "COMPLETED $RunRoot"
}finally{
    if(!$proc.HasExited){
        $now=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
        if($now.ExecutablePath -ceq $identity.ExecutablePath -and $now.CommandLine -ceq $identity.CommandLine -and $now.CreationDate -eq $identity.CreationDate){Stop-Process -Id $proc.Id}
    }
}
