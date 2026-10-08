param(
    [Parameter(Mandatory)][string]$BuildPath,
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$UpdateReport,
    [Parameter(Mandatory)][string]$GeometryReport,
    [Parameter(Mandatory)][string]$RulesReport,
    [string]$ExpectedVersion = '1.7.4'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$buildDirectory = [IO.Path]::GetFullPath($BuildPath).TrimEnd('\', '/')
$releaseDirectory = [IO.Path]::GetFullPath($ReleasePath)
$manifestPath = Join-Path $buildDirectory 'build-info.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.schemaVersion -ne 2 -or $manifest.version -ne $ExpectedVersion -or $manifest.buildResult -ne 'Succeeded') {
    throw 'Fresh successful build metadata does not match the requested release.'
}
$updateText = Get-Content -Raw -LiteralPath $UpdateReport
$geometryText = Get-Content -Raw -LiteralPath $GeometryReport
$rulesText = Get-Content -Raw -LiteralPath $RulesReport
foreach ($reportPath in @($UpdateReport, $GeometryReport, $RulesReport)) {
    if ((Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt [DateTimeOffset]::Parse($manifest.builtAtUtc).UtcDateTime) {
        throw 'A verification report predates this build.'
    }
    $reportText = Get-Content -Raw -LiteralPath $reportPath
    if ($reportText -notmatch ('(?m)^(?:BuildManifestSHA256|BUILD-INFO-SHA256):\s*' + $manifestHash + '\s*$')) {
        throw 'A verification report belongs to a different build manifest.'
    }
}
if ($updateText -notmatch ('^PASSED: v' + [regex]::Escape($ExpectedVersion) + ';')) {
    throw 'Original player runtime report has the wrong version or did not pass.'
}
if ($geometryText -match '(?m)^FAIL:' -or $geometryText -notmatch '(?m)^COMPLETE: failures=0\s*$' -or
    $geometryText -notmatch '(?m)^PASS: Fuse counts fits without vertical clipping\.' -or
    $geometryText -notmatch '(?m)^PASS: All three evidence HUD rows occupy separate nonoverlapping vertical slots\.') {
    throw 'Geometry and HUD verification did not pass.'
}
if ($rulesText -notmatch '^Rule audit summary: [1-9]\d* passed, 0 failed\.') {
    throw 'Native rules verification did not pass.'
}
$runtimeChecks = foreach ($check in @($UpdateReport, $GeometryReport, $RulesReport)) {
    [pscustomobject]@{
        report = [IO.Path]::GetFileName($check)
        sha256 = (Get-FileHash -LiteralPath $check -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$zipPath = Join-Path $releaseDirectory ('DeadAir-Windows-v' + $ExpectedVersion + '.zip')
$hashPath = $zipPath + '.sha256'
foreach ($path in @($zipPath, $hashPath)) {
    if (Test-Path -LiteralPath $path) { throw ('Refusing to replace an existing release: ' + $path) }
}
$expectedFiles = @{}
foreach ($file in $manifest.files) {
    $sourceFile = [IO.Path]::GetFullPath((Join-Path $buildDirectory $file.path))
    if (-not $sourceFile.StartsWith($buildDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Manifest file escaped the build directory.'
    }
    if ((Get-Item -LiteralPath $sourceFile).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw ('Build file no longer matches its fresh manifest: ' + $file.path)
    }
    $expectedFiles[$file.path] = @{ bytes = $file.bytes; sha256 = $file.sha256; source = $sourceFile }
}
foreach ($required in @('DeadAir.exe', 'UnityPlayer.dll', 'DeadAir_Data/Managed/Assembly-CSharp.dll', 'DeadAir_Data/level0', 'DeadAir_Data/globalgamemanagers', 'DeadAir_Data/resources.assets', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')) {
    if (-not $expectedFiles.ContainsKey($required)) { throw ('Missing required runtime file: ' + $required) }
}
$manifest.runtimeVerification = 'passed: original GPU player, native canister/HUD/geometry checks, rules and loopback LAN'
$manifest | Add-Member -NotePropertyName runtimeReports -NotePropertyValue @($runtimeChecks) -Force
$manifest | Add-Member -NotePropertyName testedBuildManifestSHA256 -NotePropertyValue $manifestHash -Force
$verifiedManifestText = ($manifest | ConvertTo-Json -Depth 12) + "`n"
$verifiedManifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($verifiedManifestText)
$manifestSha = [Security.Cryptography.SHA256]::Create()
try { $verifiedManifestHash = [BitConverter]::ToString($manifestSha.ComputeHash($verifiedManifestBytes)).Replace('-', '').ToLowerInvariant() }
finally { $manifestSha.Dispose() }
$expectedFiles['build-info.json'] = @{ bytes = $verifiedManifestBytes.LongLength; sha256 = $verifiedManifestHash }
$stagingZip = Join-Path $releaseDirectory ([IO.Path]::GetFileNameWithoutExtension($zipPath) + '-' + [guid]::NewGuid().ToString('N') + '.zip.partial')
$archive = [IO.Compression.ZipFile]::Open($stagingZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryName in ($expectedFiles.Keys | Sort-Object)) {
        if ($entryName -eq 'build-info.json') {
            $manifestEntry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $manifestStream = $manifestEntry.Open()
            try { $manifestStream.Write($verifiedManifestBytes, 0, $verifiedManifestBytes.Length) }
            finally { $manifestStream.Dispose() }
            continue
        }
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $expectedFiles[$entryName].source, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
    $startEntry = $archive.CreateEntry('START-HERE.txt', [IO.Compression.CompressionLevel]::Optimal)
    $writer = [IO.StreamWriter]::new($startEntry.Open(), [Text.UTF8Encoding]::new($false))
    try {
        $writer.Write("DEAD AIR v$ExpectedVersion - Windows x64`r`n`r`nРозпакуйте весь ZIP і запустіть DeadAir.exe.`r`nЗалиште DeadAir_Data, MonoBleedingEdge та DLL поруч.`r`n`r`nWASD - рух; E - взаємодія; F - ліхтар; TAB / I - інвентар; R - використати; Esc - пауза.`r`nДля LAN усі учасники мають використовувати цю версію.`r`n")
    } finally { $writer.Dispose() }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($stagingZip)
try {
    if ($archive.Entries.Count -ne $expectedFiles.Count + 1) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -eq 'START-HERE.txt') { continue }
        if (-not $expectedFiles.ContainsKey($entry.FullName)) { throw ('Unexpected archive entry: ' + $entry.FullName) }
        $expected = $expectedFiles[$entry.FullName]
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($entry.Length -ne $expected.bytes -or $actualHash -ne $expected.sha256) {
            throw ('Archive payload mismatch: ' + $entry.FullName)
        }
    }
} finally { $archive.Dispose() }
[IO.File]::Move($stagingZip, $zipPath)
[IO.File]::WriteAllText($manifestPath, $verifiedManifestText, [Text.UTF8Encoding]::new($false))
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashPath, $zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + "`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ zip = $zipPath; bytes = (Get-Item -LiteralPath $zipPath).Length; sha256 = $zipHash; verifiedFiles = $expectedFiles.Count } | ConvertTo-Json
