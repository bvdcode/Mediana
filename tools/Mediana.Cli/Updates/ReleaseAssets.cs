// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Cli.Updates
{
    internal static class ReleaseAssets
    {
        public const string Repository = "bvdcode/Mediana";

        public static ReleasePlatform CurrentPlatform =>
            (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture) switch
            {
                (true, false, false, Architecture.X64) => ReleasePlatform.WindowsX64,
                (false, true, false, Architecture.X64) => ReleasePlatform.LinuxX64,
                (false, false, true, Architecture.Arm64) => ReleasePlatform.MacOsArm64,
                _ => throw new PlatformNotSupportedException("Self-update supports Windows x64, Linux x64 and macOS ARM64.")
            };

        public static string GetName(ReleasePlatform platform, Version version)
        {
            return platform switch
            {
                ReleasePlatform.WindowsX64 => $"Mediana-{version}-win-x64.exe",
                ReleasePlatform.LinuxX64 => $"Mediana-{version}-linux-x64",
                ReleasePlatform.MacOsArm64 => $"Mediana-{version}-osx-arm64",
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported release platform.")
            };
        }
    }
}
