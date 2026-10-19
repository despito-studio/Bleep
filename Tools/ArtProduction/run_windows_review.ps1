param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$EnableReflections
)

$ErrorActionPreference = 'Stop'
$reviewExecutable = (Resolve-Path -LiteralPath $Executable).Path
$reviewOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $reviewOutput) { throw 'Choose a new output directory to keep each review run independent.' }
if (Get-NetTCPConnection -LocalPort 32004 -State Listen -ErrorAction SilentlyContinue) {
    throw 'TCP port 32004 is already in use.'
}
New-Item -ItemType Directory -Path $reviewOutput | Out-Null
$reviewArguments = @(
    '-force-d3d11', '-screen-fullscreen', '0',
    '-screen-width', '1280', '-screen-height', '720',
    '-deadAirBlackpineCapture', ('"' + $reviewOutput + '"'),
    '-logFile', ('"' + (Join-Path $reviewOutput 'Player.log') + '"')
)
if ($EnableReflections) { $reviewArguments += '-deadAirArtReviewReflections' }
$reviewProcess = Start-Process -FilePath $reviewExecutable -ArgumentList $reviewArguments -WindowStyle Hidden -PassThru
$reviewProcess.Id | Set-Content -LiteralPath (Join-Path $reviewOutput 'process-id.txt')
$reviewClock = [Diagnostics.Stopwatch]::StartNew()
while (-not $reviewProcess.HasExited -and $reviewClock.Elapsed.TotalSeconds -lt 300) {
    Start-Sleep -Seconds 1
    $reviewProcess.Refresh()
}
if (-not $reviewProcess.HasExited) {
    Stop-Process -Id $reviewProcess.Id
    throw 'The review player exceeded its 300-second outer timeout.'
}
$reviewFailures = [Collections.Generic.List[string]]::new()
if ($reviewProcess.ExitCode -ne 0) { $reviewFailures.Add('The player returned a failing exit code.') }
$reviewReportPath = Join-Path $reviewOutput 'blackpine-runtime-report.txt'
if (-not (Test-Path -LiteralPath $reviewReportPath) -or
    -not ((Get-Content -LiteralPath $reviewReportPath -Raw) -match '^PASSED:')) {
    $reviewFailures.Add('The player did not produce a passing runtime report.')
}
$reviewLogPath = Join-Path $reviewOutput 'Player.log'
$reviewNegativeScaleWarnings = 0
$reviewNonfiniteGeometryWarnings = 0
if (Test-Path -LiteralPath $reviewLogPath) {
    $reviewLog = Get-Content -LiteralPath $reviewLogPath -Raw
    $reviewNegativeScaleWarnings = [regex]::Matches($reviewLog, '(?im)(Box|Capsule|Sphere|Mesh)Collider[^\r\n]*negative (scale|size)').Count
    if ($reviewNegativeScaleWarnings -gt 0) { $reviewFailures.Add('PhysX reported unsupported negative collider scale or size.') }
    $reviewNonfiniteGeometryWarnings = [regex]::Matches($reviewLog, '(?im)abnormal mesh bounds|Invalid(?:\s+world)?AABB|Mesh bounds[^\r\n]*(?:\bnan\b|infinity|inifinity)').Count
    if ($reviewNonfiniteGeometryWarnings -gt 0) { $reviewFailures.Add('The player reported nonfinite mesh geometry or bounds.') }
}
if ($EnableReflections) {
    $reviewProbePath = Join-Path $reviewOutput 'blackpine-runtime-probes.json'
    if (-not (Test-Path -LiteralPath $reviewProbePath)) {
        $reviewFailures.Add('The requested reflection diagnostics are missing.')
    } else {
        $reviewProbes = Get-Content -LiteralPath $reviewProbePath -Raw | ConvertFrom-Json
        if (-not $reviewProbes.processOnlyReflectionOverride -or -not $reviewProbes.savedPreferenceUnchanged) {
            $reviewFailures.Add('The reflection override or saved-preference preservation was not confirmed.')
        }
        if (@($reviewProbes.captures).Count -ne 2) { $reviewFailures.Add('Both chapters must provide actual reflection diagnostics.') }
        foreach ($reviewProbe in $reviewProbes.captures) {
            $reviewActual = $reviewProbe.actual
            if (-not $reviewProbe.reflectionsEnabled -or $reviewActual.completedRealtimeProbes -lt 1 -or
                $reviewActual.renderedFaceCallbacks -lt 1 -or $reviewActual.faceScopeOpen -or
                $reviewActual.allocatedProbes -gt 2 -or $reviewActual.maximumResolution -gt 128) {
                $reviewFailures.Add('Actual bounded runtime reflection capture was not confirmed for ' + $reviewProbe.chapter + '.')
            }
        }
    }
}
$reviewExitCode = if ($reviewFailures.Count -eq 0) { 0 } else { 1 }
$reviewResult = [ordered]@{
    executable = $reviewExecutable
    output = $reviewOutput
    playerExitCode = $reviewProcess.ExitCode
    exitCode = $reviewExitCode
    elapsedSeconds = [math]::Round($reviewClock.Elapsed.TotalSeconds, 1)
    processOnlyReflectionOverrideRequested = [bool]$EnableReflections
    automaticPlayerRenderLoop = $true
    negativeColliderScaleWarnings = $reviewNegativeScaleWarnings
    nonfiniteGeometryWarnings = $reviewNonfiniteGeometryWarnings
    validationFailures = $reviewFailures.ToArray()
}
$reviewResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reviewOutput 'launch-result.json')
$reviewResult | ConvertTo-Json
exit $reviewExitCode
