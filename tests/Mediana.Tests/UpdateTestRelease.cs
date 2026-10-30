// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mediana.Cli.Updates;

namespace Mediana.Tests
{
    internal static class UpdateTestRelease
    {
        public static JsonObject Metadata(string version, byte[] payload)
        {
            JsonArray assets = [];
            foreach (ReleasePlatform platform in new[] { ReleasePlatform.WindowsX64, ReleasePlatform.LinuxX64, ReleasePlatform.MacOsArm64 })
            {
                string name = ReleaseAssets.GetName(platform, Version.Parse(version));
                assets.Add(new JsonObject
                {
                    ["name"] = name,
                    ["state"] = "uploaded",
                    ["size"] = payload.Length,
                    ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)),
                    ["browser_download_url"] = $"https://github.com/bvdcode/Mediana/releases/download/v{version}/{name}"
                });
            }
            return new JsonObject
            {
                ["tag_name"] = "v" + version,
                ["draft"] = false,
                ["prerelease"] = false,
                ["assets"] = assets
            };
        }
    }
}
