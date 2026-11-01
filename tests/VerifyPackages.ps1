param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $AssetsFile,
    [Parameter(Mandatory)][string] $Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.ZipFile
$root = Split-Path $PSScriptRoot -Parent
$projects = @(Get-ChildItem "$root/src/*/*.csproj" | ForEach-Object { $_.BaseName })
$packages = @{}
foreach ($file in Get-ChildItem "$PackageDirectory/*.nupkg") {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $specifications = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec') })
        if ($specifications.Count -ne 1) { throw "Expected one nuspec in $($file.Name)." }
        $reader = [System.IO.StreamReader]::new($specifications[0].Open())
        try { [xml] $spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $spec.package.metadata
        $id = [string]$metadata.id
        if ($packages.ContainsKey($id)) { throw "Duplicate package: $id." }
        if ([string]$metadata.version -ne $Version) { throw "Unexpected version in $id." }
        if ([string]$metadata.license.'#text' -ne 'MIT') { throw "Unexpected license in $id." }
        if (@($archive.Entries | Where-Object { $_.FullName -match "^lib/[^/]+/$([regex]::Escape($id)).dll$" }).Count -eq 0) {
            throw "Missing library assembly in $id."
        }
        if ($null -eq $archive.GetEntry('README.md')) { throw "Missing package documentation in $id." }
        $packages[$id] = $true
        foreach ($dependency in $spec.SelectNodes('//*[local-name()="dependency"]')) {
            if ($dependency.id -like 'Mediana*' -and $dependency.id -notin $projects) {
                throw "Unpackaged dependency $($dependency.id) in $id."
            }
        }
    } finally { $archive.Dispose() }
}
$missing = @($projects | Where-Object { -not $packages.ContainsKey($_) })
$extra = @($packages.Keys | Where-Object { $_ -notin $projects })
if ($missing.Count -or $extra.Count) { throw "Package set mismatch. Missing: $missing. Unexpected: $extra." }
$assets = Get-Content -Raw $AssetsFile | ConvertFrom-Json -AsHashtable
$resolved = @{}
foreach ($entry in $assets.libraries.GetEnumerator()) {
    if ($entry.Value.type -eq 'project') { throw "Consumer still uses a project reference: $($entry.Key)." }
    $parts = $entry.Key.Split('/')
    if ($parts[0] -in $projects) {
        if ($parts[1] -ne $Version) { throw "Consumer resolved the wrong version: $($entry.Key)." }
        $resolved[$parts[0]] = $true
    }
}
$unconsumed = @($projects | Where-Object { -not $resolved.ContainsKey($_) })
if ($unconsumed.Count) { throw "Consumer did not restore modules: $unconsumed." }
Write-Host "Verified $($projects.Count) package assemblies, metadata and package-only consumer dependencies."
