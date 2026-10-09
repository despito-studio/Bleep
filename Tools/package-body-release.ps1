param(
    [string]$BuildPath='Builds/Windows-BodyUpdate/DeadAir.exe',
    [string]$ReportPath='Builds/Verification-BodyUpdate/body-runtime-report.txt',
    [string]$OutputPath='Builds/Release-BodyUpdate/DeadAir-Windows-v1.7.10.zip'
)
$ErrorActionPreference='Stop'
$bodyExe=(Resolve-Path -LiteralPath $BuildPath).Path
$bodyRoot=Split-Path -Parent $bodyExe
$bodyReport=(Resolve-Path -LiteralPath $ReportPath).Path
$bodyManifest=Get-Content -LiteralPath (Join-Path $bodyRoot 'build-info.json') -Raw | ConvertFrom-Json
if($bodyManifest.version -ne '1.7.10' -or $bodyManifest.buildResult -ne 'Succeeded' -or
    $bodyManifest.sourceDirty -isnot [bool] -or $bodyManifest.sourceDirty -ne $false -or
    $bodyManifest.sourceCommit -notmatch '^[a-fA-F0-9]{40}$'){throw 'A clean successful v1.7.10 build with source provenance is required.'}
$bodyRepository=Split-Path -Parent $PSScriptRoot
git -C $bodyRepository diff --quiet $bodyManifest.sourceCommit -- Assets Packages ProjectSettings Tools scripts
if($LASTEXITCODE -ne 0){throw 'The built game does not match the current game source.'}
$bodyStatus=git -C $bodyRepository status --porcelain --untracked-files=normal -- Assets Packages ProjectSettings Tools scripts
if($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($bodyStatus -join "`n"))){throw 'Current game source must be committed and clean before packaging.'}
$bodyChecks=@(Get-Content -LiteralPath $bodyReport | Where-Object {-not [string]::IsNullOrWhiteSpace($_)})
if($bodyChecks.Count -ne 3 -or @($bodyChecks | Where-Object {$_ -notlike 'PASSED:*'}).Count -ne 0 -or
    (Get-Item -LiteralPath $bodyReport).LastWriteTimeUtc -lt [DateTime]::Parse($bodyManifest.builtAtUtc).ToUniversalTime()){throw 'The three focused native body checks must pass on this build.'}
$bodyPayload=@{}
foreach($bodyFile in @($bodyManifest.files)){
    $bodyEntry=([string]$bodyFile.path).Replace('\','/')
    $bodySource=[IO.Path]::GetFullPath((Join-Path $bodyRoot $bodyEntry))
    if([string]::IsNullOrWhiteSpace($bodyEntry) -or [IO.Path]::IsPathRooted($bodyEntry) -or $bodyEntry.Contains(':') -or
        $bodyEntry -match '(^|/)\.\.?(/|$)' -or $bodyEntry -in @('build-info.json','START-HERE.txt','body-runtime-report.txt') -or
        $bodyPayload.ContainsKey($bodyEntry) -or -not $bodySource.StartsWith($bodyRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.File]::Exists($bodySource) -or (Get-Item -LiteralPath $bodySource).Length -ne [long]$bodyFile.bytes -or
        $bodyFile.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $bodySource -Algorithm SHA256).Hash -ne $bodyFile.sha256){throw ('Invalid or changed build payload: '+$bodyEntry)}
    $bodyPayload[$bodyEntry]=@{Source=$bodySource;Bytes=[long]$bodyFile.bytes;SHA256=[string]$bodyFile.sha256}
}
foreach($bodyRequired in @('DeadAir.exe','UnityPlayer.dll','DeadAir_Data/Managed/Assembly-CSharp.dll','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')){
    if(-not $bodyPayload.ContainsKey($bodyRequired)){throw ('Missing required runtime payload: '+$bodyRequired)}
}
$bodyZip=[IO.Path]::GetFullPath($OutputPath)
if((Test-Path -LiteralPath $bodyZip) -or (Test-Path -LiteralPath ($bodyZip+'.sha256'))){throw 'The release ZIP or checksum already exists; choose a fresh output path.'}
[IO.Directory]::CreateDirectory((Split-Path -Parent $bodyZip)) | Out-Null
$bodyManifest.runtimeVerification='passed: removed outfit editing and saved preferences, four authored first-person bodies with crouch physics, shared bounded head look and torso follow through real LAN'
$bodyManifestText=$bodyManifest | ConvertTo-Json -Depth 16
$bodyInstructions=@'
DEAD AIR v1.7.10 / WINDOWS x64

1. Розпакуйте ВЕСЬ ZIP в окрему папку (правою кнопкою → Видобути все).
2. Запускайте DeadAir.exe з розпакованої папки.
3. Залишайте DeadAir_Data, MonoBleedingEdge та UnityPlayer.dll поруч із грою.
4. У кооперативі всі учасники повинні мати цей самий реліз.

Не запускайте EXE безпосередньо з архіву: Windows може витягти тільки EXE без потрібних DLL.

Від першої особи видно груди, торс і ноги персонажа в початковому одязі.
Голова стежить за поглядом; при великому розвороті плавно повертається тіло.
Ці рухи синхронізовані в кооперативі. Старі налаштування одягу скидаються.
Tab — журнал завдань та інвентар. Ctrl (утримувати) або C (перемкнути) — присідання.
План будинку висить у фургоні: підійдіть до нього й натисніть E.
Відбиття світла вимикаються в налаштуваннях.

build-info.json містить версію, коміт джерел та контрольні суми файлів.
body-runtime-report.txt містить три перевірки цієї Windows-збірки.
'@
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bodyArchive=[IO.Compression.ZipFile]::Open($bodyZip,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($bodyEntry in ($bodyPayload.Keys | Sort-Object)){
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bodyArchive,$bodyPayload[$bodyEntry].Source,$bodyEntry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach($bodyText in @(@{Name='build-info.json';Text=$bodyManifestText},@{Name='START-HERE.txt';Text=$bodyInstructions},@{Name='body-runtime-report.txt';Text=($bodyChecks -join "`r`n")})){
        $bodyWriter=[IO.StreamWriter]::new($bodyArchive.CreateEntry($bodyText.Name).Open(),[Text.UTF8Encoding]::new($false))
        try{$bodyWriter.Write($bodyText.Text)}finally{$bodyWriter.Dispose()}
    }
}finally{$bodyArchive.Dispose()}
$bodyReader=[IO.Compression.ZipFile]::OpenRead($bodyZip)
try{
    if($bodyReader.Entries.Count -ne $bodyPayload.Count+3){throw 'ZIP entry count differs from the verified build.'}
    foreach($bodyEntry in $bodyPayload.Keys){
        $bodyItem=$bodyReader.GetEntry($bodyEntry)
        if($null -eq $bodyItem -or $bodyItem.Length -ne $bodyPayload[$bodyEntry].Bytes){throw ('Invalid ZIP payload: '+$bodyEntry)}
        $bodyStream=$bodyItem.Open();$bodyAlgorithm=[Security.Cryptography.SHA256]::Create()
        try{$bodyEntryHash=[BitConverter]::ToString($bodyAlgorithm.ComputeHash($bodyStream)).Replace('-','').ToLowerInvariant()}
        finally{$bodyAlgorithm.Dispose();$bodyStream.Dispose()}
        if($bodyEntryHash -ne $bodyPayload[$bodyEntry].SHA256){throw ('ZIP payload hash differs: '+$bodyEntry)}
    }
}finally{$bodyReader.Dispose()}
$bodyHash=(Get-FileHash -LiteralPath $bodyZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($bodyZip+'.sha256',$bodyHash+'  '+[IO.Path]::GetFileName($bodyZip)+"`n",[Text.Encoding]::ASCII)
Write-Output ('Packaged '+$bodyZip+' ('+(Get-Item -LiteralPath $bodyZip).Length+' bytes)')
Write-Output ('SHA256 '+$bodyHash)
