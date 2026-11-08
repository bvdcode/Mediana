[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $ArtifactDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string] $AccountId,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$')][string] $Bucket
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$publicBaseUrl = 'https://releases.splidex.com/mediana'
$endpoint = "https://$AccountId.r2.cloudflarestorage.com"
$destination = "s3://$Bucket/mediana/$Version"
[System.Version] $parsedVersion = $null
if (-not [System.Version]::TryParse($Version, [ref] $parsedVersion) -or $parsedVersion.Build -lt 0 -or
    $parsedVersion.Revision -ge 0 -or $Version -cne $parsedVersion.ToString()) {
    throw 'Release version must be a canonical stable three-part version.'
}

$directory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$platforms = @(
    @{ Rid = 'win-x64'; ExecutableSuffix = '.exe'; ArchiveSuffix = '.zip' },
    @{ Rid = 'linux-x64'; ExecutableSuffix = ''; ArchiveSuffix = '.tar.gz' },
    @{ Rid = 'osx-arm64'; ExecutableSuffix = ''; ArchiveSuffix = '.tar.gz' }
)
$files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$assets = foreach ($platform in $platforms) {
    $prefix = "Mediana-$Version-$($platform.Rid)"
    $binaryName = "$prefix$($platform.ExecutableSuffix)"
    foreach ($name in @($binaryName, "$prefix$($platform.ArchiveSuffix)")) {
        $file = Get-Item -LiteralPath (Join-Path $directory $name)
        $checksumFile = Get-Item -LiteralPath "$($file.FullName).sha256"
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($file.Length -le 0 -or (Get-Content -LiteralPath $checksumFile.FullName -Raw).TrimEnd("`r", "`n") -cne "$hash  $name") {
            throw "Release asset failed checksum validation: $name"
        }
        $files.Add($file)
        $files.Add($checksumFile)
        if ($name -ceq $binaryName) {
            [ordered]@{ name = $name; size = $file.Length; sha256 = $hash }
        }
    }
}
$manifest = [ordered]@{ version = $Version; assets = @($assets) } | ConvertTo-Json -Depth 4 -Compress
if ([System.Text.Encoding]::UTF8.GetByteCount($manifest) -gt 16 * 1024) {
    throw 'The release manifest exceeds the updater size limit.'
}

$manifestPath = Join-Path ([System.IO.Path]::GetTempPath()) "mediana-release-$([System.Guid]::NewGuid().ToString('N')).json"
try {
    [System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))
    foreach ($file in $files) {
        Write-Host "Publishing $($file.Name)"
        & aws s3 cp $file.FullName "$destination/$($file.Name)" --endpoint-url $endpoint --cache-control 'public,max-age=31536000,immutable' --no-progress --only-show-errors
    }
    & aws s3 cp $manifestPath "$destination/release.json" --endpoint-url $endpoint --content-type application/json --cache-control 'public,max-age=31536000,immutable' --no-progress --only-show-errors
    foreach ($asset in $assets) {
        $response = Invoke-WebRequest -Method Head -Uri "$publicBaseUrl/$Version/$($asset.name)" -TimeoutSec 60
        if ([long] $response.Headers['Content-Length'][0] -ne $asset.size) {
            throw "Published executable size does not match: $($asset.name)"
        }
    }
    & aws s3 cp $manifestPath "s3://$Bucket/mediana/latest.json" --endpoint-url $endpoint --content-type application/json --cache-control no-store --no-progress --only-show-errors
    $published = Invoke-WebRequest -Uri "$publicBaseUrl/latest.json" -TimeoutSec 60
    if ($published.Content.Trim() -cne $manifest) {
        throw 'The published release manifest does not match the uploaded version.'
    }
    Write-Host "Published Mediana $Version at $publicBaseUrl"
}
finally {
    Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
}
