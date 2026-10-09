param(
    [string]$BuildPath='Builds/Windows-FieldUpdate/DeadAir.exe',
    [string]$OutputPath='Builds/Verification-FieldUpdate'
)
$ErrorActionPreference='Stop'
$fieldExe=(Resolve-Path -LiteralPath $BuildPath).Path
$fieldOutput=[IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory($fieldOutput) | Out-Null
$fieldLog=Join-Path $fieldOutput 'Player.log'
$fieldArgs=@('-batchmode','-force-d3d11','-screen-width','1280','-screen-height','720','-deadAirFieldCapture',('"'+$fieldOutput+'"'),'-logFile',('"'+$fieldLog+'"'))
$fieldPlayer=Start-Process -FilePath $fieldExe -ArgumentList $fieldArgs -WindowStyle Hidden -PassThru
Write-Output ('Native field update capture PID '+$fieldPlayer.Id)
if(-not $fieldPlayer.WaitForExit(420000)){
    Stop-Process -Id $fieldPlayer.Id -ErrorAction SilentlyContinue
    throw ('Native capture timed out; inspect '+$fieldLog)
}
if($fieldPlayer.ExitCode -ne 0){throw ('Native capture failed with exit '+$fieldPlayer.ExitCode+'; inspect '+$fieldLog)}
$fieldReport=Join-Path $fieldOutput 'field-runtime-report.txt'
if(-not(Test-Path -LiteralPath $fieldReport)){throw 'Native player exited without a field report.'}
Get-Content -LiteralPath $fieldReport
