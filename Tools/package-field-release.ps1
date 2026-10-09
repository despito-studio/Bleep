param(
    [string]$BuildPath='Builds/Windows-FieldUpdate/DeadAir.exe',
    [string]$ReportPath='Builds/Verification-FieldUpdate/field-runtime-report.txt',
    [string]$OutputPath='Builds/Release-FieldUpdate/DeadAir-Windows-v1.7.9.zip'
)
$ErrorActionPreference='Stop'
$fieldExe=(Resolve-Path -LiteralPath $BuildPath).Path
$fieldRoot=Split-Path -Parent $fieldExe
$fieldReport=(Resolve-Path -LiteralPath $ReportPath).Path
$fieldManifest=Get-Content -LiteralPath (Join-Path $fieldRoot 'build-info.json') -Raw | ConvertFrom-Json
if($fieldManifest.version -ne '1.7.9' -or $fieldManifest.buildResult -ne 'Succeeded' -or $fieldManifest.sourceDirty){throw 'A clean successful v1.7.9 build is required.'}
git diff --quiet $fieldManifest.sourceCommit HEAD -- Assets Packages ProjectSettings scripts
if($LASTEXITCODE -ne 0){throw 'The built game does not match the current game source.'}
$fieldChecks=Get-Content -LiteralPath $fieldReport
if(@($fieldChecks | Where-Object {$_ -like 'PASSED:*'}).Count -ne 6 -or (Get-Item -LiteralPath $fieldReport).LastWriteTimeUtc -lt [DateTime]::Parse($fieldManifest.builtAtUtc).ToUniversalTime()){throw 'The six focused native checks must pass on this build.'}
$fieldZip=[IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $fieldZip)) | Out-Null
if(Test-Path -LiteralPath $fieldZip){throw 'The release ZIP already exists; choose a fresh output path.'}
$fieldManifest.runtimeVerification='passed: rendered Windows arrival, map and journal, actual van boarding, crouch physics, reflections, LAN clothing/crouch/departure and extracted loot'
$fieldManifestText=$fieldManifest | ConvertTo-Json -Depth 16
$fieldInstructions=@'
DEAD AIR v1.7.9 / WINDOWS x64

1. Розпакуйте ВЕСЬ ZIP в окрему папку (правою кнопкою → Видобути все).
2. Запускайте DeadAir.exe з розпакованої папки.
3. Залишайте DeadAir_Data, MonoBleedingEdge та UnityPlayer.dll поруч із грою.
4. У кооперативі всі учасники повинні мати цей самий реліз.

Не запускайте EXE безпосередньо з архіву: Windows може витягти тільки EXE без потрібних DLL.

Tab — журнал завдань та інвентар. Ctrl або C — присідання.
План будинку висить у фургоні: підійдіть до нього й натисніть E.
Одяг персонажів змінюється в лобі. Відбиття світла вимикаються в налаштуваннях.

build-info.json містить версію, коміт джерел та контрольні суми файлів.
'@
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fieldArchive=[IO.Compression.ZipFile]::Open($fieldZip,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($fieldFile in $fieldManifest.files){
        $fieldEntry=[string]$fieldFile.path
        $fieldSource=[IO.Path]::GetFullPath((Join-Path $fieldRoot $fieldEntry))
        if(-not $fieldSource.StartsWith($fieldRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $fieldSource)){throw ('Invalid build payload: '+$fieldEntry)}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($fieldArchive,$fieldSource,$fieldEntry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach($fieldText in @(@{Name='build-info.json';Text=$fieldManifestText},@{Name='START-HERE.txt';Text=$fieldInstructions},@{Name='field-runtime-report.txt';Text=($fieldChecks -join "`r`n")})){
        $fieldWriter=[IO.StreamWriter]::new($fieldArchive.CreateEntry($fieldText.Name).Open(),[Text.UTF8Encoding]::new($false))
        try{$fieldWriter.Write($fieldText.Text)}finally{$fieldWriter.Dispose()}
    }
}finally{$fieldArchive.Dispose()}
$fieldHash=(Get-FileHash -LiteralPath $fieldZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($fieldZip+'.sha256',$fieldHash+'  '+[IO.Path]::GetFileName($fieldZip)+"`n",[Text.Encoding]::ASCII)
Write-Output ('Packaged '+$fieldZip+' ('+(Get-Item -LiteralPath $fieldZip).Length+' bytes)')
Write-Output ('SHA256 '+$fieldHash)
