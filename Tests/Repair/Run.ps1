param([Parameter(Mandatory=$true)][string]$GameRoot,[string]$Label='SP-R1',[switch]$Cold,[string]$ColdSource='Repair-20261017\SP-R1',[switch]$Baseline,[string]$ColdSave='EnemyMechaRaidState',[Parameter(Mandatory=$true)][string]$CandidateSource,[switch]$DiagnosticRun)
$validationRoot=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'BuildValidation'

$ErrorActionPreference='Stop'
$gameRootPath=(Resolve-Path -LiteralPath $GameRoot).Path
$run=Join-Path $validationRoot ('Repair-20261017\'+$Label)
if(Test-Path -LiteralPath $run){throw 'Do not reuse runtime evidence'}
$game=Join-Path $env:TEMP ('MonolynMecha-'+[guid]::NewGuid().ToString('N')+'\Game')
New-Item -ItemType Directory -Path "$game\Mods","$run\Single\Config" -Force | Out-Null
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','Version.txt','steam_appid.txt')){
 if(Test-Path -LiteralPath "$gameRootPath\$name"){Copy-Item -LiteralPath "$gameRootPath\$name" -Destination $game}
}
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$game\$name" -Target "$gameRootPath\$name" | Out-Null}
New-Item -ItemType Directory -Path "$run\Candidate" -Force | Out-Null
foreach($part in @('About','1.6','Languages','Textures','LoadFolders.xml')){Copy-Item -LiteralPath "$candidateSource\$part" -Destination "$run\Candidate" -Recurse}
foreach($name in @('2009463077','839005762','3742031864')){Copy-Item -LiteralPath "$gameRootPath\Mods\$name" -Destination "$game\Mods\$name" -Recurse}
Copy-Item -LiteralPath "$run\Candidate" -Destination "$game\Mods\MonolynMecha" -Recurse
New-Item -ItemType Directory -Path "$game\Mods\Probe\About","$game\Mods\Probe\Assemblies" -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\Monolyn.Mecha.Probe.dll" -Destination "$game\Mods\Probe\Assemblies"
New-Item -ItemType Directory -Force -Path "$game\Mods\Probe\Defs" | Out-Null

$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText("$game\Mods\Probe\About\About.xml",'<ModMetaData><name>Isolated Monolyn Mecha Probe</name><packageId>local.monolyn.mecha.probe</packageId><author>Validation</author><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>',$utf8)
$ids=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','asel.monolynrace')
if(!$Baseline){$ids+=@('local.monolyn.mecha','local.monolyn.mecha.probe')}
$cfg='<ModsConfigData><version>1.6.4850 rev645</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText("$run\Single\Config\ModsConfig.xml",$cfg,$utf8)
[IO.File]::WriteAllText("$run\Single\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><volumeAmbient>0</volumeAmbient><volumeUI>0</volumeUI><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode><screenWidth>1280</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><uiScale>1</uiScale></PrefsData>',$utf8)
$coreHash=(Get-FileHash -LiteralPath "$game\Mods\MonolynMecha\1.6\Assemblies\Monolyn.Mecha.dll").Hash
[IO.File]::WriteAllText("$run\core.sha256",$coreHash,$utf8)
$inputs=@(Get-ChildItem -LiteralPath "$game\Mods\MonolynMecha" -File -Recurse | ForEach-Object {Get-FileHash -LiteralPath $_.FullName})
$inputs+=Get-FileHash -LiteralPath "$run\Single\Config\ModsConfig.xml","$gameRootPath\RimWorldWin64_Data\Managed\Assembly-CSharp.dll","$game\Mods\3742031864\1.6\Assemblies\ASEL.dll","$game\Mods\Probe\Assemblies\Monolyn.Mecha.Probe.dll"
$inputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run\inputs.json" -Encoding UTF8
Add-Type 'using System;using System.Runtime.InteropServices;using System.Text;public static class NmcTaskWindow{public delegate bool EnumProc(IntPtr h,IntPtr l);[DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc cb,IntPtr l);[DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);[DllImport("user32.dll",CharSet=CharSet.Auto)]public static extern int GetClassName(IntPtr h,StringBuilder b,int n);[DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);[DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr hWnd,int cmd);[DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr hWnd,IntPtr after,int x,int y,int w,int h,uint flags);public static IntPtr Find(uint pid){IntPtr result=IntPtr.Zero;EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);var b=new StringBuilder(256);GetClassName(h,b,256);if(p==pid && b.ToString()=="UnityWndClass")result=h;return true;},IntPtr.Zero);return result;}}'
$argList='-savedatafolder="'+$run+'\Single" -logfile "'+$run+'\game.log" -noaudio -screen-fullscreen 0 -quicktest -mm-repair-test'
if($DiagnosticRun){$argList+=' -mm-repair-diagnostics'}
if($Cold){New-Item -ItemType Directory -Force -Path "$run\Single\Saves" | Out-Null;Copy-Item -LiteralPath "$validationRoot\$ColdSource\Single\Saves\$ColdSave.rws" -Destination "$run\Single\Saves";$argList=$argList.Replace('-quicktest','-mm-repair-cold')}
if($Cold){Copy-Item -LiteralPath "$validationRoot\$ColdSource\expected-state.txt" -Destination $run}
$owned=Start-Process -FilePath "$game\RimWorldWin64.exe" -ArgumentList $argList -WorkingDirectory $game -WindowStyle Hidden -PassThru
$record=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)"
$record | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content -LiteralPath "$run\process.json" -Encoding UTF8
$timer=[Diagnostics.Stopwatch]::StartNew(); $next=0; $minimized=$false
try {
 while(!$owned.HasExited){
  if($timer.Elapsed.TotalSeconds -gt 600){throw 'Runtime timeout'}
  $owned.Refresh()
  $hwnd=[NmcTaskWindow]::Find($owned.Id)
  if(!$minimized -or ($hwnd -ne [IntPtr]::Zero -and ![NmcTaskWindow]::IsWindowVisible($hwnd))){
   if($hwnd -ne [IntPtr]::Zero){
    Write-Output "OWNED_WINDOW hwnd=$hwnd visible=$([NmcTaskWindow]::IsWindowVisible($hwnd)) minimized=$([NmcTaskWindow]::IsIconic($hwnd))"
    [NmcTaskWindow]::SetWindowPos($hwnd,[IntPtr]::new(1),-16000,0,0,0,17) | Out-Null
    [NmcTaskWindow]::ShowWindow($hwnd,8) | Out-Null
    [NmcTaskWindow]::SetWindowPos($hwnd,[IntPtr]::new(1),-16000,0,0,0,17) | Out-Null; $minimized=[NmcTaskWindow]::IsWindowVisible($hwnd)
   }
  }
  if($timer.Elapsed.TotalSeconds -ge $next){
   $next=$timer.Elapsed.TotalSeconds+30
   if((Get-CimInstance Win32_OperatingSystem).FreeVirtualMemory -lt 2097152){throw 'Commit headroom below 2 GiB'}
   $tail=if(Test-Path -LiteralPath "$run\evidence.txt"){Get-Content -LiteralPath "$run\evidence.txt" -Tail 1}else{'loading'}
   Write-Output "PROGRESS $([int]$timer.Elapsed.TotalSeconds)s pid=$($owned.Id) $tail"
  }
  Start-Sleep -Seconds 2
  if($Baseline -and (Test-Path -LiteralPath "$run\game.log") -and ((Get-Content -LiteralPath "$run\game.log" -Raw) -match 'Alien race successfully completed')){
   [IO.File]::WriteAllText("$run\complete",'native-only startup diagnostics captured',$utf8); break
  }
 }
 if(Test-Path -LiteralPath "$run\failed"){throw ([IO.File]::ReadAllText("$run\failed"))}
 if(!(Test-Path -LiteralPath "$run\complete")){throw 'Exited before completing assertions'}
 $gameLog=Get-Content -LiteralPath "$run\game.log" -Raw
 $diagnostics=@($gameLog -split "`r?`n" | Where-Object {$_ -match 'Could not resolve cross-reference|Translation data for language|Parsed .* as int'})
 $diagnostics | Set-Content -LiteralPath "$run\upstream-diagnostics.txt" -Encoding UTF8
 if(!$Baseline){
  $baselinePath=Join-Path $validationRoot 'NativeBaselineDiagnostics.txt'
  if(!(Test-Path -LiteralPath $baselinePath)){$baselinePath=Join-Path $PSScriptRoot 'NativeBaselineDiagnostics.txt'}
  if(!(Test-Path -LiteralPath $baselinePath)){throw 'Native-only diagnostic baseline not captured'}
  $expected=Get-Content -LiteralPath $baselinePath
  $new=@($diagnostics | Where-Object {$_ -notin $expected})
  if($new.Count){throw ('New load diagnostics: '+($new -join ' | '))}
  if($gameLog -match 'Exception ticking|Exception drawing|Exception in GameComponent|Error in static constructor|XML error:|Could not load Texture2D|Failed to find any textures at|Tried to destroy non-destroyable|MatFrom with null|NullReferenceException|JobDriver threw'){throw 'Runtime log errors'}
 }
 foreach($input in $inputs){if((Get-FileHash -LiteralPath $input.Path).Hash -cne $input.Hash){throw ('Frozen input changed '+$input.Path)}}
 if($Baseline){$diagnostics | Set-Content -LiteralPath "$validationRoot\NativeBaselineDiagnostics.txt" -Encoding UTF8}
 [ordered]@{status=$(if($Cold){'PASS_RAID_COLD'}elseif($DiagnosticRun){'PASS_RAID_DIAGNOSTICS'}else{'PASS_MECHA_SP'});core=$coreHash;game=$game;seconds=[int]$timer.Elapsed.TotalSeconds;mpTested=$false} | ConvertTo-Json | Set-Content -LiteralPath "$run\result.json" -Encoding UTF8
 Get-Content -LiteralPath "$run\result.json"
} catch {
 [ordered]@{status='FAIL';reason=$_.Exception.Message;game=$game} | ConvertTo-Json | Set-Content -LiteralPath "$run\result.json" -Encoding UTF8
 throw
} finally {
 if(!$owned.HasExited){
  [IO.File]::WriteAllText("$run\quit",'requested',$utf8)
  if(!$owned.WaitForExit(10000)){
   $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)"
   if($actual -and $actual.ExecutablePath -ceq $record.ExecutablePath -and $actual.CommandLine -ceq $record.CommandLine -and ([datetime]$actual.CreationDate).ToUniversalTime() -eq ([datetime]$record.CreationDate).ToUniversalTime()){Stop-Process -Id $owned.Id}
   else{throw 'Owned process identity changed'}
  }
 }
}

