// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text;

namespace Mediana.Cli.Updates
{
    internal class GitHubReleaseClient(HttpClient httpClient, ReleasePlatform platform, UpdateRetryPolicy retry)
    {
        private const int Sha256HexLength = 64;
        private static readonly Uri LatestReleaseUrl = new($"https://github.com/{ReleaseAssets.Repository}/releases/latest");
        private const string ReleaseTagPrefix = $"https://github.com/{ReleaseAssets.Repository}/releases/tag/v";

        public Task<GitHubRelease> GetLatestAsync(CancellationToken token)
        {
            return retry.ExecuteAsync(GetLatestCoreAsync, token);
        }

        private async Task<GitHubRelease> GetLatestCoreAsync(CancellationToken token)
        {
            using HttpRequestMessage request = new(HttpMethod.Head, LatestReleaseUrl);
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            string? releaseUrl = response.RequestMessage?.RequestUri?.AbsoluteUri;
            if (releaseUrl is null || !releaseUrl.StartsWith(ReleaseTagPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("GitHub did not redirect to a release in the Mediana repository.");
            }
            string tagVersion = releaseUrl[ReleaseTagPrefix.Length..];
            if (!Version.TryParse(tagVersion, out Version? version) || version.Build < 0 || version.Revision >= 0
                || tagVersion != version.ToString())
            {
                throw new InvalidDataException("The release tag must contain a stable three-part version.");
            }

            string assetName = ReleaseAssets.GetName(platform, version);
            Uri downloadUrl = new($"https://github.com/{ReleaseAssets.Repository}/releases/download/v{version}/{assetName}");
            byte[] sha256 = await ReadChecksumAsync(new Uri(downloadUrl.AbsoluteUri + ".sha256"), assetName, token);
            long size = await ReadSizeAsync(downloadUrl, token);
            return new GitHubRelease(version, downloadUrl, size, sha256);
        }

        private async Task<byte[]> ReadChecksumAsync(Uri url, string assetName, CancellationToken token)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            int lineLength = Sha256HexLength + 2 + assetName.Length;
            byte[] buffer = new byte[lineLength + 3];
            int length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token);
            string checksum = Encoding.ASCII.GetString(buffer, 0, length).TrimEnd('\r', '\n');

            if (length == buffer.Length || checksum.Length != lineLength
                || checksum[Sha256HexLength..] != "  " + assetName)
            {
                throw new InvalidDataException("The release checksum must contain one SHA-256 entry for the platform executable.");
            }
            string hash = checksum[..Sha256HexLength];
            if (hash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The executable SHA-256 checksum is invalid.");
            }
            return Convert.FromHexString(hash);
        }

        private async Task<long> ReadSizeAsync(Uri url, CancellationToken token)
        {
            using HttpRequestMessage request = new(HttpMethod.Head, url);
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is not long size || size <= 0)
            {
                throw new InvalidDataException("The release executable has an invalid size.");
            }
            return size;
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
