// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacPsDecodeTests
    {
        [Theory]
        [InlineData("tonal", false)]
        [InlineData("tonal", true)]
        [InlineData("transients", false)]
        [InlineData("transients", true)]
        [InlineData("parameters", false)]
        [InlineData("parameters", true)]
        public async Task CompleteSequenceMatchesIndependentPcmWithExplicitAndImplicitPs(string name, bool explicitPs)
        {
            await AacPsDecodeTestInput.VerifySequenceAsync(Root, name, explicitPs);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SingleRateStereoMatchesIndependentCompletePcmWithSyncAndForwardSignaling(bool explicitPs)
        {
            await AacPsDecodeTestInput.VerifySequenceAsync(Root, "transients", explicitPs, singleRate: true);
        }

        [Fact]
        public async Task ResetClearsStereoStateAndRetainedOutputSurvivesDisposal()
        {
            byte[][] packets = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            MediaBufferAllocator allocator = new(256 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(AacPsDecodeTestInput.Stream(true), allocator))
            {
                using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[0], 0);
                decoder.TrySend(packet);
                using AudioFrame retained = decoder.Receive().Value;
                byte[] expected = retained.GetPlane(0).ToArray();
                for (int index = 1; index < packets.Length; index++)
                {
                    using MediaPacket input = AacSbrDecodeTestInput.Packet(allocator, packets[index], index);
                    decoder.TrySend(input);
                    using AudioFrame frame = decoder.Receive().Value;
                }
                decoder.Reset();
                decoder.TrySend(packet);
                using AudioFrame repeated = decoder.Receive().Value;
                Assert.Equal(expected, repeated.GetPlane(0).ToArray());
                decoder.Dispose();
                Assert.Equal(expected, retained.GetPlane(0).ToArray());
                Assert.Equal(packet.Data.Length + 2 * expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExplicitPsInspectorRequiresIndependentStereoDataAndPrerollReconstructsFreshReference()
        {
            byte[][] packets = await AacPsDecodeTestInput.ReadPacketsAsync(Root, "transients");
            string monoRoot = Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
            byte[][] mono = await AacSbrDecodeTestInput.ReadPacketsAsync(monoRoot, 1);
            MediaBufferAllocator allocator = new(256 * 1024);
            AacDecoderFactory factory = new();
            IRandomAccessRecoveryInspector inspector = Assert.IsAssignableFrom<IRandomAccessRecoveryInspector>(
                factory.CreateRandomAccessInspector(AacPsDecodeTestInput.Stream(true)));
            using (MediaPacket monoPacket = AacSbrDecodeTestInput.Packet(allocator, mono[0], 0))
            {
                Assert.Null(inspector.InspectRecovery(monoPacket));
            }
            using (MediaPacket anchor = AacSbrDecodeTestInput.Packet(allocator, packets[10], 10))
            {
                Assert.Equal(2, inspector.InspectRecovery(anchor)?.RequiredPrerollPackets);
            }
            byte[] expected = await AacPsDecodeTestInput.ReadReferenceAsync(Root, "transients", restart: true);
            Assert.Equal(5 * 2048 * 2 * sizeof(float), expected.Length);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(AacPsDecodeTestInput.Stream(true), allocator))
            {
                IPacketPrerollDecoder preroll = Assert.IsAssignableFrom<IPacketPrerollDecoder>(decoder);
                byte[]? repeated = null;
                foreach (int requested in new[] { 14, 12, 13, 14 })
                {
                    preroll.ResetForPreroll(2, false);
                    for (int index = 10; index <= requested; index++)
                    {
                        using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[index], index);
                        decoder.TrySend(packet);
                        if (index < 12)
                        {
                            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                            continue;
                        }
                        using AudioFrame frame = decoder.Receive().Value;
                        ReadOnlySpan<float> actual = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                        double error = 0;
                        double energy = 0;
                        double maximum = 0;
                        for (int sample = 0; sample < actual.Length; sample++)
                        {
                            double value = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                                expected.AsSpan(((index - 10) * actual.Length + sample) * sizeof(float)));
                            double difference = actual[sample] - value;
                            error += difference * difference;
                            energy += value * value;
                            maximum = Math.Max(maximum, Math.Abs(difference));
                        }
                        Assert.True(10 * Math.Log10(energy / error) >= 95);
                        Assert.True(maximum <= 0.0001);
                        Assert.Equal(packet.PresentationTime, frame.Timestamp);
                        if (index == 14)
                        {
                            byte[] current = frame.GetPlane(0).ToArray();
                            if (repeated is not null)
                            {
                                Assert.Equal(repeated, current);
                            }
                            repeated = current;
                        }
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacPs");
    }
}
