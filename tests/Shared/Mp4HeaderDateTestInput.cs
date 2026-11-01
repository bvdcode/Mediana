// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4HeaderDateTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "header-dates.reference.json")));
            foreach (JsonElement file in reference.RootElement.GetProperty("files").EnumerateArray())
            {
                string path = Path.Combine(root, file.GetProperty("file").GetString()!);
                if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) != file.GetProperty("sha256").GetString())
                {
                    throw new InvalidDataException("A header-date fixture changed.");
                }
                DateTimeKind kind = file.GetProperty("kind").GetString() switch
                {
                    "Utc" => DateTimeKind.Utc,
                    "Unspecified" => DateTimeKind.Unspecified,
                    _ => throw new InvalidDataException("The header-date reference has an invalid time-zone kind.")
                };
                MediaInfo info;
                await using (RandomAccessStreamByteSource input = new(File.OpenRead(path)))
                {
                    MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).Build();
                    info = await engine.ProbeAsync(input);
                }
                VerifyDates(info.Container!.Dates!, [file.GetProperty("header")], kind);
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using FileStream stream = File.OpenRead(path);
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                await source.ReadAsync(new byte[5]);
                Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                await using (reader)
                {
                    VerifyHeader(reader.HeaderDates, file.GetProperty("header"), kind);
                    if (reader.MajorBrand != file.GetProperty("major_brand").GetString())
                    {
                        throw new InvalidDataException("Header dates lost their original brand context.");
                    }
                    foreach (JsonElement track in file.GetProperty("tracks").EnumerateArray())
                    {
                        int index = track.GetProperty("index").GetInt32();
                        VerifyHeader(reader.Tracks[index].HeaderDates, track.GetProperty("header"), kind);
                        VerifyHeader(reader.Tracks[index].MediaDates, track.GetProperty("media"), kind);
                        if (Convert.ToHexStringLower(SHA256.HashData(reader.Streams[index].Configuration.Span)) !=
                            track.GetProperty("configuration_sha256").GetString())
                        {
                            throw new InvalidDataException("An encoded stream configuration differs from the independent reference.");
                        }
                        VerifyDates(info.Container.StreamInfo![index].Dates!, [track.GetProperty("header"), track.GetProperty("media")], kind);
                    }
                    await Mp4TrackMetadataTestInput.VerifyPacketsAsync(reader, file.GetProperty("tracks"));
                }
                VerifyDates(reader.Info.Dates!, [file.GetProperty("header")], kind);
                foreach (JsonElement track in file.GetProperty("tracks").EnumerateArray())
                {
                    int index = track.GetProperty("index").GetInt32();
                    VerifyDates(reader.Info.StreamInfo![index].Dates!, [track.GetProperty("header"), track.GetProperty("media")], kind);
                }
                if (allocator.AllocatedBytes != 0 || stream.Position != 5 || !stream.CanRead)
                {
                    throw new InvalidDataException("Header dates retained movie buffers or changed source ownership/cursor.");
                }
            }
        }

        private static void VerifyHeader(Mp4HeaderDates actual, JsonElement expected, DateTimeKind kind)
        {
            if (actual.CreationSeconds != expected.GetProperty("creation_seconds").GetUInt64() ||
                actual.ModificationSeconds != expected.GetProperty("modification_seconds").GetUInt64() || actual.Kind != kind)
            {
                throw new InvalidDataException("Unsigned header-date declarations differ from original bytes.");
            }
            VerifyValue(actual.CreationTime, expected.GetProperty("creation").GetString(), kind);
            VerifyValue(actual.ModificationTime, expected.GetProperty("modification").GetString(), kind);
        }

        private static void VerifyValue(DateTime? actual, string? expected, DateTimeKind kind)
        {
            if (actual?.ToString("O", CultureInfo.InvariantCulture) != expected ||
                (actual is DateTime value && value.Kind != kind))
            {
                throw new InvalidDataException("A header calendar value was shifted, truncated or given an incorrect time zone.");
            }
        }

        private static void VerifyDates(IReadOnlyList<MediaDateTimeTag> actual, JsonElement[] headers, DateTimeKind kind)
        {
            int index = 0;
            foreach (JsonElement header in headers)
            {
                foreach ((string field, string name) in new[] { ("creation", "creation_time"), ("modification", "modification_time") })
                {
                    string? value = header.GetProperty(field).GetString();
                    if (value is null)
                    {
                        continue;
                    }
                    if (index == actual.Count)
                    {
                        throw new InvalidDataException("Common calendar metadata lost a declared header date.");
                    }
                    MediaDateTimeTag date = actual[index++];
                    if (date.Namespace != header.GetProperty("namespace").GetString() || date.Name != name ||
                        date.SourcePath != header.GetProperty("source_path").GetString())
                    {
                        throw new InvalidDataException("A calendar date moved between movie, track or media scopes.");
                    }
                    VerifyValue(date.Value, value, kind);
                }
            }
            if (index != actual.Count)
            {
                throw new InvalidDataException("Common calendar metadata invented a date from an unspecified or unrepresentable value.");
            }
        }
    }
}
