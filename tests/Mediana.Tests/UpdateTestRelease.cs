// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mediana.Cli.Updates;

namespace Mediana.Tests
{
    internal class UpdateTestRelease(byte[] payload, string version = "1.2.3")
    {
        public JsonObject Manifest { get; } = CreateManifest(payload, version);

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Uri url = request.RequestUri ?? throw new InvalidOperationException("Missing request URL.");
            Assert.Equal(HttpMethod.Get, request.Method);
            HttpResponseMessage response = new(HttpStatusCode.OK) { RequestMessage = request };
            if (url.AbsoluteUri == "https://releases.splidex.com/mediana/latest.json")
            {
                response.Content = new StringContent(Manifest.ToJsonString());
                return response;
            }
            foreach (ReleasePlatform platform in new[] { ReleasePlatform.WindowsX64, ReleasePlatform.LinuxX64, ReleasePlatform.MacOsArm64 })
            {
                string name = ReleaseAssets.GetName(platform, Version.Parse(version));
                if (url.AbsoluteUri == $"https://releases.splidex.com/mediana/{version}/{name}")
                {
                    response.Content = new ByteArrayContent(payload);
                    return response;
                }
            }
            response.Dispose();
            throw new InvalidOperationException($"Unexpected update request: {request.Method} {url}");
        }

        private static JsonObject CreateManifest(byte[] payload, string version)
        {
            JsonArray assets = [];
            foreach (ReleasePlatform platform in new[] { ReleasePlatform.WindowsX64, ReleasePlatform.LinuxX64, ReleasePlatform.MacOsArm64 })
            {
                assets.Add(new JsonObject
                {
                    ["name"] = ReleaseAssets.GetName(platform, Version.Parse(version)),
                    ["size"] = payload.LongLength,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(payload))
                });
            }
            return new JsonObject { ["version"] = version, ["assets"] = assets };
        }
    }
}
