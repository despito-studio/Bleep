param(
    [string]$BuildPath='Builds/Windows-BodyUpdate/DeadAir.exe',
    [string]$OutputPath='Builds/Verification-BodyUpdate'
)
$ErrorActionPreference='Stop'
$bodyExe=(Resolve-Path -LiteralPath $BuildPath).Path
$bodyOutput=[IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory($bodyOutput) | Out-Null
$bodyLog=Join-Path $bodyOutput 'Player.log'
$bodyArgs=@('-batchmode','-force-d3d11','-screen-width','1280','-screen-height','720','-deadAirBodyCapture',('"'+$bodyOutput+'"'),'-logFile',('"'+$bodyLog+'"'))
$bodyPlayer=Start-Process -FilePath $bodyExe -ArgumentList $bodyArgs -WindowStyle Hidden -PassThru
Write-Output ('Native first-person body capture PID '+$bodyPlayer.Id)
if(-not $bodyPlayer.WaitForExit(180000)){
    Stop-Process -Id $bodyPlayer.Id -ErrorAction SilentlyContinue
    throw ('Native capture timed out; inspect '+$bodyLog)
}
if($bodyPlayer.ExitCode -ne 0){throw ('Native capture failed with exit '+$bodyPlayer.ExitCode+'; inspect '+$bodyLog)}
$bodyReport=Join-Path $bodyOutput 'body-runtime-report.txt'
if(-not(Test-Path -LiteralPath $bodyReport)){throw 'Native player exited without a body report.'}
Get-Content -LiteralPath $bodyReport
