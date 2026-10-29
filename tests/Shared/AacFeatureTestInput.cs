using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AacFeatureTestInput
    {
        public static async Task<(int Sequences, int Shapes, int Books, int Filters, int MidSideBands)> VerifyAsync(string root, string name)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            int sequences = 0;
            int shapes = 0;
            int books = 0;
            int filters = 0;
            int midSideBands = 0;
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".m4a")));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                AacConfiguration configuration = AacConfiguration.ReadAudioSpecificConfig(reader.Streams[0].Configuration.Span, out _)
                    ?? throw new InvalidDataException("The AAC fixture configuration is unsupported.");
                using AacDecoderWorkspace workspace = new(allocator, configuration.ChannelCount);
                uint noiseState = 0x13579bdf;
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null)
                    {
                        break;
                    }
                    midSideBands += CountMidSideBands(packet.Data.Span, configuration.FrequencyIndex);
                    AacAccessUnitParser.Read(packet.Data.Span, configuration.FrequencyIndex, workspace, ref noiseState, default);
                    foreach (AacChannelState channel in workspace.Right is null ? new[] { workspace.Left } : new[] { workspace.Left, workspace.Right })
                    {
                        sequences |= 1 << channel.Info.Sequence;
                        shapes |= 1 << channel.Info.Shape;
                        filters += channel.TnsFilters.Count;
                        for (int group = 0; group < channel.Info.Groups.Length; group++)
                        {
                            for (int band = 0; band < channel.Info.MaxBands; band++)
                            {
                                books |= 1 << channel.Books[group * 64 + band];
                            }
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("AAC feature inspection retained media storage.");
            }
            (int, int, int, int, int) expected = name switch
            {
                "lc-mono-48" => (0xf, 3, 0xff6, 2, 0),
                "lc-stereo-44" => (0xf, 3, 0xcffe, 2, 374),
                "lc-pns-48" => (0xf, 3, 0xadfe, 5, 292),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            if ((sequences, shapes, books, filters, midSideBands) != expected)
            {
                throw new InvalidDataException("The AAC fixture no longer exercises its recorded window, spectral and stereo tools.");
            }
            return (sequences, shapes, books, filters, midSideBands);
        }

        private static int CountMidSideBands(ReadOnlySpan<byte> data, int frequencyIndex)
        {
            AacBitReader bits = new(data);
            if (bits.Read(3) != 1)
            {
                return 0;
            }
            bits.Read(4);
            if (bits.Read(1) == 0)
            {
                return 0;
            }
            AacWindowInfo info = AacWindowInfo.Read(ref bits, frequencyIndex);
            int mode = bits.Read(2);
            switch (mode)
            {
                case 0:
                    return 0;
                case 1:
                    int count = 0;
                    for (int index = 0; index < info.MaxBands * info.Groups.Length; index++)
                    {
                        count += bits.Read(1);
                    }
                    return count;
                case 2:
                    return info.MaxBands * info.Groups.Length;
                default:
                    throw new InvalidDataException("The AAC mask mode is reserved.");
            }
        }
    }
}
