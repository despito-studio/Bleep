param(
    [Parameter(Mandatory)][string]$BuildPath,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Resolve-WorkspacePath([string]$path) {
    if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $workspace $path }
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\', '/')
    if (-not $full.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Audit path escaped the workspace.' }
    $ancestor = $full
    while ($ancestor -and $ancestor.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Audit path has a reparse-point ancestor.' }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
$buildDirectory = Resolve-WorkspacePath $BuildPath
$auditDirectory = Resolve-WorkspacePath $OutputPath
$prepared = Get-Content -Raw -LiteralPath (Join-Path $auditDirectory 'prepared-audit.json') | ConvertFrom-Json
$copiedPlayer = Resolve-WorkspacePath (Join-Path $auditDirectory 'Player')
$outputs = Resolve-WorkspacePath (Join-Path $auditDirectory 'Results')
if ($prepared.schemaVersion -ne 1 -or @($prepared.files).Count -eq 0 -or $prepared.buildPath -ne $buildDirectory -or
    $prepared.playerPath -ne $copiedPlayer -or $prepared.resultsPath -ne $outputs) { throw 'Prepared native audit belongs to a different build or output directory.' }
$manifestPath = Join-Path $buildDirectory 'build-info.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.buildResult -ne 'Succeeded' -or $manifest.version -ne $prepared.version -or
    (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $prepared.buildManifestSHA256) { throw 'Original build changed after native audit preparation.' }
foreach ($file in $manifest.files) {
    $path = Resolve-WorkspacePath (Join-Path $buildDirectory $file.path)
    if (-not $path.StartsWith($buildDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Build file escaped the source player.' }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.PSIsContainer -or $item.Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Original runtime file changed: $($file.path)" }
}
if (Test-Path -LiteralPath $outputs) { throw 'Results already exist; prepare a fresh immutable audit copy.' }
foreach ($file in $prepared.files) {
    $path = Resolve-WorkspacePath (Join-Path $copiedPlayer $file.path)
    if (-not $path.StartsWith($copiedPlayer + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Prepared file escaped the copied player.' }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.PSIsContainer -or $item.Attributes -band [IO.FileAttributes]::ReparsePoint -or $item.Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Immutable audit player changed: $($file.path)" }
}
$executable = Join-Path $copiedPlayer 'DeadAir.exe'
$helper = Join-Path $copiedPlayer 'DeadAir_Data\Managed\RuntimeAudit.dll'
if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath $helper)) { throw 'Prepare the native audit copy before launching it.' }
[IO.Directory]::CreateDirectory($outputs) | Out-Null
$arguments = @('-batchmode', '-force-d3d11', '-screen-width', '1280', '-screen-height', '720', '-deadAirGeometryAudit', ('"{0}"' -f $outputs), '-logFile', ('"{0}"' -f (Join-Path $outputs 'Player.log')))
$process = Start-Process -FilePath $executable -WorkingDirectory $copiedPlayer -ArgumentList $arguments -WindowStyle Hidden -PassThru
[IO.File]::WriteAllText((Join-Path $outputs 'process-id.txt'), [string]$process.Id)
[pscustomobject]@{ processId = $process.Id; player = $executable; results = $outputs; version = $prepared.version; buildManifestSHA256 = $prepared.buildManifestSHA256 } | ConvertTo-Json
