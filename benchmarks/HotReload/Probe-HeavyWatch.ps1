#requires -Version 7.2
param(
    [string]$OutputDirectory = 'bin/WatchMemory/heavy-feasibility',
    [string]$ToolDirectory = 'bin/WatchMemory/pilot-tools/baseline-production'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve previous heavy feasibility evidence; use a fresh directory.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$encoding = [Text.UTF8Encoding]::new($false)
function Run([string]$name, [string[]]$arguments) {
    $writer = [IO.StreamWriter]::new((Join-Path $OutputDirectory $name),$false,$encoding)
    $writer.AutoFlush = $true
    try {
        & dotnet @arguments 2>&1 | ForEach-Object { $writer.WriteLine("$_") }
        if ($LASTEXITCODE -ne 0) { throw "Failed: $name; inspect the local feasibility logs." }
    }
    finally { $writer.Dispose() }
}
$prepared = & (Join-Path $PSScriptRoot 'Prepare-Purity.ps1') -SourceRoot (Join-Path $PSScriptRoot 'Akbura-PurityUI-Tutorial') -OutputDirectory $OutputDirectory -RepositoryRoot $repository
$project = Join-Path $prepared 'PurityUIDashborad/PurityUIDashborad.csproj'
$xml = [IO.File]::ReadAllText($project)
foreach ($assembly in @('Akbura.Generator','Akbura.BlackSilence')) {
    $xml = $xml.Replace((Join-Path $repository "src/$assembly/bin/Debug/netstandard2.0/$assembly.dll"),(Join-Path $ToolDirectory "$assembly.dll"))
}
$xml = $xml.Replace('</PropertyGroup>','<UseSharedCompilation>false</UseSharedCompilation><AkburaCrossCompilationReuse>true</AkburaCrossCompilationReuse></PropertyGroup>')
[IO.File]::WriteAllText($project,$xml.ReplaceLineEndings("`r`n")+"`r`n",$encoding)
$source = Join-Path $prepared 'PurityUIDashborad/Components/Sidebar.akbura'
$originalHash = (Get-FileHash -LiteralPath $source).Hash
$text = [IO.File]::ReadAllText($source)
if (-not $text.Contains('<Panel>')) { throw 'The real Sidebar root changed; review the minimal fixture adaptation.' }
$text = [regex]::new('<Panel>').Replace($text,'<Panel Margin="10">',1)
[IO.File]::WriteAllText($source,$text,$encoding)
$preparedHash = (Get-FileHash -LiteralPath $source).Hash
$hostProject = Join-Path $prepared 'PurityHost/PurityHost.csproj'
$generated = Join-Path $OutputDirectory 'generated'
Run 'build.log' @('build',$hostProject,'--nologo','-v:minimal','-p:UseSharedCompilation=false','-nr:false',
    '-p:EmitCompilerGeneratedFiles=true',"-p:CompilerGeneratedFilesOutputPath=$generated")
Run 'inputs.json' @('msbuild',$project,'-nologo','-v:q','-t:ResolveReferences','-getProperty:RootNamespace,MSBuildProjectDirectory,DefineConstants','-getItem:Compile,ReferencePath,AdditionalFiles')
$files = @(Get-ChildItem -LiteralPath $generated -File -Recurse -Filter '*.cs')
$components = @(Get-ChildItem -LiteralPath (Join-Path $prepared 'PurityUIDashborad') -File -Recurse -Filter '*.akbura')
$nested = @([regex]::Matches($text,'<([A-Za-z_]\w*)\b') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -in $components.BaseName } | Sort-Object -Unique)
$metadata = @{ Workload = 'Real Purity Sidebar from the tutorial submodule'; PreparedProject=$project; PreparedHost=$hostProject; PreparedRoot=$prepared
    OriginalSidebarSha256=$originalHash; PreparedSidebarSha256=$preparedHash; Adaptation='Added only Margin="10" to the real root Panel before initial compilation; the tutorial checkout is unchanged.'
    ComponentSourceCount=$components.Count; SidebarLexicalAttributeExpressionCount=[regex]::Matches($text,'=\s*\{').Count
    BindingCountMeaning='Lexical markup attribute-expression count, not a semantic binding or admitted-hit count.'; NestedComponentKinds=$nested
    GeneratedCsFiles=$files.Count; GeneratedCsBytes=($files | Measure-Object Length -Sum).Sum
    CompilerPair=@('Akbura.Generator.dll','Akbura.BlackSilence.dll') | ForEach-Object { @{ File=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $ToolDirectory $_)).Hash } } }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'workload.json'),(($metadata | ConvertTo-Json -Depth 8).ReplaceLineEndings("`r`n")+"`r`n"),$encoding)
$previous = $env:AKBURA_TIMING_COMPONENT
try {
    $env:AKBURA_TIMING_COMPONENT = 'Sidebar'
    Run 'watch.jsonl' @((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'),'watch',$hostProject,$source,'Sidebar')
}
finally { $env:AKBURA_TIMING_COMPONENT = $previous }
Write-Output 'PASS real Sidebar live unsupported-edit feasibility. Supported eligibility, compilation controls and primary performance remain separate investigations.'
