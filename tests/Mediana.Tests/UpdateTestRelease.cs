// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Security.Cryptography;
using Mediana.Cli.Updates;

namespace Mediana.Tests
{
    internal class UpdateTestRelease(byte[] payload, string version = "1.2.3")
    {
        public Uri ReleaseUri { get; set; } = new($"https://github.com/bvdcode/Mediana/releases/tag/v{version}");
        public long? Size { get; set; } = payload.LongLength;
        public string? ChecksumOverride { get; set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Uri url = request.RequestUri ?? throw new InvalidOperationException("Missing request URL.");
            HttpResponseMessage response = new(HttpStatusCode.OK) { RequestMessage = request };
            if (url.AbsoluteUri == "https://github.com/bvdcode/Mediana/releases/latest")
            {
                Assert.Equal(HttpMethod.Head, request.Method);
                request.RequestUri = ReleaseUri;
                return response;
            }
            foreach (ReleasePlatform platform in new[] { ReleasePlatform.WindowsX64, ReleasePlatform.LinuxX64, ReleasePlatform.MacOsArm64 })
            {
                string name = ReleaseAssets.GetName(platform, Version.Parse(version));
                string downloadUrl = $"https://github.com/bvdcode/Mediana/releases/download/v{version}/{name}";
                if (url.AbsoluteUri == downloadUrl + ".sha256")
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    response.Content = new StringContent(ChecksumOverride ?? $"{Convert.ToHexString(SHA256.HashData(payload))}  {name}\n");
                    return response;
                }
                if (url.AbsoluteUri == downloadUrl)
                {
                    if (request.Method == HttpMethod.Head)
                    {
                        response.Content.Headers.ContentLength = Size;
                    }
                    else
                    {
                        Assert.Equal(HttpMethod.Get, request.Method);
                        response.Content = new ByteArrayContent(payload);
                    }
                    return response;
                }
            }
            response.Dispose();
            throw new InvalidOperationException($"Unexpected update request: {request.Method} {url}");
        }
    }
}
