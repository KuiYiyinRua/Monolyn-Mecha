param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Mecha,[Parameter(Mandatory=$true)][string]$GameRoot,[int]$Ticks=12000,[int]$Port=31421,[switch]$Trace,[string]$SeedSave)
$ErrorActionPreference='Stop'
& "$PSScriptRoot/PrepareMP.ps1" -RunRoot $RunRoot -Mecha $Mecha -GameRoot $GameRoot
$run=(Resolve-Path -LiteralPath $RunRoot).Path
New-Item -ItemType Directory -Force -Path "$run/Host/Config","$run/Client/Config","$run/Game/Mods/Probe/Defs","$run/HarnessSource" | Out-Null
Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination "$run/HarnessSource"
Copy-Item -LiteralPath "$PSScriptRoot/TestDefs.xml" -Destination "$run/Game/Mods/Probe/Defs"
foreach($peer in @('Host','Client')){Copy-Item -LiteralPath "$run/Single/Config/ModsConfig.xml","$run/Single/Config/Prefs.xml" -Destination "$run/$peer/Config"}
$inputs=@(Get-ChildItem -LiteralPath "$run/Game/Mods" -Recurse -File | Get-FileHash)
$inputs+=Get-FileHash -LiteralPath "$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml"
if($SeedSave){
 New-Item -ItemType Directory -Force -Path "$run/Host/Saves" | Out-Null
 Copy-Item -LiteralPath $SeedSave -Destination "$run/Host/Saves/SelfDestructMpSeed.rws"
 $seedHash=(Get-FileHash -LiteralPath $SeedSave).Hash
 if((Get-FileHash "$run/Host/Saves/SelfDestructMpSeed.rws").Hash -cne $seedHash){throw 'Seed copy mismatch'}
 Set-Content -LiteralPath "$run/seed.sha256" -Value $seedHash
 $inputs+=Get-FileHash -LiteralPath "$run/Host/Saves/SelfDestructMpSeed.rws"
}
$inputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/frozen-inputs.json"
Get-ChildItem -LiteralPath "$run/Game/Mods/Mecha" -Recurse -File -Filter '*.dll' | Get-FileHash | ForEach-Object {$_.Hash+' '+(Split-Path $_.Path -Leaf)} | Set-Content -LiteralPath "$run/candidate.sha256"
$windowCode=Get-Content -LiteralPath "$PSScriptRoot/../SelfDestruct/Run.ps1" | Where-Object {$_ -like 'Add-Type *'} | Select-Object -First 1
Invoke-Expression $windowCode
function ReadLog([string]$path){
 if(!(Test-Path -LiteralPath $path)){return ''}
 try{$stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);try{return $reader.ReadToEnd()}finally{$reader.Dispose()}}catch [IO.IOException]{return ''}
}
$flags=" -mm-aa-mp -mm-port=$Port -mm-ticks=$Ticks"
if($Trace){$flags+=' -mm-trace'}
$owned=[Collections.Generic.List[object]]::new()
function StartPeer([string]$name,[string]$extra){
 $arguments='-savedatafolder="'+$run+'/'+$name+'" -logfile "'+$run+'/'+$name.ToLower()+'.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 900 -noaudio'+$flags+' '+$extra
 $process=Start-Process -FilePath "$run/Game/RimWorldWin64.exe" -WorkingDirectory "$run/Game" -ArgumentList $arguments -WindowStyle Hidden -PassThru
 $record=Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
 $owned.Add([pscustomobject]@{process=$process;record=$record;windowReady=$false})
 $record | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content -LiteralPath "$run/$name-process.json"
}
$timer=[Diagnostics.Stopwatch]::StartNew();$joined=$false;$next=0
try {
 if($SeedSave){StartPeer 'Host' '-mm-seed-load -username=SDHost'}else{StartPeer 'Host' '-quicktest -username=SDHost'}
 while($timer.Elapsed.TotalSeconds -lt 2400){
  foreach($item in $owned){
   $item.process.Refresh()
   if($item.process.HasExited){throw 'Peer exited before completing assertions'}
   if(!$item.windowReady){$hwnd=[NmcTaskWindow]::Find($item.process.Id);if($hwnd -ne [IntPtr]::Zero){[NmcTaskWindow]::SetWindowPos($hwnd,[IntPtr]::new(1),-16000,0,0,0,17)|Out-Null;[NmcTaskWindow]::ShowWindow($hwnd,8)|Out-Null;$item.windowReady=[NmcTaskWindow]::IsWindowVisible($hwnd)}}
  }
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log"
  foreach($peer in @('host','client')){if(Test-Path -LiteralPath "$run/$peer.failed"){throw (ReadLog "$run/$peer.failed")}}
  if(($h+$c) -match 'AA_MP FAILED|Desynced after|Sync Error|Inconsistent player|PacketReadException|WriterException|Map cmd exception|World cmd exception|JobDriver threw|started 10 jobs|Prepatcher Error: Fatal|Error in static constructor|Could not resolve reference to object|MakeThing error:|Exception ticking|NullReferenceException|Could not load Texture2D|XML error:'){throw 'Runtime failure; inspect peer logs'}
  if(!$joined -and $h -match '(?m)^Server started\.'){
   if($h -notmatch '\[Monolyn Mecha MP\] ready commands=19 pilotHolder=1 missing=0'){throw 'Required MP registration summary missing'}
   StartPeer 'Client' ('-mm-client -username=SDClient -connect=127.0.0.1:'+$Port);$joined=$true
  }
  if((Test-Path -LiteralPath "$run/host.complete") -and (Test-Path -LiteralPath "$run/client.complete")){
   if((ReadLog "$run/host.final") -cne (ReadLog "$run/client.final")){throw 'Final simulation or Rand mismatch'}
   $hostChecks=@([regex]::Matches($h,'AA_MP ASSERT[^\r\n]+') | ForEach-Object Value)
   $clientChecks=@([regex]::Matches($c,'AA_MP ASSERT[^\r\n]+') | ForEach-Object Value)
   if($hostChecks.Count -ne 12 -or ($hostChecks -join "`n") -cne ($clientChecks -join "`n")){throw 'Paired action assertions missing or unequal'}
   foreach($input in $inputs){if((Get-FileHash -LiteralPath $input.Path).Hash -cne $input.Hash){throw ('Frozen input changed: '+$input.Path)}}
   [ordered]@{status='PASS';pairedPhases=12;seconds=[int]$timer.Elapsed.TotalSeconds;host=(ReadLog "$run/host.complete");client=(ReadLog "$run/client.complete")} | ConvertTo-Json | Set-Content -LiteralPath "$run/result.json"
   Get-Content -LiteralPath "$run/result.json"; break
  }
  if($joined -and ($h+$c) -match 'Session lost|Disconnected from server'){throw 'Unexpected session loss'}
  if($timer.Elapsed.TotalSeconds -ge $next){$next=$timer.Elapsed.TotalSeconds+30;$progress=@([regex]::Matches($h,'AA_MP (?:PROGRESS|ASSERT)[^\r\n]+') | ForEach-Object Value); Write-Output ("PROGRESS seconds="+[int]$timer.Elapsed.TotalSeconds+" joined="+$joined+" "+($progress | Select-Object -Last 1))}
  Start-Sleep -Seconds 2
 }
 if(!(Test-Path -LiteralPath "$run/result.json")){throw 'Runtime timeout'}
} catch {
 [ordered]@{status='FAIL';reason=$_.Exception.Message;seconds=[int]$timer.Elapsed.TotalSeconds}|ConvertTo-Json|Set-Content -LiteralPath "$run/result.json"
 throw
} finally {
 Set-Content -LiteralPath "$run/quit" -Value 'controller cleanup'
 foreach($item in $owned){
  if(!$item.process.WaitForExit(10000)){
   $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($item.process.Id)"
   if($actual -and $actual.ExecutablePath -ceq $item.record.ExecutablePath -and $actual.CommandLine -ceq $item.record.CommandLine -and $actual.CreationDate -eq $item.record.CreationDate){Stop-Process -Id $actual.ProcessId}else{throw 'Owned process identity changed'}
  }
 }
}
