// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;

namespace Mediana.Cli
{
    internal static class CommandTextResources
    {
        private static readonly Lazy<Task<TextOptions>> Resources = new(LoadAsync);

        internal static async ValueTask<TextOptions> GetAsync(CancellationToken cancellationToken) =>
            await Resources.Value.WaitAsync(cancellationToken);

        private static async Task<TextOptions> LoadAsync()
        {
            FontCollection fonts = await FontCollection.LoadAsync(InstalledFontFiles());
            return new()
            {
                SubstituteMissingFonts = true,
                FontResolver = Resolve,
                FallbackFontResolver = fonts.ResolveCharacter,
                InlineGraphicProvider = new FluentEmojiProvider()
            };

            ReadOnlyMemory<byte> Resolve(FontRequest request)
            {
                ReadOnlyMemory<byte> exact = fonts.Resolve(request);
                if (!exact.IsEmpty) { return exact; }
                string? family = request.Family.ToUpperInvariant() switch
                {
                    "SANS-SERIF" or "HELVETICA" or "ARIAL" or "ARIALMT" => "Liberation Sans",
                    "SERIF" or "TIMES" or "TIMES NEW ROMAN" or "TIMESNEWROMANPSMT" => "Liberation Serif",
                    "MONOSPACE" or "COURIER" or "COURIER NEW" or "COURIERNEWPSMT" => "Liberation Mono",
                    "CALIBRI" => "Carlito",
                    _ => null
                };
                return family is null ? ReadOnlyMemory<byte>.Empty : fonts.Resolve(request with { Family = family });
            }
        }

        private static IEnumerable<string> InstalledFontFiles()
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
