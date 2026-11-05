// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.IO.Compression;
using System.Text;

if (args.Length != 2) { throw new ArgumentException("Usage: compress-fluent-emoji.cs input.zip output.zip"); }
const int maximumBlockBytes = 128 * 1024;
using ZipArchive source = ZipFile.OpenRead(args[0]);
using FileStream output = new(args[1], FileMode.Create, FileAccess.Write, FileShare.None);
using ZipArchive archive = new(output, ZipArchiveMode.Create);
using StreamReader index = new(source.GetEntry("index.tsv")!.Open());
using MemoryStream block = new(maximumBlockBytes);
List<string> rows = [];
int blockNumber = 0;
while (index.ReadLine() is string row)
{
    string key = row[..row.IndexOf('\t')];
    ZipArchiveEntry original = source.GetEntry(key + ".svg")!;
    if (original.Length > maximumBlockBytes) { throw new InvalidDataException("SVG exceeds block size."); }
    if (block.Length + original.Length > maximumBlockBytes) { Flush(); }
    rows.Add(string.Create(CultureInfo.InvariantCulture, $"{row}\t{blockNumber}\t{block.Length}\t{original.Length}"));
    using Stream input = original.Open();
    input.CopyTo(block);
}
Flush();
using (StreamWriter writer = new(Entry("index.tsv", CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
{
    foreach (string row in rows) { writer.Write(row); writer.Write('\n'); }
}
foreach (string name in new[] { "LICENSE.txt", "manifest.json" })
{
    using Stream input = source.GetEntry(name)!.Open();
    using Stream destination = Entry(name, CompressionLevel.Optimal).Open();
    input.CopyTo(destination);
}

void Flush()
{
    if (block.Length == 0) { return; }
    using Stream destination = Entry($"blocks/{blockNumber:D4}.br", CompressionLevel.NoCompression).Open();
    using (BrotliStream brotli = new(destination, CompressionLevel.SmallestSize, leaveOpen: true))
    {
        brotli.Write(block.GetBuffer().AsSpan(0, checked((int)block.Length)));
    }
    block.SetLength(0);
    block.Position = 0;
    blockNumber++;
}

ZipArchiveEntry Entry(string name, CompressionLevel compression)
{
    ZipArchiveEntry entry = archive.CreateEntry(name, compression);
    entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    entry.ExternalAttributes = unchecked((int)0x81a40000);
    return entry;
}
