using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AacDecodeTestInput
    {
        public static readonly string[] Names = ["lc-mono-48", "lc-stereo-44", "lc-pns-48"];

        public static async Task<double> VerifyAsync(string root, string name)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = document.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            int rate = entry.GetProperty("sampleRate").GetInt32();
            int channels = entry.GetProperty("channels").GetInt32();
            bool noise = entry.GetProperty("pns").GetBoolean();
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".m4a"));
            VerifyHash(expected, entry.GetProperty("pcmSha256").GetString());
            VerifyHash(input, entry.GetProperty("inputSha256").GetString());
            VerifyHash(await File.ReadAllBytesAsync(Path.Combine(root, name + ".wav")), entry.GetProperty("sourceSha256").GetString());
            JsonElement packets = entry.GetProperty("reference").GetProperty("packets");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using MemoryStream file = new(input, writable: false);
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            AudioFrame? retained = null;
            byte[]? lastBytes = null;
            List<byte> actual = [];
            int frames = 0;
            double maximum = 0;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (frames >= packets.GetArrayLength() || frame.Format.SampleFormat != AudioSampleFormat.Float32
                        || frame.Format.SampleRate != rate || frame.Format.Layout != AudioStorageLayout.Interleaved
                        || !frame.Format.Channels.SequenceEqual(ChannelNames(channels)) || frame.SamplesPerChannel != 1024
                        || frame.Timestamp != new MediaTimestamp(packets[frames].GetProperty("pts").GetInt64(), new TimeBase(1, rate))
                        || frame.Duration != new MediaDuration(1024, new TimeBase(1, rate)))
                    {
                        throw new InvalidDataException($"AAC format or timing mismatch in {name}, frame {frames}.");
                    }
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    int offset = checked(frames * 1024 * channels * sizeof(float));
                    if (offset + frame.GetPlane(0).Length > expected.Length)
                    {
                        throw new InvalidDataException("AAC produced more PCM than the independent reference.");
                    }
                    for (int index = 0; index < samples.Length; index++)
                    {
                        float reference = BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(offset + index * sizeof(float), sizeof(float)));
                        if (!float.IsFinite(samples[index]) || !float.IsFinite(reference))
                        {
                            throw new InvalidDataException("AAC produced nonfinite PCM.");
                        }
                        maximum = Math.Max(maximum, Math.Abs(samples[index] - reference));
                    }
                    lastBytes = frame.GetPlane(0).ToArray();
                    actual.AddRange(lastBytes);
                    retained?.Dispose();
                    retained = frame.Retain();
                    frames++;
                }
                if (frames != packets.GetArrayLength() || frames * 1024 * channels * sizeof(float) != expected.Length
                    || await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("AAC changed its sample count or end of stream.");
                }
                await session.DisposeAsync();
                if (retained is null || lastBytes is null || session.AllocatedBufferBytes != lastBytes.Length
                    || !retained.GetPlane(0).Span.SequenceEqual(lastBytes) || file.Position != 3)
                {
                    throw new InvalidOperationException("AAC frame ownership or borrowed source position changed after disposal.");
                }
                if (noise)
                {
                    VerifyNoiseEnergy(actual.ToArray(), expected, channels);
                }
                else if (maximum > 2e-6)
                {
                    throw new InvalidDataException($"AAC PCM differs from the independent reference by {maximum:G9} in {name}.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("AAC retained buffer storage after disposal of the last PCM frame.");
            }
            return maximum;
        }

        private static string[] ChannelNames(int channels) => channels switch
        {
            1 => ["front-center"],
            2 => ["front-left", "front-right"],
            _ => throw new InvalidDataException("The AAC fixture channel count is unsupported.")
        };

        private static void VerifyHash(ReadOnlySpan<byte> bytes, string? expected)
        {
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expected)
            {
                throw new InvalidDataException("The AAC fixture hash changed.");
            }
        }

        private static void VerifyNoiseEnergy(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, int channels)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(actual);
            double totalActual = 0;
            double totalExpected = 0;
            int blockSamples = 1024 * channels;
            for (int block = 0; block < samples.Length; block += blockSamples)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    double actualEnergy = 0;
                    double expectedEnergy = 0;
                    for (int index = block + channel; index < block + blockSamples; index += channels)
                    {
                        float reference = BinaryPrimitives.ReadSingleLittleEndian(expected.Slice(index * sizeof(float), sizeof(float)));
                        actualEnergy += samples[index] * (double)samples[index];
                        expectedEnergy += reference * (double)reference;
                    }
                    if (Math.Abs(actualEnergy - expectedEnergy) > 0.06 * Math.Max(actualEnergy, expectedEnergy))
                    {
                        throw new InvalidDataException("AAC noise substitution changed a channel block's reference energy by more than 6 percent.");
                    }
                    totalActual += actualEnergy;
                    totalExpected += expectedEnergy;
                }
            }
            if (Math.Abs(totalActual - totalExpected) > 0.001 * totalExpected)
            {
                throw new InvalidDataException("AAC noise substitution changed total reference energy by more than 0.1 percent.");
            }
        }
    }
}
