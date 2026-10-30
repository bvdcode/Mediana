// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Core.Metadata;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MediaTagTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            await VerifyOpeningSnapshotAsync(root);
            foreach (string name in new[] { "stereo-vbr-cover", "stereo-cbr-id3v23", "mono-speech" })
            {
                MediaInfo info = await ProbeAsync(Path.Combine(root, "Mp3", name + ".mp3"));
                Check(info, "id3v2", "TIT2", "MP3 reference");
                Check(info, "id3v2", "TPE1", "Mediana");
            }
            using JsonDocument flac = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Flac", "references.json")));
            foreach (JsonElement entry in flac.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                MediaInfo info = await ProbeAsync(Path.Combine(root, "Flac", entry.GetProperty("name").GetString() + ".flac"));
                foreach (JsonProperty tag in entry.GetProperty("tags").EnumerateObject())
                {
                    Check(info, "vorbis", tag.Name, tag.Value.GetString()!);
                }
            }
            using JsonDocument mp4 = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "references.json")));
            foreach (JsonElement entry in mp4.RootElement.GetProperty("files").EnumerateArray())
            {
                string file = entry.GetProperty("file").GetString()!;
                MediaInfo info = await ProbeAsync(Path.Combine(root, "Metadata", file));
                JsonElement tags = entry.GetProperty("reference").GetProperty("format").GetProperty("tags");
                (string Atom, string Tag)[] keys = [("©nam", "title"), ("©ART", "artist"), ("aART", "album_artist"),
                    ("©wrt", "composer"), ("©alb", "album"), ("©day", "date"), ("©cmt", "comment"), ("©gen", "genre"), ("©too", "encoder")];
                foreach ((string atom, string tag) in keys)
                {
                    string nativeName;
                    string nativeNamespace;
                    switch (file)
                    {
                        case "covers.m4a":
                            nativeName = atom;
                            nativeNamespace = "mdir";
                            break;
                        case "mdta.mp4":
                            nativeName = tag;
                            nativeNamespace = "mdta";
                            break;
                        default:
                            throw new InvalidDataException("Unexpected metadata reference file.");
                    }
                    Check(info, nativeNamespace, nativeName, tags.GetProperty(tag).GetString()!, 0);
                }
                if (file == "mdta.mp4")
                {
                    Check(info, "mdta", "custom.example", "Value 🧪", 0);
                }
                if (info.Container.Tags!.Any(tag => tag.Name is "covr" or "trkn" or "disk"))
                {
                    throw new InvalidDataException("Binary metadata was misrepresented as text.");
                }
            }
        }

        private static async Task VerifyOpeningSnapshotAsync(string root)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            await using StreamByteSource source = new(File.OpenRead(Path.Combine(root, "Mp3", "mono-low-rate.mp3")));
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            if (reader.CodedDuration is not null)
            {
                throw new InvalidDataException("The no-Xing reference must not declare a coded duration.");
            }
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                packet.Dispose();
            }
            await reader.DisposeAsync();
            if (reader.CodedDuration is null || reader.Info.DeclaredDuration is not null || allocator.AllocatedBytes != 0
                || reader.Info.Tags!.Single(tag => tag.Name == "TIT2").Values[0] != "MP3 reference")
            {
                throw new InvalidDataException("Delayed metadata access changed the opening snapshot or retained media buffers.");
            }
        }

        private static async Task<MediaInfo> ProbeAsync(string path)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            return await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
        }

        private static void Check(MediaInfo info, string keyNamespace, string name, string value, uint? locale = null)
        {
            MediaTag tag = (info.Container.Tags ?? throw new InvalidDataException("Header text tags are unavailable."))
                .Single(tag => tag.Namespace == keyNamespace && tag.Name == name);
            if (!tag.Values.SequenceEqual(new[] { value }) || tag.Description is not null || tag.Locale != locale)
            {
                throw new InvalidDataException($"Common text tag {keyNamespace}/{name} differs from its independent reference.");
            }
        }
    }
}
