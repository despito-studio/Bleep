param(
    [Parameter(Mandatory)][string]$BuildPath,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Resolve-WorkspacePath([string]$path) {
    if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $workspace $path }
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\', '/')
    if (-not $full.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Audit path must be inside this workspace: $full"
    }
    return $full
}
function Assert-NoReparsePoints([string]$path) {
    $ancestor = $path
    while ($ancestor -and $ancestor.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Audit path contains a reparse-point ancestor: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    if (-not (Test-Path -LiteralPath $path)) { return }
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push($path)
    while ($stack.Count -gt 0) {
        $entry = $stack.Pop()
        $attributes = [IO.File]::GetAttributes($entry)
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Audit tree contains a reparse point: $entry" }
        if ($attributes -band [IO.FileAttributes]::Directory) {
            foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($entry)) { $stack.Push($child) }
        }
    }
}

$originalPlayer = Resolve-WorkspacePath $BuildPath
$auditDirectory = Resolve-WorkspacePath $OutputPath
if ($auditDirectory.StartsWith($originalPlayer + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $originalPlayer.StartsWith($auditDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $auditDirectory.Equals($originalPlayer, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BuildPath and OutputPath must be separate directories.'
}
Assert-NoReparsePoints $originalPlayer
Assert-NoReparsePoints $auditDirectory
if (Test-Path -LiteralPath $auditDirectory) { throw "Use a fresh audit OutputPath; existing results are immutable: $auditDirectory" }
$manifestPath = Join-Path $originalPlayer 'build-info.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2 -or $manifest.buildResult -ne 'Succeeded' -or
    [string]::IsNullOrWhiteSpace($manifest.version) -or @($manifest.files).Count -eq 0) {
    throw 'Native audit requires a successful build manifest with version and runtime files.'
}
foreach ($file in $manifest.files) {
    $source = [IO.Path]::GetFullPath((Join-Path $originalPlayer $file.path))
    if (-not $source.StartsWith($originalPlayer + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest file escaped BuildPath.' }
    if ((Get-Item -LiteralPath $source).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Runtime file disagrees with the build manifest: $($file.path)"
    }
}
$managedDirectory = Join-Path $originalPlayer 'DeadAir_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managedDirectory 'Assembly-CSharp.dll')) -or
    -not (Test-Path -LiteralPath (Join-Path $originalPlayer 'DeadAir.exe'))) { throw 'Actual Windows player and game assembly are missing.' }
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$sdks = & $dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Could not list installed .NET SDKs.' }
$compilers = @(foreach ($sdk in $sdks) {
    if ($sdk -match '^(\S+)\s+\[(.+)\]$') {
        $sdkVersion = $Matches[1]
        $compiler = Join-Path $Matches[2] ($sdkVersion + '\Roslyn\bincore\csc.dll')
        if (Test-Path -LiteralPath $compiler) { [pscustomobject]@{ version = [version]($sdkVersion.Split('-')[0]); path = $compiler } }
    }
})
$sdkCsc = $compilers | Sort-Object version -Descending | Select-Object -First 1 -ExpandProperty path
if (-not $sdkCsc) { throw 'A .NET SDK with the C# compiler is required.' }
$cecilPath = Get-ChildItem -LiteralPath (Join-Path $workspace 'Library\PackageCache') -Filter 'Mono.Cecil.dll' -File -Recurse |
    Where-Object FullName -match 'com\.unity\.nuget\.mono-cecil' | Select-Object -First 1 -ExpandProperty FullName
if (-not $cecilPath) { throw 'Unity Mono.Cecil dependency was not found in the project package cache.' }
Assert-NoReparsePoints $cecilPath

[IO.Directory]::CreateDirectory($auditDirectory) | Out-Null
$helperAssembly = Join-Path $auditDirectory 'RuntimeAudit.dll'
$responseFile = Join-Path $auditDirectory 'compile.rsp'
$compileArguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-langversion:latest', ('-out:"{0}"' -f $helperAssembly))
$compileArguments += Get-ChildItem -LiteralPath $managedDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object { '-reference:"{0}"' -f $_.FullName }
$compileArguments += ('"{0}"' -f (Join-Path $PSScriptRoot 'GeometryAudit.cs'))
$compileArguments += ('"{0}"' -f (Join-Path $PSScriptRoot 'RuntimeRuleAudit.cs'))
[IO.File]::WriteAllLines($responseFile, $compileArguments, [Text.UTF8Encoding]::new($false))
& $dotnet $sdkCsc ('@{0}' -f $responseFile)
if ($LASTEXITCODE -ne 0) { throw "Audit helper compilation failed ($LASTEXITCODE)." }
$copiedPlayer = Join-Path $auditDirectory 'Player'
Copy-Item -LiteralPath $originalPlayer -Destination $copiedPlayer -Recurse
$copyManaged = Join-Path $copiedPlayer 'DeadAir_Data\Managed'
Copy-Item -LiteralPath $helperAssembly -Destination $copyManaged
Add-Type -Path $cecilPath
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($copyManaged)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$reader.ReadWrite = $false
$gamePath = Join-Path $copyManaged 'Assembly-CSharp.dll'
$gameAssembly = $null
$helper = $null
try {
    $gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath, $reader)
    $helper = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($helperAssembly, $reader)
    $bootstrap = @($gameAssembly.MainModule.Types | Where-Object FullName -eq 'DeadAir.DeadAirBootstrap')
    $awake = @($bootstrap.Methods | Where-Object Name -eq 'Awake')
    $auditType = @($helper.MainModule.Types | Where-Object Name -eq 'GeometryAudit')
    $install = @($auditType.Methods | Where-Object Name -eq 'Install')
    if ($bootstrap.Count -ne 1 -or $awake.Count -ne 1 -or $install.Count -ne 1) { throw 'Expected bootstrap and audit entry points were not found.' }
    $returns = @($awake[0].Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ret })
    if ($returns.Count -ne 1) { throw "Unexpected bootstrap return count: $($returns.Count)" }
    $reference = $gameAssembly.MainModule.ImportReference($install[0])
    # Keep the original return instruction as the branch target, so early exits also install the audit.
    $returns[0].OpCode = [Mono.Cecil.Cil.OpCodes]::Call
    $returns[0].Operand = $reference
    $awake[0].Body.GetILProcessor().InsertAfter($returns[0], [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $temporary = Join-Path $copyManaged 'Assembly-CSharp.audit-temp.dll'
    $gameAssembly.Write($temporary)
} finally {
    if ($gameAssembly) { $gameAssembly.Dispose() }
    if ($helper) { $helper.Dispose() }
    $resolver.Dispose()
}
Move-Item -LiteralPath $temporary -Destination $gamePath -Force
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifestHash) { throw 'Original build manifest changed during audit preparation.' }
$outputs = Join-Path $auditDirectory 'Results'
$clonedFiles = @(Get-ChildItem -LiteralPath $copiedPlayer -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($copiedPlayer.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$prepared = [ordered]@{ schemaVersion = 1; version = $manifest.version; buildPath = $originalPlayer; playerPath = $copiedPlayer;
    resultsPath = $outputs; buildManifestSHA256 = $manifestHash; files = $clonedFiles }
[IO.File]::WriteAllText((Join-Path $auditDirectory 'prepared-audit.json'), ($prepared | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ player = Join-Path $copiedPlayer 'DeadAir.exe'; results = $outputs; version = $manifest.version; buildManifestSHA256 = $manifestHash } | ConvertTo-Json
