param(
    [Parameter(Mandatory)][string]$BuildPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$buildDirectory = [IO.Path]::GetFullPath($BuildPath).TrimEnd('\', '/')
$outputDirectory = [IO.Path]::GetFullPath($OutputPath)
$manifestPath = Join-Path $buildDirectory 'build-info.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.buildResult -ne 'Succeeded') { throw 'Build did not complete successfully.' }
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
function Assert-ManifestFiles {
    if ($null -eq $manifest.files -or @($manifest.files).Count -eq 0) { throw 'Build manifest contains no runtime files.' }
    foreach ($file in $manifest.files) {
        $sourceFile = [IO.Path]::GetFullPath((Join-Path $buildDirectory $file.path))
        if (-not $sourceFile.StartsWith($buildDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Manifest file escaped the build directory.'
        }
        $actualFile = Get-Item -LiteralPath $sourceFile
        if ($actualFile.PSIsContainer -or $actualFile.Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
            throw ('Runtime file disagrees with the tested build manifest: ' + $file.path)
        }
    }
}
Assert-ManifestFiles
$reportPath = Join-Path $outputDirectory 'update-runtime-report.txt'
if (Test-Path -LiteralPath $reportPath) { throw 'Use a fresh verification directory; an old report already exists.' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('-batchmode', '-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"{0}"' -f (Join-Path $outputDirectory 'Player.log')), '-deadAirUpdateCapture', ('"{0}"' -f $outputDirectory))
$process = Start-Process -FilePath (Join-Path $buildDirectory 'DeadAir.exe') -WorkingDirectory $buildDirectory -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Background GPU verification PID: ' + $process.Id)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw 'Background GPU verification timed out.'
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) { throw 'Background GPU verification did not pass.' }
$report = Get-Content -Raw -LiteralPath $reportPath
if ($report -notmatch ('^PASSED: v' + [regex]::Escape($manifest.version) + ';')) { throw 'Runtime version disagrees with build metadata.' }
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifestHash) {
    throw 'Build changed while the runtime verification was running.'
}
Assert-ManifestFiles
Add-Content -LiteralPath $reportPath -Value ('BuildManifestSHA256: ' + $manifestHash) -Encoding utf8
Write-Output ('PASSED: original background GPU player v' + $manifest.version + '; manifest ' + $manifestHash)
