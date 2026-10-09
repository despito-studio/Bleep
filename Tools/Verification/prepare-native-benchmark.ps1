param(
    [Parameter(Mandatory=$true)][string]$BuildPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$HelperAssemblyPath,
    [string]$CecilPath,
    [switch]$RefreshHelper
)
$ErrorActionPreference = 'Stop'
$sourcePlayer = (Resolve-Path -LiteralPath $BuildPath).Path
$outputRoot = [IO.Path]::GetFullPath($OutputPath)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$copyPlayer = Join-Path $outputRoot 'Player'
$resultDirectory = Join-Path $outputRoot 'Results'
$sourcePrefix = $sourcePlayer.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($outputRoot.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase) -or $outputRoot -eq $sourcePlayer) {
    throw 'Benchmark output must be outside the original player directory.'
}
$copyExists = Test-Path -LiteralPath $copyPlayer
if ($copyExists -and -not $RefreshHelper) { throw "Refusing to overwrite benchmark copy: $copyPlayer" }
if ($RefreshHelper -and -not $copyExists) { throw 'No existing copy to refresh.' }
$managedDirectory = Join-Path $sourcePlayer 'DeadAir_Data\Managed'
$gamePath = Join-Path $managedDirectory 'Assembly-CSharp.dll'
$manifestPath = Join-Path $sourcePlayer 'build-info.json'
if (-not (Test-Path -LiteralPath $gamePath) -or -not (Test-Path -LiteralPath (Join-Path $sourcePlayer 'DeadAir.exe'))) {
    throw 'Supply an original Windows Mono player build.'
}
$gameHash = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$record = @($manifest.files | Where-Object path -eq 'DeadAir_Data/Managed/Assembly-CSharp.dll')
if ($record.Count -ne 1 -or $record[0].sha256 -ne $gameHash) { throw 'Original game DLL does not match its build manifest.' }
if ($RefreshHelper) {
    $existing = Get-Content -LiteralPath (Join-Path $resultDirectory 'provenance.json') -Raw | ConvertFrom-Json
    if ($existing.sourceGameSHA256 -ne $gameHash -or $existing.sourceManifestSHA256 -ne $manifestHash) {
        throw 'The original build changed since this copy was prepared; create a new output directory.'
    }
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$helperSource = Join-Path $PSScriptRoot 'NativeBenchmark.cs'
$helperAssembly = Join-Path $outputRoot 'NativeBenchmark.dll'
if ($HelperAssemblyPath) {
    # Reuse the exact baseline binary for an optimized copy when desired.
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $HelperAssemblyPath).Path -Destination $helperAssembly -Force
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdk = & $dotnet --list-sdks | ForEach-Object {
        if ($_ -match '^([0-9.]+)(?:-[^ ]+)? \[(.+)\]$') {
            $candidate = Join-Path (Join-Path $Matches[2] ($_ -split ' ')[0]) 'Roslyn\bincore\csc.dll'
            if (Test-Path -LiteralPath $candidate) { [pscustomobject]@{ Version=[version]$Matches[1]; Compiler=$candidate } }
        }
    } | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'No installed .NET SDK C# compiler was found.' }
    $responseFile = Join-Path $outputRoot 'compile-native.rsp'
    $compileArguments = @('-nologo', '-nostdlib+', '-target:library', '-langversion:latest', '-nowarn:0618', ('-out:"{0}"' -f $helperAssembly))
    $compileArguments += Get-ChildItem -LiteralPath $managedDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object { '-reference:"{0}"' -f $_.FullName }
    $compileArguments += ('"{0}"' -f $helperSource)
    [IO.File]::WriteAllLines($responseFile, $compileArguments)
    $previousGcServer = [Environment]::GetEnvironmentVariable('DOTNET_gcServer','Process')
    $previousHeapLimit = [Environment]::GetEnvironmentVariable('DOTNET_GCHeapHardLimit','Process')
    $previousProcessorCount = [Environment]::GetEnvironmentVariable('DOTNET_PROCESSOR_COUNT','Process')
    try {
        $env:DOTNET_gcServer='0'; $env:DOTNET_GCHeapHardLimit='40000000'; $env:DOTNET_PROCESSOR_COUNT='2'
        & $dotnet $sdk.Compiler ('@{0}' -f $responseFile)
        if ($LASTEXITCODE -ne 0) { throw "Native helper compilation failed ($LASTEXITCODE)." }
    } finally {
        [Environment]::SetEnvironmentVariable('DOTNET_gcServer',$previousGcServer,'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_GCHeapHardLimit',$previousHeapLimit,'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_PROCESSOR_COUNT',$previousProcessorCount,'Process')
    }
}
if (-not $RefreshHelper) { Copy-Item -LiteralPath $sourcePlayer -Destination $copyPlayer -Recurse }
$copyManaged = Join-Path $copyPlayer 'DeadAir_Data\Managed'
Copy-Item -LiteralPath $helperAssembly -Destination $copyManaged -Force
if (-not $CecilPath) {
    $CecilPath = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library\PackageCache') -Filter 'Mono.Cecil.dll' -File -Recurse |
        Where-Object FullName -match 'com\.unity\.nuget\.mono-cecil' | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $CecilPath) { throw 'Supply CecilPath or restore the project Mono.Cecil package.' }
Add-Type -Path $CecilPath
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($copyManaged)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$copyGamePath = Join-Path $copyManaged 'Assembly-CSharp.dll'
# Always read the verified ORIGINAL DLL. Refresh cannot accumulate extra hooks.
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath, $reader)
if (@($gameAssembly.MainModule.AssemblyReferences | Where-Object Name -eq 'NativeBenchmark').Count) {
    $gameAssembly.Dispose(); throw 'Input player is already instrumented.'
}
$helper = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($helperAssembly, $reader)
$benchmarkType = $helper.MainModule.Types | Where-Object Name -eq 'NativeBenchmark'
$bootstrap = $gameAssembly.MainModule.Types | Where-Object FullName -eq 'DeadAir.DeadAirBootstrap'
$awake = $bootstrap.Methods | Where-Object Name -eq 'Awake'
$install = $benchmarkType.Methods | Where-Object Name -eq 'Install'
$processor = $awake.Body.GetILProcessor()
$returns = @($awake.Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ret })
if ($returns.Count -ne 1) { throw "Unexpected bootstrap return count: $($returns.Count)" }
$returns[0].OpCode = [Mono.Cecil.Cil.OpCodes]::Call
$returns[0].Operand = $gameAssembly.MainModule.ImportReference($install)
$processor.InsertAfter($returns[0], [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
$begin = $gameAssembly.MainModule.ImportReference(($benchmarkType.Methods | Where-Object Name -eq 'Begin'))
$end = $gameAssembly.MainModule.ImportReference(($benchmarkType.Methods | Where-Object Name -eq 'End'))
$scopes = @(
    @('DeadAir.DeadAirPlayer','Update'), @('DeadAir.DeadAirSession','Update'), @('DeadAir.DeadAirWorld','Apply'),
    @('DeadAir.DeadAirOfficeSign','LateUpdate'), @('DeadAir.DeadAirUI','Update')
)
for ($scopeIndex = 0; $scopeIndex -lt $scopes.Count; $scopeIndex++) {
    $scope = $scopes[$scopeIndex]
    $scopeType = $gameAssembly.MainModule.Types | Where-Object FullName -eq $scope[0]
    $methods = @($scopeType.Methods | Where-Object { $_.Name -eq $scope[1] -and $_.HasBody })
    if ($methods.Count -ne 1) { throw "Unexpected scope method count: $($scope -join '.')" }
    $method = $methods[0]
    if ($method.ReturnType.FullName -ne 'System.Void') { throw 'Profiling hook expects void method.' }
    $il = $method.Body.GetILProcessor()
    $first = $method.Body.Instructions[0]
    $il.InsertBefore($first, [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, $scopeIndex))
    $il.InsertBefore($first, [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Call, $begin))
    $methodReturns = @($method.Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ret })
    foreach ($return in $methodReturns) {
        # Branches to the old return must execute End. Keep its instruction
        # identity as the first epilogue instruction rather than insert before it.
        $return.OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I4
        $return.Operand = $scopeIndex
        $callEnd = [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Call, $end)
        $il.InsertAfter($return, $callEnd)
        $il.InsertAfter($callEnd, [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
    }
    foreach ($instruction in $method.Body.Instructions) {
        if ($instruction.OpCode.OperandType -ne [Mono.Cecil.Cil.OperandType]::ShortInlineBrTarget) { continue }
        $longName = $instruction.OpCode.Code.ToString() -replace '_S$', ''
        $field = [Mono.Cecil.Cil.OpCodes].GetField($longName)
        if (-not $field) { throw "Long branch opcode missing: $longName" }
        $instruction.OpCode = $field.GetValue($null)
    }
    $method.Body.MaxStackSize = [Math]::Max(2,$method.Body.MaxStackSize)
}
$temporary = Join-Path $copyManaged 'Assembly-CSharp.benchmark-temp.dll'
$gameAssembly.Write($temporary)
$gameAssembly.Dispose(); $helper.Dispose()
Move-Item -LiteralPath $temporary -Destination $copyGamePath -Force
if ((Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash -ne $gameHash -or
    (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $manifestHash) { throw 'Original player changed during preparation.' }
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$provenance = [ordered]@{
    sourcePlayer=$sourcePlayer; sourceVersion=$manifest.version; sourceGameSHA256=$gameHash; sourceManifestSHA256=$manifestHash
    helperSourceSHA256=(Get-FileHash -LiteralPath $helperSource -Algorithm SHA256).Hash
    instrumentedGameSHA256=(Get-FileHash -LiteralPath $copyGamePath -Algorithm SHA256).Hash
    helperSHA256=(Get-FileHash -LiteralPath $helperAssembly -Algorithm SHA256).Hash
    note='Copy-only bootstrap Install and five Begin/End scopes. Full explicit camera pipeline without readback; normal game updates active; original player unchanged.'
}
[IO.File]::WriteAllText((Join-Path $resultDirectory 'provenance.json'), ($provenance | ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $resultDirectory 'launch-command.txt'), ('Start-Process -FilePath "{0}" -WorkingDirectory "{1}" -ArgumentList ''-force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -deadAirMovementVerification -deadAirBenchmark "{2}" -logFile "{3}"'' -WindowStyle Hidden -PassThru' -f (Join-Path $copyPlayer 'DeadAir.exe'), $copyPlayer, $resultDirectory, (Join-Path $resultDirectory 'Player.log')))
Write-Output ('PREPARED ONLY, not launched: ' + (Join-Path $copyPlayer 'DeadAir.exe'))
Write-Output ('Launch command: ' + (Join-Path $resultDirectory 'launch-command.txt'))
