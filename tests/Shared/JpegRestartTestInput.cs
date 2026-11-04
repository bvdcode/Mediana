// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class JpegRestartTestInput
    {
        internal const string Warning = "Ignored a redundant final JPEG restart marker immediately before EOI.";

        internal static byte[] Create(int mcus = 9, int interval = 1, bool redundant = true, bool progressive = false)
        {
            byte[] encoded = JpegTestData.Insert(JpegTestData.Image(), 221, [(byte)(interval >> 8), (byte)interval]);
            int frame = JpegTestData.Find(encoded, 192);
            encoded[frame + 1] = progressive ? (byte)194 : (byte)192;
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(frame + 5), 8);
            BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(frame + 7), checked((ushort)(mcus * 8)));
            int scan = JpegTestData.Find(encoded, 218);
            encoded[scan + 8] = progressive ? (byte)0 : (byte)63;
            List<byte> entropy = [];
            int restart = 0;
            for (int first = 0; first < mcus; first += interval)
            {
                int count = Math.Min(interval, mcus - first);
                int bitCount = count * (progressive ? 1 : 2);
                for (int bit = 0; bit < bitCount; bit += 8)
                {
                    int padding = Math.Max(0, 8 - (bitCount - bit));
                    entropy.Add((byte)((1 << padding) - 1));
                }
                if (first + count < mcus || redundant)
                {
                    entropy.Add(255);
                    entropy.Add((byte)(208 + restart));
                    restart = (restart + 1) & 7;
                }
            }
            return [.. encoded.AsSpan(0, scan + 10), .. entropy, 255, 217];
        }

        internal static async Task VerifyRecoveryAsync()
        {
            foreach ((int mcus, int interval) in new[] { (1, 1), (3, 3), (7, 1), (8, 1), (9, 1), (18, 2), (20, 5) })
            {
                foreach (bool progressive in new[] { false, true })
                {
                    byte[] encoded = Create(mcus, interval, progressive: progressive);
                    foreach (int reduction in new[] { 1, 2, 4, 8 })
                    {
                        MediaBufferAllocator allocator = new(1024 * 1024);
                        using (JpegImage recovered = JpegDecoder.DecodeReducedComponents(encoded, allocator, reduction))
                        using (JpegImage standard = JpegDecoder.DecodeReducedComponents(Create(mcus, interval, redundant: false, progressive), allocator, reduction))
                        {
                            Require(recovered.Warnings.Count == 1 && recovered.Warnings[0] == Warning && standard.Warnings.Count == 0);
                            byte[] actual = new byte[recovered.Width * recovered.Height * 4];
                            byte[] expected = new byte[actual.Length];
                            recovered.WriteRgbaTo(actual, JpegColorSpace.Rgb);
                            standard.WriteRgbaTo(expected, JpegColorSpace.Rgb);
                            Require(actual.AsSpan().SequenceEqual(expected));
                            VerifyGray(actual);
                        }
                        Require(allocator.AllocatedBytes == 0);
                    }
                    List<string> warnings = [];
                    MediaBufferAllocator output = new(1024 * 1024);
                    using (VideoFrame frame = await new JpegImageDecoder().DecodeAsync(encoded, new() { WarningHandler = warnings.Add }, output))
                    {
                        encoded.AsSpan().Clear();
                        VerifyGray(frame.Data.Span);
                        Require(frame.Format.Width == mcus * 8 && frame.Format.Height == 8
                            && warnings.Count == 1 && warnings[0] == Warning && output.AllocatedBytes == frame.Data.Length);
                    }
                    Require(output.AllocatedBytes == 0);
                }
            }
        }

        internal static async Task VerifyRejectedAsync()
        {
            byte[] valid = Create();
            List<byte[]> invalid =
            [
                [.. valid, 0], [.. valid.AsSpan(0, valid.Length - 1)], [.. valid.AsSpan(0, valid.Length - 2)],
                [.. valid.AsSpan(0, valid.Length - 2), 255, 209, 255, 217],
                [.. valid.AsSpan(0, valid.Length - 2), .. JpegTestData.Segment(254, []), 255, 217],
                [.. valid.AsSpan(0, valid.Length - 2), .. JpegTestData.Segment(218, [1, 1, 0, 0, 63, 0]), 63, 255, 217],
                Create(10, 3)
            ];
            for (int marker = 209; marker <= 215; marker++)
            {
                byte[] wrong = valid.ToArray();
                wrong[^3] = (byte)marker;
                invalid.Add(wrong);
            }
            int dri = JpegTestData.Find(valid, 221);
            invalid.Add([.. valid.AsSpan(0, dri), .. valid.AsSpan(dri + 6)]);
            byte[] disabled = Create(1);
            disabled[JpegTestData.Find(disabled, 221) + 5] = 0;
            invalid.Add(disabled);
            byte[] padding = valid.ToArray();
            padding[^5] = 0;
            invalid.Add(padding);
            byte[] interior = valid.ToArray();
            interior[JpegTestData.Find(interior, 208) + 1] = 209;
            invalid.Add(interior);
            byte[] premature = valid.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(premature.AsSpan(JpegTestData.Find(premature, 192) + 7), 80);
            invalid.Add(premature);
            foreach (byte[] encoded in invalid)
            {
                List<string> warnings = [];
                MediaBufferAllocator allocator = new(1024 * 1024);
                bool rejected = false;
                try
                {
                    using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(encoded, new() { WarningHandler = warnings.Add }, allocator);
                }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && warnings.Count == 0 && allocator.AllocatedBytes == 0);
            }
            await JpegDecodeTestInput.RejectAsync<InvalidDataException>(Create(), new() { MaximumMarkers = 15 });
            MediaBufferAllocator exact = new(320);
            using (VideoFrame frame = await new JpegImageDecoder(new() { MaximumMarkers = 8 }).DecodeAsync(Create(1), new(), exact))
            {
                Require(exact.AllocatedBytes == 8 * 8 * 4);
            }
            Require(exact.AllocatedBytes == 0);
            MediaBufferAllocator shortBudget = new(319);
            bool limited = false;
            try
            {
                using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(Create(1), new(), shortBudget);
            }
            catch (MediaMemoryLimitException) { limited = true; }
            Require(limited && shortBudget.AllocatedBytes == 0);
            MediaBufferAllocator diagnostics = new(1024 * 1024);
            bool stopped = false;
            try
            {
                using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(Create(),
                    new() { WarningHandler = _ => throw new InvalidOperationException("Diagnostic consumer stopped.") }, diagnostics);
            }
            catch (InvalidOperationException error) when (error.Message == "Diagnostic consumer stopped.") { stopped = true; }
            Require(stopped && diagnostics.AllocatedBytes == 0);
        }

        private static void VerifyGray(ReadOnlySpan<byte> pixels)
        {
            ReadOnlySpan<byte> gray = [128, 128, 128, 255];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                Require(pixels.Slice(i, 4).SequenceEqual(gray));
            }
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("JPEG final restart recovery changed pixels, diagnostics or buffer ownership."); }
        }
    }
}
