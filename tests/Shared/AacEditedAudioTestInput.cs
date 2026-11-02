// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AacEditedAudioTestInput
    {
        public static async Task VerifyTimeWindowAsync(string root, string name, bool indexed = false, byte[]? encoded = null)
        {
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = references.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            int rate = entry.GetProperty("sampleRate").GetInt32();
            int channels = entry.GetProperty("channels").GetInt32();
            if (entry.GetProperty("pns").GetBoolean())
            {
                throw new ArgumentException("Exact sample windows require a reference without noise substitution.", nameof(name));
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            foreach ((MediaTimestamp start, MediaTimestamp? end) in new (MediaTimestamp, MediaTimestamp?)[]
                { (new(-1, new(1, 97)), new(1, new(1, 997))), (new(1, new(1, 997)), new(21, new(1, 97))),
                  (new(7, new(1, 31)), null), (new(1, new(1, 1)), new(2, new(1, 1))),
                  (new(113, new(1, 997)), new(114, new(1, 997))), (new(0, new(1, 1)), new(0, new(1, 1))) })
            {
                MediaBufferAllocator allocator = new(128 * 1024);
                await using Stream stream = encoded is null ? File.OpenRead(Path.Combine(root, name + ".m4a"))
                    : new MemoryStream(encoded, writable: false);
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                Mp4AudioSampleRange edit;
                await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
                {
                    edit = reader.Tracks[0].Edits.Count == 0
                        ? new(0, reference.Length / (channels * sizeof(float)), new(0, new(1, rate)))
                        : Mp4AudioSampleRange.FromEditList(reader.Tracks[0], rate);
                }
                Mp4TimestampMode mode = indexed ? Mp4TimestampMode.LinearMovie : Mp4TimestampMode.Media;
                MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new() { TimestampMode = mode }))
                    .AddAudioDecoder(new AacDecoderFactory()).Build();
                await using AudioSession session = await engine.OpenAudioAsync(source);
                if (indexed)
                {
                    await session.SeekAsync(start);
                }
                await using AudioTrimmer edited = indexed
                    ? AudioTrimmer.CreateTimeWindow(session.ReadFrameAsync, edit.PresentationTime, edit.GetPresentationEnd(rate), allocator)
                    : AudioTrimmer.CreateWindow(session.ReadFrameAsync, edit.StartSample, edit.SampleCount, allocator, edit.PresentationTime);
                await using AudioTrimmer window = AudioTrimmer.CreateTimeWindow(edited.ReadFrameAsync, start, end, allocator);
                long available = reference.Length / (channels * sizeof(float));
                int[] selected = Enumerable.Range(0, checked((int)Math.Max(0, Math.Min(edit.SampleCount, available - edit.StartSample)))).Where(index =>
                {
                    MediaTimestamp timestamp = AudioTimeWindowTestInput.At(edit.PresentationTime, index, rate);
                    return timestamp.CompareTo(start) >= 0 && (end is null || timestamp.CompareTo(end.Value) < 0);
                }).ToArray();
                int position = 0;
                while (true)
                {
                    using AudioFrame? frame = await window.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (position >= selected.Length || frame.Timestamp != AudioTimeWindowTestInput.At(edit.PresentationTime, selected[position], rate))
                    {
                        throw new InvalidDataException("The edited AAC time window changed its exact presentation grid.");
                    }
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    for (int sample = 0; sample < frame.SamplesPerChannel; sample++)
                    {
                        for (int channel = 0; channel < channels; channel++)
                        {
                            int offset = checked((int)(edit.StartSample + selected[position]) * channels * sizeof(float) + channel * sizeof(float));
                            float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(offset, sizeof(float)));
                            float value = actual[sample * channels + channel];
                            if (!float.IsFinite(value) || Math.Abs(value - expected) > 2e-6)
                            {
                                throw new InvalidDataException("The AAC presentation window differs from independently decoded samples.");
                            }
                        }
                        position++;
                    }
                }
                await window.DisposeAsync();
                await edited.DisposeAsync();
                await session.DisposeAsync();
                if (position != selected.Length || allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0
                    || !stream.CanRead || stream.Position != 0)
                {
                    throw new InvalidDataException("The AAC presentation window lost samples or retained media buffers.");
                }
            }
        }

        public static async Task VerifyAsync(string root, string name, bool exactEdit)
        {
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = references.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            int rate = entry.GetProperty("sampleRate").GetInt32();
            int channels = entry.GetProperty("channels").GetInt32();
            if (entry.GetProperty("pns").GetBoolean())
            {
                throw new ArgumentException("Edited sample comparisons require a reference without noise substitution.", nameof(name));
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            MediaBufferAllocator allocator = new(128 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".m4a")));
            Mp4AudioSampleRange range;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                range = Mp4AudioSampleRange.FromEditList(reader.Tracks[0], rate);
            }
            if (range.StartSample != 1024 || range.SampleCount != entry.GetProperty("sourceSamples").GetInt64()
                || range.PresentationTime != new MediaTimestamp(0, new TimeBase(1, rate)))
            {
                throw new InvalidDataException("The AAC reference edit changed.");
            }
            int startBytes = checked((int)range.StartSample * channels * sizeof(float));
            int expectedBytes = exactEdit ? checked((int)range.SampleCount * channels * sizeof(float)) : reference.Length - startBytes;
            string hashName = exactEdit ? "exactEditPcmSha256" : "defaultPcmSha256";
            if (Convert.ToHexStringLower(SHA256.HashData(reference.AsSpan(startBytes, expectedBytes))) != entry.GetProperty(hashName).GetString())
            {
                throw new InvalidDataException("The edited PCM no longer matches the independent decoding command.");
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddAudioDecoder(new AacDecoderFactory()).Build();
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            long availableSamples = reference.LongLength / (channels * sizeof(float));
            await using AudioTrimmer trimmer = exactEdit
                ? new(session.ReadFrameAsync, range.StartSample, range.SampleCount, allocator, range.PresentationTime)
                : AudioTrimmer.CreateWindow(session.ReadFrameAsync, range.StartSample, availableSamples, allocator, range.PresentationTime);
            int bytes = 0;
            AudioFrame? retained = null;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await trimmer.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    int position = bytes / (channels * sizeof(float));
                    if (frame.Timestamp != new MediaTimestamp(position, new TimeBase(1, rate))
                        || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, rate))
                        || bytes + frame.GetPlane(0).Length > expectedBytes)
                    {
                        throw new InvalidDataException("Edited AAC changed its output timing or sample count.");
                    }
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    for (int index = 0; index < actual.Length; index++)
                    {
                        float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(startBytes + bytes + index * sizeof(float), sizeof(float)));
                        if (!float.IsFinite(actual[index]) || Math.Abs(actual[index] - expected) > 2e-6)
                        {
                            throw new InvalidDataException("Edited AAC PCM differs from independent decoding.");
                        }
                    }
                    bytes += frame.GetPlane(0).Length;
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                await trimmer.DisposeAsync();
                await session.DisposeAsync();
                if (bytes != expectedBytes || retained is null ||
                    allocator.AllocatedBytes + session.AllocatedBufferBytes != retained.GetPlane(0).Length)
                {
                    throw new InvalidOperationException("Edited AAC lost output or retained decoder storage after disposal.");
                }
                _ = retained.GetPlane(0).Span[0];
            }
            finally
            {
                retained?.Dispose();
            }
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Edited AAC retained PCM storage after its last output was released.");
            }
        }
    }
}
