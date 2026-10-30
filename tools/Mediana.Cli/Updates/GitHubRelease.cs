// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli.Updates
{
    internal record GitHubRelease(Version Version, Uri DownloadUrl, long Size, byte[] Sha256);
}
