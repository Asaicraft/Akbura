param(
    [Parameter(Mandatory)] [string] $VsixPath,
    [Parameter(Mandatory)] [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$verify = Join-Path $PSScriptRoot 'verify-vsix.ps1'
& $verify -VsixPath $VsixPath -ExpectedVersion $ExpectedVersion
$cases = foreach ($edition in @('Community', 'Pro', 'Enterprise')) {
    foreach ($api in @('18.0', '18.9')) {
        @{
            XPath = "//vs:InstallationTarget[@Id='Microsoft.VisualStudio.$edition']"
            Version = "[$api,)"
            Error = 'stable Marketplace API range'
        }
    }
}
$cases += foreach ($hostVersion in @('17.14', '18.8')) {
    @{
        XPath = "//vs:Prerequisite[@Id='Microsoft.VisualStudio.Component.CoreEditor']"
        Version = "[$hostVersion,)"
        Error = 'CoreEditor host requirement'
    }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) (
    'akbura-vsix-guard-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    foreach ($case in $cases) {
        $candidate = Join-Path $temporary 'invalid.vsix'
        Copy-Item -LiteralPath $VsixPath -Destination $candidate -Force
        $archive = [IO.Compression.ZipFile]::Open($candidate, 'Update')
        try {
            $entry = $archive.GetEntry('extension.vsixmanifest')
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml] $manifest = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
            $namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
            $namespaces.AddNamespace('vs', $manifest.DocumentElement.NamespaceURI)
            $manifest.SelectSingleNode($case.XPath, $namespaces).SetAttribute('Version', $case.Version)
            $entry.Delete()
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('extension.vsixmanifest').Open())
            try { $writer.Write($manifest.OuterXml) }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
        $rejected = $false
        try { & $verify -VsixPath $candidate -ExpectedVersion $ExpectedVersion *> $null }
        catch {
            if (!$_.Exception.Message.Contains($case.Error)) { throw }
            $rejected = $true
        }
        if (!$rejected) { throw "Verifier accepted $($case.XPath): $($case.Version)." }
    }
    Write-Host "Verified Marketplace guard: valid package accepted; $($cases.Count) invalid API/host ranges rejected."
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'akbura-vsix-guard-*') {
        throw 'Refusing cleanup outside the owned temporary directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
