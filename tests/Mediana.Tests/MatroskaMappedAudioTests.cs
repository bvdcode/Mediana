// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Vorbis;
using Mediana.Containers.Matroska;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class MatroskaMappedAudioTests
    {
        [Theory]
        [InlineData("flac-offset.mka", "flac", 137, 11157)]
        [InlineData("vorbis-mono.webm", "vorbis", 3, 45632)]
        public async Task CodecFramesDecodeOnTheMatroskaTimeline(string name, string codec, int startMilliseconds, int expectedSamples)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new FlacDecoderFactory()).AddAudioDecoder(new VorbisDecoderFactory()).Build();
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", name);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaInfo info = await engine.ProbeAsync(source);
            Assert.Equal(codec, Assert.Single(info.Streams).CodecId);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(44100, metadata.SampleRate);
            Assert.Equal(1, metadata.ChannelCount);

            await using AudioSession session = await engine.OpenAudioAsync(source, 0);
            using IncrementalHash pcm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] encodedSample = new byte[sizeof(int)];
            int samples = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(startMilliseconds * 441L + samples * 10L, new(1, 441000)), frame.Timestamp);
                    samples += frame.SamplesPerChannel;
                    if (codec == "flac")
                    {
                        foreach (int sample in MemoryMarshal.Cast<byte, int>(frame.GetPlane(0).Span))
                        {
                            BinaryPrimitives.WriteInt32LittleEndian(encodedSample, sample);
                            pcm.AppendData(encodedSample);
                        }
                    }
                }
            }
            Assert.Equal(expectedSamples, samples);
            if (codec == "flac")
            {
                Assert.Equal("CAE4CC64F147503D7CD6DD57BA167BE4B49C32F72316B4CA1361E36E7FF6D98D", Convert.ToHexString(pcm.GetHashAndReset()));
            }
        }

        [Fact]
        public async Task VorbisInWebmBuildsAContinuousWaveform()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "vorbis-mono.webm");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new VorbisDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0);
            await using AudioWaveformReader waveform = new(session.ReadFrameAsync, [1], new AudioWaveformOptions(400, 40),
                new MediaBufferAllocator(1024 * 1024));
            int samples = 0;
            double peak = 0;
            while (await waveform.ReadLevelAsync() is AudioLevel level)
            {
                samples += level.SampleCount;
                peak = Math.Max(peak, level.Peak);
            }
            Assert.Equal(414, samples);
            Assert.InRange(peak, 0.09, 0.16);
        }
    }
}
