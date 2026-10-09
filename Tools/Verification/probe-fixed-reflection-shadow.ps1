param([Parameter(Mandatory)][string]$BenchmarkPath)
$ErrorActionPreference='Stop'
$probeRoot=(Resolve-Path -LiteralPath $BenchmarkPath).Path
$provenancePath=Join-Path $probeRoot 'Results\provenance.json'
$provenance=Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
$managed=Join-Path $probeRoot 'Player\DeadAir_Data\Managed'
$gamePath=Join-Path $managed 'Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash -ne $provenance.instrumentedGameSHA256) {
    throw 'Probe must start from an unchanged, instrumented benchmark copy.'
}
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$cecil=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library\PackageCache') -Filter 'Mono.Cecil.dll' -File -Recurse |
    Where-Object FullName -match 'com\.unity\.nuget\.mono-cecil' | Select-Object -First 1 -ExpandProperty FullName
Add-Type -Path $cecil
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new(); $resolver.AddSearchDirectory($managed)
$reader=[Mono.Cecil.ReaderParameters]::new(); $reader.AssemblyResolver=$resolver
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath,$reader)
try {
    $type=$assembly.MainModule.Types | Where-Object FullName -eq 'DeadAir.DeadAirPlanarReflections'
    $count=0
    foreach($methodName in @('SuspendLocalShadows','RestoreLocalShadows')) {
        $method=$type.Methods | Where-Object Name -eq $methodName
        $calls=@($method.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Void UnityEngine.Light::set_shadowCustomResolution(System.Int32)'
        })
        if($calls.Count -ne 1) { throw "Expected one lunar shadow resolution setter in $methodName." }
        # Consume the original setter's int and Light arguments. Every other
        # light, reflection, roof shadow and game update remains unchanged.
        $calls[0].OpCode=[Mono.Cecil.Cil.OpCodes]::Pop; $calls[0].Operand=$null
        $method.Body.GetILProcessor().InsertAfter($calls[0],[Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Pop))
        $count++
    }
    if($count -ne 2) { throw 'Unexpected probe patch count.' }
    $temporary=Join-Path $managed 'Assembly-CSharp.shadow-probe.dll'
    $assembly.Write($temporary)
} finally { $assembly.Dispose(); $resolver.Dispose() }
Move-Item -LiteralPath $temporary -Destination $gamePath -Force
$provenance | Add-Member -NotePropertyName previousInstrumentedGameSHA256 -NotePropertyValue $provenance.instrumentedGameSHA256
$provenance.instrumentedGameSHA256=(Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
$provenance.note+=' Diagnostic copy only: two per-reflection shadow resolution setter calls removed; lunar soft shadows stay at 1024; original player/source unchanged.'
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $provenancePath -Encoding utf8
Write-Output 'Prepared fixed lunar shadow resolution probe; no player launched.'
