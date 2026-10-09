param(
    [Parameter(Mandatory)][string]$BuildPath,
    [Parameter(Mandatory)][string]$RuntimeAudit,
    [Parameter(Mandatory)][string]$ReleasePath,
    [string]$ExpectedVersion = '1.7.7'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-AudioReleaseStreamHash([IO.Stream]$Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-AudioReleaseFileHash([string]$Path) {
    $fileStream = [IO.File]::OpenRead($Path)
    try { return Get-AudioReleaseStreamHash $fileStream }
    finally { $fileStream.Dispose() }
}

function Write-AudioReleaseZipText([IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Text) {
    $entry = $Archive.CreateEntry($Name, [IO.Compression.CompressionLevel]::Fastest)
    $entryStream = $entry.Open()
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
        $entryStream.Write($bytes, 0, $bytes.Length)
    }
    finally { $entryStream.Dispose() }
}

if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'ExpectedVersion must be a numeric release version.' }
$buildDirectory = [IO.Path]::GetFullPath($BuildPath).TrimEnd([char[]]'\/')
$releaseDirectory = [IO.Path]::GetFullPath($ReleasePath)
$manifestPath = Join-Path $buildDirectory 'build-info.json'
$auditPath = [IO.Path]::GetFullPath($RuntimeAudit)
if (-not [IO.File]::Exists($manifestPath) -or -not [IO.File]::Exists($auditPath)) {
    throw 'The fresh build manifest and device audio audit must both exist.'
}
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestHash = Get-AudioReleaseFileHash $manifestPath
$audit = Get-Content -Raw -LiteralPath $auditPath | ConvertFrom-Json
$auditHash = Get-AudioReleaseFileHash $auditPath
if ($manifest.schemaVersion -ne 2 -or $manifest.version -ne $ExpectedVersion -or
    $manifest.buildResult -ne 'Succeeded' -or $manifest.sourceDirty -isnot [bool] -or
    $manifest.sourceDirty -ne $false -or $manifest.sourceCommit -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'Release requires a successful version-matched build from a clean 40-character source commit.'
}
if ($audit.passed -isnot [bool] -or $audit.passed -ne $true -or @($audit.checks).Count -lt 134 -or
    $audit.buildManifestSHA256 -ne $manifestHash) {
    throw 'Device audio audit must pass at least 134 checks and reference this exact build-info.json SHA-256.'
}

$zipName = 'DeadAir-Windows-v' + $ExpectedVersion + '.zip'
$zipPath = Join-Path $releaseDirectory $zipName
$hashPath = $zipPath + '.sha256'
$auditName = 'v' + $ExpectedVersion + '-audio-verification.json'
$publishedAuditPath = Join-Path $releaseDirectory $auditName
foreach ($newOutput in @($zipPath, $hashPath, $publishedAuditPath)) {
    if (Test-Path -LiteralPath $newOutput) { throw ('Refusing to replace existing release output: ' + $newOutput) }
}

$expectedFiles = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$buildPrefix = $buildDirectory + [IO.Path]::DirectorySeparatorChar
foreach ($file in @($manifest.files)) {
    $entryName = ([string]$file.path).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($entryName) -or [IO.Path]::IsPathRooted($entryName) -or
        $entryName -match '(^|/)\.\.?(/|$)' -or $entryName.EndsWith('/') -or
        $entryName.Contains(':') -or $entryName -in @('build-info.json', 'START-HERE.txt', 'CREDITS-AUDIO.txt')) {
        throw ('Invalid or reserved manifest path: ' + $entryName)
    }
    $sourceFile = [IO.Path]::GetFullPath((Join-Path $buildDirectory $entryName))
    if (-not $sourceFile.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Manifest file escaped the build directory: ' + $entryName)
    }
    if ($expectedFiles.ContainsKey($entryName)) { throw ('Duplicate manifest path: ' + $entryName) }
    if (-not [IO.File]::Exists($sourceFile) -or (Get-Item -LiteralPath $sourceFile).Length -ne [long]$file.bytes -or
        $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-AudioReleaseFileHash $sourceFile) -ne $file.sha256) {
        throw ('Build payload no longer matches its manifest: ' + $entryName)
    }
    $expectedFiles.Add($entryName, [pscustomobject]@{
        source = $sourceFile; bytes = [long]$file.bytes; sha256 = ([string]$file.sha256).ToLowerInvariant()
    })
}
$requiredFiles = @(
    'DeadAir.exe', 'UnityPlayer.dll', 'DeadAir_Data/Managed/Assembly-CSharp.dll',
    'DeadAir_Data/level0', 'DeadAir_Data/globalgamemanagers', 'DeadAir_Data/resources.assets',
    'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'
)
foreach ($required in $requiredFiles) {
    if (-not $expectedFiles.ContainsKey($required)) { throw ('Missing required runtime payload: ' + $required) }
}

$manifest.runtimeVerification = 'passed: fresh Windows device audio playback, adaptive music, pause/resume, mute, scare duck, protected role/fall voices and loop synchronization'
$manifest | Add-Member -NotePropertyName testedBuildManifestSHA256 -NotePropertyValue $manifestHash -Force
$manifest | Add-Member -NotePropertyName audioRuntimeReportSHA256 -NotePropertyValue $auditHash -Force
$manifest | Add-Member -NotePropertyName audioRuntimeReport -NotePropertyValue $auditName -Force
$packagedManifestText = ($manifest | ConvertTo-Json -Depth 30) + "`n"
$packagedManifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($packagedManifestText)
$manifestBytesStream = [IO.MemoryStream]::new($packagedManifestBytes, $false)
try { $packagedManifestHash = Get-AudioReleaseStreamHash $manifestBytesStream }
finally { $manifestBytesStream.Dispose() }
$startText = @"
Dead Air: Blackpine Broadcast — v$ExpectedVersion — Windows x64

УКРАЇНСЬКОЮ
1. Розпакуйте весь ZIP у звичайну папку.
2. Залиште DeadAir.exe, UnityPlayer.dll, DeadAir_Data та MonoBleedingEdge разом.
3. Запустіть DeadAir.exe із розпакованої папки. Не запускайте його всередині ZIP.

ENGLISH
1. Extract the entire ZIP to a normal folder.
2. Keep DeadAir.exe, UnityPlayer.dll, DeadAir_Data and MonoBleedingEdge together.
3. Launch DeadAir.exe from the extracted folder, not from inside the ZIP.

Source commit: $($manifest.sourceCommit)
Audio verification: $auditName (available alongside this release archive).
This release's device audio verification covers 134 or more technical checks.
See build-info.json for source provenance and payload hashes.
"@
$creditsPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Docs/AudioSources.md'
$creditsText = if ([IO.File]::Exists($creditsPath)) { [IO.File]::ReadAllText($creditsPath) } else { $null }

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($entryName in ($expectedFiles.Keys | Sort-Object)) {
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Fastest)
            $entryStream = $entry.Open()
            try {
                $payloadStream = [IO.File]::OpenRead($expectedFiles[$entryName].source)
                try { $payloadStream.CopyTo($entryStream) }
                finally { $payloadStream.Dispose() }
            }
            finally { $entryStream.Dispose() }
        }
        Write-AudioReleaseZipText $archive 'build-info.json' $packagedManifestText
        Write-AudioReleaseZipText $archive 'START-HERE.txt' $startText
        if ($null -ne $creditsText) { Write-AudioReleaseZipText $archive 'CREDITS-AUDIO.txt' $creditsText }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

# Read back every payload from the completed archive, not just its directory.
$reader = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $expectedEntryCount = $expectedFiles.Count + 2 + [int]($null -ne $creditsText)
    if ($reader.Entries.Count -ne $expectedEntryCount) { throw 'ZIP entry count differs from the verified release payload.' }
    foreach ($entryName in $expectedFiles.Keys) {
        $entry = $reader.GetEntry($entryName)
        if ($null -eq $entry -or $entry.Length -ne $expectedFiles[$entryName].bytes) {
            throw ('ZIP payload missing or has incorrect length: ' + $entryName)
        }
        $entryStream = $entry.Open()
        try {
            if ((Get-AudioReleaseStreamHash $entryStream) -ne $expectedFiles[$entryName].sha256) {
                throw ('ZIP payload SHA-256 mismatch: ' + $entryName)
            }
        }
        finally { $entryStream.Dispose() }
    }
    $manifestEntry = $reader.GetEntry('build-info.json')
    if ($null -eq $manifestEntry) { throw 'Packaged build-info.json is missing.' }
    $entryStream = $manifestEntry.Open()
    try {
        if ((Get-AudioReleaseStreamHash $entryStream) -ne $packagedManifestHash) { throw 'Packaged build-info.json SHA-256 mismatch.' }
    }
    finally { $entryStream.Dispose() }
    if ($null -eq $reader.GetEntry('START-HERE.txt') -or
        ($null -ne $creditsText -and $null -eq $reader.GetEntry('CREDITS-AUDIO.txt'))) {
        throw 'Release instructions or audio credits are missing.'
    }
}
finally { $reader.Dispose() }

# Keep the measured build's original manifest intact; only the ZIP copy gains verification fields.
if ((Get-AudioReleaseFileHash $auditPath) -ne $auditHash) { throw 'The audio report changed during packaging.' }
[IO.File]::Copy($auditPath, $publishedAuditPath, $false)
$zipHash = Get-AudioReleaseFileHash $zipPath
[IO.File]::WriteAllText($hashPath, $zipHash + '  ' + $zipName + "`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    archive = $zipPath; sha256 = $zipHash; verification = $publishedAuditPath
    sourceCommit = $manifest.sourceCommit; payloadFiles = $expectedFiles.Count
    bytes = (Get-Item -LiteralPath $zipPath).Length
} | ConvertTo-Json -Compress
