// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.Cli.Updates
{
    internal class ReleaseClient(HttpClient httpClient, ReleasePlatform platform, UpdateRetryPolicy retry)
    {
        private const int MaximumManifestBytes = 16 * 1024;
        private const int Sha256HexLength = 64;
        private static readonly Uri LatestManifestUrl = new(ReleaseAssets.BaseUrl, "latest.json");

        public Task<UpdateRelease> GetLatestAsync(CancellationToken token)
        {
            return retry.ExecuteAsync(GetLatestCoreAsync, token);
        }

        private async Task<UpdateRelease> GetLatestCoreAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(LatestManifestUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            byte[] buffer = new byte[MaximumManifestBytes + 1];
            int length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token);
            if (length > MaximumManifestBytes)
            {
                throw new InvalidDataException("The release manifest exceeds the size limit.");
            }
            using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, length));
            JsonElement manifest = document.RootElement;
            Version version = ReadVersion(manifest);
            string assetName = ReleaseAssets.GetName(platform, version);
            JsonElement asset = ReadAsset(manifest, assetName);
            if (!asset.TryGetProperty("size", out JsonElement sizeElement) || sizeElement.ValueKind != JsonValueKind.Number
                || !sizeElement.TryGetInt64(out long size) || size <= 0)
            {
                throw new InvalidDataException("The release executable has an invalid size.");
            }
            if (!asset.TryGetProperty("sha256", out JsonElement hashElement) || hashElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The release executable is missing its SHA-256 checksum.");
            }
            string hash = hashElement.GetString()!;
            if (hash.Length != Sha256HexLength || hash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The executable SHA-256 checksum is invalid.");
            }
            Uri downloadUrl = new(ReleaseAssets.BaseUrl, $"{version}/{assetName}");
            return new UpdateRelease(version, downloadUrl, size, Convert.FromHexString(hash));
        }

        private static Version ReadVersion(JsonElement manifest)
        {
            if (manifest.ValueKind != JsonValueKind.Object || !manifest.TryGetProperty("version", out JsonElement element)
                || element.ValueKind != JsonValueKind.String || !Version.TryParse(element.GetString(), out Version? version)
                || version.Build < 0 || version.Revision >= 0 || element.GetString() != version.ToString())
            {
                throw new InvalidDataException("The release manifest must contain a stable three-part version.");
            }
            return version;
        }

        private static JsonElement ReadAsset(JsonElement manifest, string name)
        {
            if (!manifest.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The release manifest is missing its executable assets.");
            }
            JsonElement[] matches = assets.EnumerateArray().Where(candidate => candidate.ValueKind == JsonValueKind.Object
                && candidate.TryGetProperty("name", out JsonElement candidateName) && candidateName.ValueKind == JsonValueKind.String
                && candidateName.GetString() == name).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException($"The release manifest must contain exactly one {name}.");
            }
            return matches[0];
        }

        public async Task DownloadAsync(UpdateRelease release, string destination, CancellationToken token)
        {
            await using FileStream file = new(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous);
            await retry.ExecuteAsync(attemptToken => DownloadCoreAsync(release, file, attemptToken), token);
        }

        private async Task<long> DownloadCoreAsync(UpdateRelease release, FileStream file, CancellationToken token)
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
