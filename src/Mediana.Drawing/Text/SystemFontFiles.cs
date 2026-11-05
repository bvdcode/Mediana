// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal static class SystemFontFiles
    {
        internal static IEnumerable<string> Enumerate()
        {
            List<string> directories = [];
            if (OperatingSystem.IsWindows())
            {
                directories.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
                directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"));
            }
            else if (OperatingSystem.IsLinux())
            {
                directories.Add("/usr/share/fonts");
                directories.Add("/usr/local/share/fonts");
                directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts"));
            }
            else if (OperatingSystem.IsMacOS())
            {
                directories.Add("/System/Library/Fonts");
                directories.Add("/Library/Fonts");
                directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts"));
            }
            foreach (string directory in directories.Distinct(StringComparer.Ordinal))
            {
                if (!Directory.Exists(directory)) { continue; }
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file).Equals(".ttf", StringComparison.OrdinalIgnoreCase)) { yield return file; }
                }
            }
        }
    }
}
