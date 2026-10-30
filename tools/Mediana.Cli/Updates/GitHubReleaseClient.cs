// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.Cli.Updates
{
    internal class GitHubReleaseClient(HttpClient httpClient, ReleasePlatform platform, UpdateRetryPolicy retry)
    {
        private const string Sha256Prefix = "sha256:";
        private static readonly Uri LatestReleaseUrl = new($"https://api.github.com/repos/{ReleaseAssets.Repository}/releases/latest");

        public Task<GitHubRelease> GetLatestAsync(CancellationToken token)
        {
            return retry.ExecuteAsync(GetLatestCoreAsync, token);
        }

        private async Task<GitHubRelease> GetLatestCoreAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            JsonElement release = document.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            {
                throw new InvalidDataException("The latest GitHub release is not a stable published release.");
            }

            string? tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out Version? version) || version.Build < 0 || version.Revision >= 0)
            {
                throw new InvalidDataException("The release tag must contain a stable three-part version.");
            }

            string assetName = ReleaseAssets.GetName(platform, version);
            JsonElement asset = release.GetProperty("assets").EnumerateArray()
                .SingleOrDefault(candidate => candidate.GetProperty("name").GetString() == assetName);
            if (asset.ValueKind == JsonValueKind.Undefined || asset.GetProperty("state").GetString() != "uploaded")
            {
                throw new InvalidDataException($"The latest release does not contain {assetName}.");
            }

            string expectedUrl = $"https://github.com/{ReleaseAssets.Repository}/releases/download/{tag}/{assetName}";
            if (asset.GetProperty("browser_download_url").GetString() != expectedUrl)
            {
                throw new InvalidDataException("The executable download URL does not belong to this release.");
            }

            string? digest = asset.GetProperty("digest").GetString();
            if (digest is null || !digest.StartsWith(Sha256Prefix, StringComparison.Ordinal) || digest.Length != Sha256Prefix.Length + 64)
            {
                throw new InvalidDataException("GitHub did not provide a SHA-256 checksum for the release executable.");
            }
            string hash = digest[Sha256Prefix.Length..];
            if (hash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The executable SHA-256 checksum is invalid.");
            }
            byte[] sha256 = Convert.FromHexString(hash);

            long size = asset.GetProperty("size").GetInt64();
            if (size <= 0)
            {
                throw new InvalidDataException("The release executable has an invalid size.");
            }
            return new GitHubRelease(version, new Uri(expectedUrl), size, sha256);
        }

        public async Task DownloadAsync(GitHubRelease release, string destination, CancellationToken token)
        {
            await using FileStream file = new(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous);
            await retry.ExecuteAsync(attemptToken => DownloadCoreAsync(release, file, attemptToken), token);
        }

        private async Task<long> DownloadCoreAsync(GitHubRelease release, FileStream file, CancellationToken token)
        {
            file.SetLength(0);
            file.Position = 0;
            using HttpResponseMessage response = await httpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            await stream.CopyToAsync(file, token);
            await file.FlushAsync(token);
            if (file.Length != release.Size)
            {
                throw new InvalidDataException("The downloaded executable size does not match the release asset.");
            }
            file.Position = 0;
            byte[] sha256 = await SHA256.HashDataAsync(file, token);
            if (!CryptographicOperations.FixedTimeEquals(sha256, release.Sha256))
            {
                throw new InvalidDataException("SHA-256 verification failed. The installed executable has not been changed.");
            }
            return file.Length;
        }
    }
}
