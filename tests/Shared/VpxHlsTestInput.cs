// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Vp8;
using Mediana.Codecs.Vp9;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class VpxHlsTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            foreach ((string group, string name, int pictures) in new[]
            { ("Vp8", "motion", 24), ("Vp8", "altref", 72), ("Vp9", "main8", 16), ("Vp9", "altref", 48) })
            {
                IVideoDecoderFactory decoder = group switch
                {
                    "Vp8" => new Vp8DecoderFactory(),
                    "Vp9" => new Vp9DecoderFactory(),
                    _ => throw new InvalidDataException("Unknown WebM video reference codec.")
                };
                byte[] data = await File.ReadAllBytesAsync(Path.Combine(root, group, name + ".webm"));
                await VerifyAsync(data, decoder, pictures, tailOnly: true);
            }
        }

        public static async Task VerifyAsync(byte[] data, IVideoDecoderFactory decoder, int pictures, bool tailOnly = false)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            VideoFormat format = await ReadFormatAsync(data, decoder, allocator);
            await using (TrackingRandomAccessSource source = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Require(reader.GetPresentationUnitCount(0) == pictures, "The WebM displayed-picture index changed its count.");
                long retained = allocator.AllocatedBytes;
                source.Reads.Clear();
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, decoder, format,
                    new MediaDuration(1, new(1, 1)), allocator, preparation: new VideoPreparationOptions { MaximumWidth = 32 });
                using MemoryStream initialization = new();
                await session.WriteInitializationAsync(initialization);
                for (long unit = 0; unit < reader.GetPresentationUnitCount(0); unit++)
                {
                    ContainerVideoPresentationUnit presentation = reader.GetPresentationUnit(0, unit);
                    Require(presentation.Duration == reader.GetPresentationDuration(0, presentation.PacketIndex),
                        "The displayed index and decoder duration hook disagree.");
                }
                Require(source.Reads.Count == 0 && allocator.AllocatedBytes == retained,
                    "WebM HLS planning or initialization read encoded bytes or retained temporary storage.");
                IEnumerable<int> requests = tailOnly ? [session.Windows.Count - 1, session.Windows.Count - 1]
                    : Enumerable.Range(0, session.Windows.Count).Reverse().Append(session.Windows.Count - 1);
                Dictionary<int, byte[]> fragments = [];
                long observed = 0;
                foreach (int number in requests)
                {
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    byte[] fragment = output.ToArray();
                    Require(allocator.AllocatedBytes == retained, "A WebM HLS request retained encoder, decoder or fragment storage.");
                    if (fragments.TryGetValue(number, out byte[]? previous))
                    {
                        Require(previous.AsSpan().SequenceEqual(fragment), "Repeated cold WebM HLS output changed.");
                        continue;
                    }
                    fragments.Add(number, fragment);
                    ReadOnlyMemory<byte> track = OnDemandHlsTestInput.Box(OnDemandHlsTestInput.Box(fragment, "moof"), "traf");
                    ReadOnlyMemory<byte> run = OnDemandHlsTestInput.Box(track, "trun");
                    uint count = BinaryPrimitives.ReadUInt32BigEndian(run.Span[4..]);
                    long ticks = checked((long)BinaryPrimitives.ReadUInt64BigEndian(OnDemandHlsTestInput.Box(track, "tfdt").Span[4..]));
                    HlsVideoWindow window = session.Windows[number];
                    Require(window.Start == new MediaTimestamp(ticks, session.Stream.TimeBase), "A WebM HLS fragment changed its exact start.");
                    for (int packet = 0; packet < count; packet++)
                    { ticks += BinaryPrimitives.ReadUInt32BigEndian(run.Span[(12 + 16 * packet)..]); }
                    Require(window.End == new MediaTimestamp(ticks, session.Stream.TimeBase), "A WebM HLS fragment changed its exact end.");
                    long expected = 0;
                    for (long unit = 0; unit < reader.GetPresentationUnitCount(0); unit++)
                    {
                        MediaTimestamp pts = reader.GetPresentationUnit(0, unit).PresentationTime!.Value;
                        if (pts.CompareTo(window.Start) >= 0 && pts.CompareTo(window.End) < 0) { expected++; }
                    }
                    Require(count == expected, "A WebM HLS fragment changed its visible-picture sample count.");
                    observed += count;
                }
                if (!tailOnly) { Require(observed == pictures, "The full WebM HLS sequence lost or duplicated visible pictures."); }
            }
            Require(allocator.AllocatedBytes == 0, "WebM HLS retained its source index or picture storage after disposal.");
        }

        internal static async Task<VideoFormat> ReadFormatAsync(byte[] data, IVideoDecoderFactory decoder, MediaBufferAllocator allocator)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).AddVideoDecoder(decoder).Build();
            await using TrackingRandomAccessSource source = new(data);
            await using VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null);
            using VideoFrame first = (await session.ReadFrameAsync())!;
            return first.Format;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }
    }
}
