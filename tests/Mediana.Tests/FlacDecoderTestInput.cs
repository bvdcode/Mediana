using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Flac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal static class FlacDecoderTestInput
    {
        public static byte[] Constant(int samples = 16, long number = 0, bool variable = false)
        {
            FlacFrameTestWriter writer = new(samples, number: number, variable: variable);
            writer.Write(0, 8);
            writer.Write(0, 16);
            return writer.Finish();
        }

        public static int[] Decode(byte[] data, int bits, int channels, long total, int packetBytes = 7, bool variable = false)
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            List<int> result = [];
            try
            {
                using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator, bits, channels, total, variable: variable);
                int offset = 0;
                while (true)
                {
                    CodecOutput<AudioFrame> output = decoder.Receive();
                    switch (output.Status)
                    {
                        case CodecOutputStatus.Available:
                            using (AudioFrame frame = output.Value)
                            {
                                Assert.Equal(new MediaTimestamp(result.Count / channels, new TimeBase(1, 48000)), frame.Timestamp);
                                string[] labels = channels switch
                                {
                                    1 => ["front-center"],
                                    2 => ["front-left", "front-right"],
                                    3 => ["front-left", "front-right", "front-center"],
                                    4 => ["front-left", "front-right", "back-left", "back-right"],
                                    5 => ["front-left", "front-right", "front-center", "back-left", "back-right"],
                                    6 => ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right"],
                                    7 => ["front-left", "front-right", "front-center", "low-frequency", "back-center", "side-left", "side-right"],
                                    8 => ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right", "side-left", "side-right"],
                                    _ => throw new InvalidOperationException()
                                };
                                Assert.Equal(labels, frame.Format.Channels);
                                result.AddRange(MemoryMarshal.Cast<byte, int>(frame.GetPlane(0).Span).ToArray());
                            }
                            break;
                        case CodecOutputStatus.InputRequired:
                            if (offset == data.Length)
                            {
                                decoder.CompleteInput();
                                break;
                            }
                            int length = Math.Min(packetBytes, data.Length - offset);
                            Send(decoder, allocator, data.AsSpan(offset, length));
                            offset += length;
                            break;
                        case CodecOutputStatus.EndOfStream:
                            return result.ToArray();
                        default:
                            throw new InvalidOperationException();
                    }
                }
            }
            finally
            {
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        public static IMediaCodec<MediaPacket, AudioFrame> Create(MediaBufferAllocator allocator, int bits, int channels,
            long total, bool variable = false, int maxFrameBytes = 4096)
        {
            byte[] info = FlacTestInput.StreamInfo(minimum: 16, maximum: variable ? 128 : 64, bits: bits, channels: channels, samples: total);
            if (!variable)
            {
                info[1] = 64;
            }
            EncodedStreamInfo stream = new(0, MediaKind.Audio, FlacConfiguration.CodecId, new TimeBase(1, 48000), info);
            return new FlacDecoderFactory(maxFrameBytes).Create(stream, allocator);
        }

        public static void Send(IMediaCodec<MediaPacket, AudioFrame> decoder, MediaBufferAllocator allocator, ReadOnlySpan<byte> data)
        {
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            using MediaPacket packet = new(0, buffer);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
        }
    }
}
