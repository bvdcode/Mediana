// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Svg;
using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Mediana.Drawing.Emoji.Fluent
{
    /// <summary>Resolves complete Unicode text elements to Microsoft Fluent Flat SVG graphics.
    /// Uses embedded resources only. Resolution and graphics support concurrent callers.
    /// Disposal releases the archive and cache; graphics already returned remain usable.</summary>
    public class FluentEmojiProvider : IInlineGraphicProvider, IDisposable
    {
        private const int MaximumSequenceLength = 64;
        private const int MaximumSvgBytes = 64 * 1024;
        private const int MaximumBlockBytes = 128 * 1024;
        private static readonly SvgReaderOptions ReaderOptions = new() { MaximumInputBytes = MaximumSvgBytes, MaximumElements = 1024, MaximumPathCommands = 20_000 };
        private readonly object _gate = new();
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, FluentEmojiEntry> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InlineGraphic> _cache = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        private readonly int _maximumCachedGraphics;
        private bool _disposed;

        public FluentEmojiProvider(FluentEmojiOptions? options = null)
        {
            options ??= new();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumCachedGraphics);
            _maximumCachedGraphics = options.MaximumCachedGraphics;
            _archive = new(new MemoryStream(FluentEmojiResources.Archive, writable: false), ZipArchiveMode.Read);
            bool completed = false;
            try
            {
                using StreamReader index = new(_archive.GetEntry("index.tsv")!.Open(), Encoding.UTF8);
                while (index.ReadLine() is string row)
                {
                    string[] fields = row.Split('\t');
                    string key = string.Concat(fields[0].Split('-').Select(point => char.ConvertFromUtf32(int.Parse(point, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                    int block = int.Parse(fields[2], CultureInfo.InvariantCulture);
                    _entries.Add(key, new(_archive.GetEntry($"blocks/{block:D4}.br")!, int.Parse(fields[3], CultureInfo.InvariantCulture),
                        int.Parse(fields[4], CultureInfo.InvariantCulture), fields[1] == "1"));
                }
                completed = true;
            }
            finally
            {
                if (!completed) { _archive.Dispose(); }
            }
        }

        /// <summary>The number of distinct bundled Unicode sequences, including skin-tone variants.</summary>
        public int Count => _entries.Count;

        public InlineGraphic? Resolve(ReadOnlySpan<char> textElement, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (textElement.IsEmpty || textElement.Length > MaximumSequenceLength || textElement.Contains('\ufe0e')) { return null; }
                Span<char> normalized = stackalloc char[textElement.Length];
                int length = 0;
                bool selector = false;
                int previous = 0;
                for (int offset = 0; offset < textElement.Length;)
                {
                    if (Rune.DecodeFromUtf16(textElement[offset..], out Rune character, out int consumed) != OperationStatus.Done) { return null; }
                    if (character.Value == 0xfe0f)
                    {
                        if (previous is 0 or 0xfe0f or 0x200d or >= 0x1f3fb and <= 0x1f3ff) { return null; }
                        selector = true;
                    }
                    else { length += character.EncodeToUtf16(normalized[length..]); }
                    previous = character.Value;
                    offset += consumed;
                }
                ReadOnlySpan<char> key = normalized[..length];
                if (!_entries.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out FluentEmojiEntry? entry)
                    || entry.RequiresSelector && !selector) { return null; }
                if (_cache.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out InlineGraphic? cached)) { return cached; }
                cancellationToken.ThrowIfCancellationRequested();
                SvgDocument document = Read(entry, cancellationToken);
                if (document.UnsupportedFeatures.Count != 0) { throw new NotSupportedException("Unsupported Fluent SVG: " + string.Join("; ", document.UnsupportedFeatures)); }
                InlineGraphic graphic = new FluentEmojiGraphic(document);
                if (_cache.Count == _maximumCachedGraphics) { _cache.Remove(_order.Dequeue()); }
                string retained = new(key);
                _cache.Add(retained, graphic);
                _order.Enqueue(retained);
                return graphic;
            }
        }

        private static SvgDocument Read(FluentEmojiEntry entry, CancellationToken token)
        {
            if (entry.Length <= 0 || entry.Length > MaximumSvgBytes || entry.Offset < 0 || entry.Offset > MaximumBlockBytes - entry.Length)
            {
                throw new InvalidDataException("Fluent SVG exceeds its resource bound.");
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(MaximumBlockBytes);
            try
            {
                using Stream source = entry.Block.Open();
                using BrotliStream brotli = new(source, CompressionMode.Decompress);
                int length = 0, read;
                while (length < MaximumBlockBytes && (read = brotli.Read(buffer.AsSpan(length, MaximumBlockBytes - length))) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    length += read;
                }
                if (length < entry.Offset + entry.Length || brotli.ReadByte() != -1) { throw new InvalidDataException("Invalid Fluent resource block."); }
                return SvgReader.Read(buffer.AsMemory(entry.Offset, entry.Length), ReaderOptions, token);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) { return; }
                _disposed = true;
                _cache.Clear();
                _order.Clear();
                _archive.Dispose();
            }
        }
    }
}
