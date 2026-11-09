$ErrorActionPreference = 'Stop'
$releasePublisher = Join-Path $PSScriptRoot '../.github/PublishRelease.ps1'
$releaseRoot = Join-Path ([System.IO.Path]::GetTempPath()) "mediana-publication-$([System.Guid]::NewGuid().ToString('N'))"
$releasePublicationState = @{
    Uploads = [System.Collections.Generic.List[string[]]]::new()
    Manifest = ''
    IncorrectPublicSize = $false
}

function aws {
    [string[]] $uploadArguments = $args
    if ($uploadArguments[0] -cne 's3' -or $uploadArguments[1] -cne 'cp') {
        throw 'Unexpected storage operation.'
    }
    $releasePublicationState.Uploads.Add($uploadArguments)
    if ($uploadArguments[3].EndsWith('/latest.json')) {
        $releasePublicationState.Manifest = [System.IO.File]::ReadAllText($uploadArguments[2])
    }
}

function Invoke-WebRequest {
    param([string] $Method, [string] $Uri, [int] $TimeoutSec)
    if ($Method -ceq 'Head') {
        if ($releasePublicationState.Uploads.Count -ne 13) {
            throw 'Public validation must follow all versioned uploads.'
        }
        $name = ([System.Uri] $Uri).Segments[-1]
        [long] $size = (Get-Item -LiteralPath (Join-Path $releaseRoot $name)).Length
        if ($releasePublicationState.IncorrectPublicSize) {
            $size++
        }
        return @{ Headers = @{ 'Content-Length' = @("$size") } }
    }
    if ($Uri -cne 'https://releases.splidex.com/mediana/latest.json' -or $releasePublicationState.Uploads.Count -ne 14) {
        throw 'Latest verification must follow the final manifest upload.'
    }
    return @{ Content = $releasePublicationState.Manifest }
}

try {
    New-Item -ItemType Directory -Path $releaseRoot > $null
    foreach ($name in @('Mediana-1.2.3-win-x64.exe', 'Mediana-1.2.3-win-x64.zip',
        'Mediana-1.2.3-linux-x64', 'Mediana-1.2.3-linux-x64.tar.gz',
        'Mediana-1.2.3-osx-arm64', 'Mediana-1.2.3-osx-arm64.tar.gz')) {
        $path = Join-Path $releaseRoot $name
        [System.IO.File]::WriteAllText($path, "Release payload for $name")
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText("$path.sha256", "$hash  $name`n")
    }
    $arguments = @{ Version = '1.2.3'; ArtifactDirectory = $releaseRoot; AccountId = '0123456789abcdef0123456789abcdef'; Bucket = 'releases' }
    & $releasePublisher @arguments *> $null
    if ($releasePublicationState.Uploads.Count -ne 14 -or $releasePublicationState.Uploads[-1][3] -cne 's3://releases/mediana/latest.json') {
        throw 'Latest must be published after all twelve assets and the immutable manifest.'
    }
    $manifest = $releasePublicationState.Manifest | ConvertFrom-Json
    if ($manifest.version -cne '1.2.3' -or $manifest.assets.Count -ne 3) {
        throw 'The published manifest must include exactly three platform executables.'
    }
    foreach ($asset in $manifest.assets) {
        $file = Get-Item -LiteralPath (Join-Path $releaseRoot $asset.name)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset.size -ne $file.Length -or $asset.sha256 -cne $hash) {
            throw 'Published manifest metadata does not match the executable.'
        }
    }
    foreach ($upload in $releasePublicationState.Uploads) {
        $cacheIndex = [System.Array]::IndexOf($upload, '--cache-control')
        $expected = if ($upload[3].EndsWith('/latest.json')) { 'no-store' } else { 'public,max-age=31536000,immutable' }
        if ($cacheIndex -lt 0 -or $upload[$cacheIndex + 1] -cne $expected) {
            throw 'Release object cache policy is incorrect.'
        }
    }
    Write-Output 'Publication order, metadata and cache policies passed.'

    $releasePublicationState.Uploads.Clear()
    $releasePublicationState.IncorrectPublicSize = $true
    $rejected = $false
    try {
        & $releasePublisher @arguments *> $null
    }
    catch {
        $rejected = $_.Exception.Message.StartsWith('Published executable size does not match:')
    }
    if (-not $rejected -or $releasePublicationState.Uploads.Count -ne 13) {
        throw 'A failed public verification must leave latest unpublished.'
    }
    Write-Output 'Public verification failure preserves latest.'

    $releasePublicationState.Uploads.Clear()
    $releasePublicationState.IncorrectPublicSize = $false
    [System.IO.File]::WriteAllText((Join-Path $releaseRoot 'Mediana-1.2.3-win-x64.exe'), 'Modified executable')
    $rejected = $false
    try {
        & $releasePublisher @arguments *> $null
    }
    catch {
        $rejected = $_.Exception.Message.StartsWith('Release asset failed checksum validation:')
    }
    if (-not $rejected -or $releasePublicationState.Uploads.Count -ne 0) {
        throw 'A checksum failure must prevent every upload.'
    }
    Write-Output 'Checksum failure prevents publication.'
}
finally {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
