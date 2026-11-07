// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Hevc;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class HevcPictureStorageTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            SequenceParameters sequence = await ReadSequenceAsync(root, name);
            int sampleBytes = (sequence.BitDepth + 7) / 8;
            int lumaSamples = checked(sequence.Width * sequence.Height);
            int chromaSamples = checked((sequence.Width / sequence.SubWidthC) * (sequence.Height / sequence.SubHeightC));
            int expectedBytes = checked((lumaSamples + 2 * chromaSamples) * sampleBytes);
            MediaBufferAllocator allocator = new(expectedBytes);
            using DecodedPicture picture = new(sequence, allocator);
            SamplePlane[] planes = [picture.Luma, picture.Cb!, picture.Cr!];
            int maximum = (1 << sequence.BitDepth) - 1;
            for (int component = 0; component < planes.Length; component++)
            {
                SamplePlane plane = planes[component];
                for (int i = 0; i < plane.Width * plane.Height; i++)
                {
                    int value = (i * 37 + component * 11) & maximum;
                    if (sampleBytes == 1) { plane.ByteView.Samples[i] = (byte)value; }
                    else { plane.WordView.Samples[i] = (ushort)value; }
                }
            }
            using MediaBuffer published = picture.PublishPixels();
            using (MediaBuffer again = picture.PublishPixels())
            {
                if (!published.Memory.Equals(again.Memory)) { throw new InvalidDataException("HEVC repeated publication changed pixel storage."); }
            }
            if (allocator.AllocatedBytes != expectedBytes) { throw new InvalidDataException("HEVC publication duplicated its picture storage."); }
            int offset = 0;
            for (int component = 0; component < planes.Length; component++)
            {
                SamplePlane plane = planes[component];
                int samples = plane.Width * plane.Height;
                ReadOnlySpan<byte> bytes = published.Memory.Span.Slice(offset, samples * sampleBytes);
                for (int i = 0; i < samples; i++)
                {
                    int expected = (i * 37 + component * 11) & maximum;
                    int actual = sampleBytes == 1 ? bytes[i] : MemoryMarshal.Cast<byte, ushort>(bytes)[i];
                    int reference = sampleBytes == 1 ? plane.ReadByteView.Samples[i] : plane.ReadWordView.Samples[i];
                    if (actual != expected || reference != expected) { throw new InvalidDataException("HEVC published pixels and borrowed reference samples differ."); }
                }
                offset += samples * sampleBytes;
            }
            using MediaBuffer retained = published.Retain();
            picture.Dispose(); published.Dispose();
            if (allocator.AllocatedBytes != expectedBytes || retained.Memory.Span[0] != 0)
            {
                throw new InvalidDataException("HEVC picture disposal invalidated a retained output lease.");
            }
            retained.Dispose();
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("HEVC retained picture storage was not released."); }
        }

        public static async Task<SequenceParameters> ReadSequenceAsync(string root, string name)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            SequenceParameters result;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4"))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                HevcDecoderConfiguration configuration = HevcDecoderConfiguration.Read(reader.Streams[0].Configuration);
                ReadOnlyMemory<byte> bytes = configuration.Arrays.Single(array => array.Type == 33).Units.Single();
                result = SequenceParameters.Read(new(bytes), int.MaxValue);
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("HEVC parameter parsing retained container storage."); }
            return result;
        }
    }
}
