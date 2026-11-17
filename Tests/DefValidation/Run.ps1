param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$EvidenceRoot,
    [int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$EvidenceRoot=[IO.Path]::GetFullPath($EvidenceRoot)
if(Test-Path -LiteralPath $EvidenceRoot){throw 'Use a new evidence directory.'}
$modRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
dotnet build "$PSScriptRoot\Probe.csproj" -c Release "-p:GameRoot=$GameRoot" --nologo
if($LASTEXITCODE -ne 0){throw 'Probe build failed.'}
$game=Join-Path $EvidenceRoot 'Game'
$fixture=Join-Path $game 'Mods\BodyValidation'
New-Item -ItemType Directory -Path "$fixture\About","$fixture\Assemblies","$fixture\Defs","$fixture\Patches" -Force | Out-Null
Get-ChildItem -LiteralPath $GameRoot -File | Copy-Item -Destination $game
foreach($name in @('Data','Assets','Rendering','vulkan')){
    $source=Join-Path $GameRoot $name
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $game -Recurse}
}
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net10.0\Probe.dll" -Destination "$fixture\Assemblies"
[IO.File]::WriteAllText("$fixture\About\About.xml",'<ModMetaData><name>Mecha body validation</name><author>Validation</author><packageId>local.monolyn.mecha.bodyvalidation</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>')
$version=(Get-Content -LiteralPath "$GameRoot\Version.txt" -Raw).Trim()
$results=@()
$baselineErrors=@()
foreach($case in @('Baseline-1.7','Fixed-1.7','Fixed-1.6')){
    $payload=$case.Split('-')[1]
    $baseline=$case.StartsWith('Baseline')
    $defs=New-Object System.Xml.XmlDocument
    $root=$defs.CreateElement('Defs')
    [void]$defs.AppendChild($root)
    foreach($file in Get-ChildItem -LiteralPath "$modRoot\$payload\Defs" -Filter *.xml){
        [xml]$source=Get-Content -LiteralPath $file.FullName -Raw
        foreach($node in $source.SelectNodes('/Defs/BodyDef | /Defs/BodyPartDef')){
            [void]$root.AppendChild($defs.ImportNode($node,$true))
        }
    }
    $defs.Save("$fixture\Defs\Bodies.xml")
    $patch=Join-Path $fixture 'Patches\BodyParts.xml'
    if($baseline){if(Test-Path -LiteralPath $patch){Remove-Item -LiteralPath $patch}}
    else{Copy-Item -LiteralPath "$modRoot\$payload\Patches\BodyParts.xml" -Destination $patch}
    $run=Join-Path $EvidenceRoot $case
    New-Item -ItemType Directory -Path "$run\Config","$run\Fixture" -Force | Out-Null
    Copy-Item -LiteralPath "$fixture\Defs\Bodies.xml" -Destination "$run\Fixture"
    if(!$baseline){Copy-Item -LiteralPath $patch -Destination "$run\Fixture"}
    [IO.File]::WriteAllText("$run\Config\ModsConfig.xml",'<ModsConfigData><version>'+$version+'</version><activeMods><li>ludeon.rimworld</li><li>local.monolyn.mecha.bodyvalidation</li></activeMods><knownExpansions /></ModsConfigData>')
    [IO.File]::WriteAllText("$run\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><uiScale>1</uiScale></PrefsData>')
    $flags='-savedatafolder="'+$run+'" -NoCrashDialog -mm-body-validation'
    if($baseline){$flags+=' -mm-body-baseline'}
    $proc=Start-Process -FilePath "$game\RimWorld.exe" -ArgumentList $flags -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run\stdout.log" -RedirectStandardError "$run\stderr.log"
    $identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
    $identity | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content -LiteralPath "$run\process.json" -Encoding utf8
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try{
        while(!$proc.HasExited -and $watch.Elapsed.TotalSeconds -lt $TimeoutSeconds){
            Start-Sleep -Seconds 2
            $proc.Refresh()
        }
        if(!$proc.HasExited){throw "$case timed out."}
        if(Test-Path -LiteralPath "$run\failed"){throw ([IO.File]::ReadAllText("$run\failed"))}
        if(!(Test-Path -LiteralPath "$run\complete")){throw "$case exited without completion."}
        $text=[IO.File]::ReadAllText("$run\stdout.log")
        $errors=@([regex]::Matches($text,'(?m)^.*ERROR (.+)$') | ForEach-Object {$_.Groups[1].Value.Trim()})
        if($baseline){
            $baselineErrors=@($errors | Where-Object {$_ -notmatch 'Config error in MM_\w+Body: no parts vulnerable to frostbite'})
            $unexpected=@($baselineErrors | Where-Object {$_ -like '*MM_*' -or $_ -like '*BodyValidation*'})
        }else{
            $unexpected=@($errors | Where-Object {$_ -notin $baselineErrors})
        }
        if($unexpected.Count){throw ($case+': unexpected startup errors: '+($unexpected -join "`n"))}
        $checks=@(Get-Content -LiteralPath "$run\assertions.txt")
        if($checks.Count -ne 6){throw 'Expected all six chassis.'}
        $results+=@{case=$case;gameVersion=$version;checks=$checks;startupErrors=$errors}
        Write-Output "$case PASS (six chassis)"
    }finally{
        if(!$proc.HasExited){
            $now=Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.Id)"
            if($now.ExecutablePath -ceq $identity.ExecutablePath -and $now.CommandLine -ceq $identity.CommandLine -and $now.CreationDate -eq $identity.CreationDate){Stop-Process -Id $proc.Id}
        }
    }
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidenceRoot\results.json" -Encoding utf8
Write-Output "VALIDATED $EvidenceRoot"
