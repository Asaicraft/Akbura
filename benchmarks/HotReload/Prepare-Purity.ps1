param(
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$sourceProject = Join-Path $SourceRoot 'PurityUIDashborad/PurityUIDashborad.csproj'
if (-not (Test-Path -LiteralPath $sourceProject)) {
    throw 'Purity submodule is missing. Run: git submodule update --init -- benchmarks/HotReload/Akbura-PurityUI-Tutorial'
}
$gitRoot = & git -C $SourceRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ne $SourceRoot.TrimEnd('\', '/')) {
    throw 'PurityRoot must be the root of the tutorial Git repository, not an unpacked or previously prepared copy.'
}
$dirty = & git -C $SourceRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $dirty) {
    throw 'The tutorial checkout has modified tracked files. Commit or stash them before benchmarking its committed revision.'
}
$revision = & git -C $SourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve the tutorial revision.'
}
$snapshotRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('purity-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $snapshotRoot
$archive = Join-Path $snapshotRoot 'source.zip'
& git -C $SourceRoot archive --format=zip HEAD --output=$archive
if ($LASTEXITCODE -ne 0) {
    throw 'Could not export the tutorial revision.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $snapshotRoot)

function Save-Xml([xml]$Document, [string]$Path) {
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [Xml.NewLineHandling]::Replace
    $writer = [Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

$projectPath = Join-Path $snapshotRoot 'PurityUIDashborad/PurityUIDashborad.csproj'
[xml]$project = [IO.File]::ReadAllText($projectPath)
foreach ($package in @($project.SelectNodes("//PackageReference[@Include='Akbura' or @Include='Akbura.Diagnostics']"))) {
    $null = $package.ParentNode.RemoveChild($package)
}
$properties = $project.CreateElement('PropertyGroup')
$tasks = $project.CreateElement('AkburaBuildTasksAssembly')
$tasks.InnerText = Join-Path $RepositoryRoot 'src/Akbura.Build.Tasks/bin/Debug/net10.0/Akbura.Build.Tasks.dll'
$null = $properties.AppendChild($tasks)
$null = $project.Project.AppendChild($properties)
$references = $project.CreateElement('ItemGroup')
foreach ($name in @('Akbura', 'Akbura.Diagnostics')) {
    $reference = $project.CreateElement('Reference')
    $reference.SetAttribute('Include', $name)
    $reference.SetAttribute('HintPath', (Join-Path $RepositoryRoot "src/$name/bin/Debug/net10.0/$name.dll"))
    $null = $references.AppendChild($reference)
}
foreach ($name in @('Akbura.BlackSilence', 'Akbura.Generator')) {
    $analyzer = $project.CreateElement('Analyzer')
    $analyzer.SetAttribute('Include', (Join-Path $RepositoryRoot "src/$name/bin/Debug/netstandard2.0/$name.dll"))
    $null = $references.AppendChild($analyzer)
}
$null = $project.Project.AppendChild($references)
$import = $project.CreateElement('Import')
$import.SetAttribute('Project', (Join-Path $RepositoryRoot 'src/Akbura/buildTransitive/Akbura.targets'))
$null = $project.Project.AppendChild($import)
Save-Xml $project $projectPath

$packagesPath = Join-Path $snapshotRoot 'Directory.Packages.props'
[xml]$packages = [IO.File]::ReadAllText($packagesPath)
if (-not $packages.SelectSingleNode("//PackageVersion[@Include='Avalonia.Headless']")) {
    $version = $packages.SelectSingleNode("//PackageVersion[@Include='Avalonia']").GetAttribute('Version')
    $headless = $packages.CreateElement('PackageVersion')
    $headless.SetAttribute('Include', 'Avalonia.Headless')
    $headless.SetAttribute('Version', $version)
    $null = $packages.SelectSingleNode('//ItemGroup').AppendChild($headless)
    Save-Xml $packages $packagesPath
}
$hostRoot = Join-Path $snapshotRoot 'PurityHost'
$null = New-Item -ItemType Directory -Path $hostRoot
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'PurityHost') -File) {
    Copy-Item -LiteralPath $file.FullName -Destination $hostRoot
}
$hostPath = Join-Path $hostRoot 'PurityHost.csproj'
$hostXml = [IO.File]::ReadAllText($hostPath).Replace('$(MSBuildThisFileDirectory)../../..', [Security.SecurityElement]::Escape($RepositoryRoot))
[IO.File]::WriteAllText($hostPath, $hostXml, [Text.UTF8Encoding]::new($false))
$metadata = @{ Repository = 'https://github.com/Asaicraft/Akbura-PurityUI-Tutorial.git'; Revision = "$revision"; PreparedRoot = $snapshotRoot } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'purity-source.json'), $metadata + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Host "Prepared tutorial revision $revision without changing its checkout."
return $snapshotRoot
