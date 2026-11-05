// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal class WordPreviewDecoder : IOpenXmlPageDecoder
    {
        private static readonly WordReaderOptions ReaderOptions = new() { AllowFractionalMeasurements = true, DefaultPageSize = WordPageSize.A4 };
        private static readonly WordRenderOptions RenderOptions = new() { MaximumWidth = 1200, MaximumHeight = 1200, ImageColorResolver = SdrPreviewColorPolicy.Resolve };

        public string ContentType => WordReader.DocumentContentType;

        public ImageInfo ReadInfo(OpenXmlPackage package, string mainPart, CancellationToken cancellationToken = default) =>
            new WordPageDecoder(ReaderOptions).ReadInfo(package, mainPart, cancellationToken);

        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            WordFontCollection fonts = await WordFontCollection.LoadAsync(InstalledFontFiles(), cancellationToken: cancellationToken);
            WordLayoutOptions layout = new()
            {
                SubstituteMissingFonts = true,
                FontResolver = Resolve,
                FallbackFontResolver = fonts.ResolveCharacter
            };
            return await new WordPageDecoder(ReaderOptions, layout, RenderOptions).DecodeAsync(package, mainPart, request, allocator, cancellationToken);

            ReadOnlyMemory<byte> Resolve(WordFontRequest requested)
            {
                ReadOnlyMemory<byte> exact = fonts.Resolve(requested);
                if (!exact.IsEmpty) { return exact; }
                string? replacement = requested.Family.ToUpperInvariant() switch
                {
                    "ARIAL" => "Liberation Sans",
                    "TIMES NEW ROMAN" => "Liberation Serif",
                    "COURIER NEW" => "Liberation Mono",
                    "CALIBRI" => "Carlito",
                    _ => null
                };
                return replacement is null ? ReadOnlyMemory<byte>.Empty : fonts.Resolve(requested with { Family = replacement });
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
