// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264QuantizedSkipTestInput
    {
        private static readonly VideoFormat BlockFormat = new(16, 16, VideoPixelFormat.Yuv420P8,
            new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));

        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1_000_000);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 3,
                FrameMbsOnly = true,
                Width = 48,
                Height = 48
            };
            using (H264Picture reference = new(sequence, allocator, H264SliceKind.P))
            using (H264CabacMacroblockState state = new(sequence, allocator))
            {
                Random random = new(38271);
                for (int plane = 0; plane < 3; plane++) { random.NextBytes(reference.Plane(plane)); }
                state.Types.Fill(H264CabacMacroblockState.Skipped);
                state.Patterns.Fill(35); state.ChromaModes.Fill(2); state.DcFlags.Fill(5);
                using H264MotionReference interpolation = new(reference, 16, allocator);
                interpolation.Prepare(reference, CancellationToken.None);
                byte[][] original = [reference.ReadPlane(0).ToArray(), reference.ReadPlane(1).ToArray(), reference.ReadPlane(2).ToArray()];
                byte[] prediction = new byte[384];
                foreach ((int x, int y) in new[] { (16, 0), (32, 0), (0, 32), (32, 32), (16, 16) })
                    foreach (int integer in new[] { -64, 0, 57 })
                        for (int phase = 0; phase < 64; phase++)
                        {
                            int motionX = integer + (phase & 7), motionY = integer + (phase >> 3);
                            Predict(reference, x, y, motionX, motionY, prediction);
                            foreach (int qp in new[] { 0, 12, 26, 38, 51 })
                                for (int pattern = 0; pattern < 6; pattern++)
                                {
                                    byte[] samples = prediction.ToArray();
                                    Perturb(samples, pattern, phase & 15);
                                    VerifyCase(reference, state, allocator, interpolation, original,
                                        samples, prediction, x, y, motionX, motionY, qp);
                                }
                        }
                VerifyGates(reference, state, allocator, interpolation);
                VerifyChroma(reference, state, allocator);
                VerifyAllocation(reference, state, allocator);
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Quantized skip verification retained media storage."); }
        }

        private static void VerifyCase(H264Picture reference, H264CabacMacroblockState state, MediaBufferAllocator allocator,
            H264MotionReference interpolation, byte[][] original, byte[] samples, byte[] prediction,
            int x, int y, int motionX, int motionY, int qp)
        {
            using VideoFrame frame = CreateFrame(samples, allocator);
            Span<byte> sourceStorage = stackalloc byte[384];
            H264SourceMacroblock source = new(frame, 0, 0, sourceStorage);
            bool expected = HasZeroResidual(samples, prediction, qp);
            Span<byte> actual = stackalloc byte[384];
            int address = y / 16 * 3 + x / 16;
            for (int prepared = 0; prepared < 2; prepared++)
            {
                bool selected = H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, address, x, y,
                    motionX, motionY, qp, 16, actual, prepared == 0 ? null : interpolation);
                if (selected != expected || (selected && !actual.SequenceEqual(prediction)))
                { throw new InvalidDataException($"Quantized skip differs at {x},{y}, MV {motionX},{motionY}, QP {qp}."); }
            }
            if (state.Types.ContainsAnyExcept(H264CabacMacroblockState.Skipped) || state.Patterns.ContainsAnyExcept((byte)35)
                || state.ChromaModes.ContainsAnyExcept((byte)2) || state.DcFlags.ContainsAnyExcept((byte)5))
            { throw new InvalidDataException("Quantized skip changed committed syntax state."); }
            for (int plane = 0; plane < 3; plane++)
                if (!reference.ReadPlane(plane).SequenceEqual(original[plane]))
                { throw new InvalidDataException("Quantized skip changed reference samples."); }
        }

        private static bool HasZeroResidual(ReadOnlySpan<byte> samples, ReadOnlySpan<byte> prediction, int qp)
        {
            Span<int> transformed = stackalloc int[16];
            Span<int> levels = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int x, int y) = H264Picture.LumaBlockPosition(index);
                int offset = y * 16 + x;
                H264EncodingBlocks.Transform(samples[offset..], 16, prediction[offset..], 16, transformed);
                H264ForwardTransform.Quantize4x4(transformed, qp, levels);
                if (levels.ContainsAnyExcept(0)) { return false; }
            }
            Span<int> chroma = stackalloc int[H264ChromaEncoder.CoefficientCount];
            H264ChromaEncoder.Analyze(samples[256..], qp, prediction[256..], chroma);
            return !chroma.ContainsAnyExcept(0);
        }

        private static void VerifyGates(H264Picture reference, H264CabacMacroblockState state,
            MediaBufferAllocator allocator, H264MotionReference interpolation)
        {
            byte[] samples = new byte[384];
            Predict(reference, 16, 16, 0, 0, samples);
            using VideoFrame frame = CreateFrame(samples, allocator);
            Span<byte> storage = stackalloc byte[384];
            H264SourceMacroblock source = new(frame, 0, 0, storage);
            Span<byte> prediction = stackalloc byte[384];
            foreach (int neighbor in new[] { -1, 0, 1, 2, 3, 4, 5 })
            {
                state.Types.Fill(H264CabacMacroblockState.Inter);
                if (neighbor >= 0) { state.Types[neighbor] = H264CabacMacroblockState.Skipped; }
                bool selected = H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 4, 16, 16, 0, 0, 26, 16, prediction, interpolation);
                if (selected != (neighbor is 1 or 3)) { throw new InvalidDataException("Skip probe used a non-left/top or unavailable neighbor."); }
            }
            state.Types.Fill(H264CabacMacroblockState.Skipped);
            if (!H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 4, 16, 16, 0, 0, 26, 0, prediction, interpolation)
                || H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 4, 16, 16, 1, 0, 26, 0, prediction, interpolation)
                || H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 0, 0, 0, 0, 0, 26, 16, prediction, interpolation))
            { throw new InvalidDataException("Skip probe violated zero-range or first-macroblock boundaries."); }
            foreach ((int x, int y) in new[] { (-65, 0), (65, 0), (0, -65), (0, 65), (int.MinValue, int.MaxValue) })
            {
                prediction.Fill(173);
                if (H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 4, 16, 16, x, y, 26, 16, prediction, interpolation)
                    || prediction.ContainsAnyExcept((byte)173))
                { throw new InvalidDataException("Skip probe accepted motion outside the configured range."); }
            }
            using H264CabacMacroblockState slice = new(reference.Sequence, allocator, firstMacroblock: 4);
            slice.Types.Fill(H264CabacMacroblockState.Skipped);
            if (H264QuantizedSkipAnalysis.TryAnalyze(slice, source, reference, 4, 16, 16, 0, 0, 26, 16, prediction, interpolation))
            { throw new InvalidDataException("Skip probe used a neighbor before the slice."); }
        }

        private static void VerifyChroma(H264Picture reference, H264CabacMacroblockState state, MediaBufferAllocator allocator)
        {
            for (int plane = 0; plane < 3; plane++) { reference.Plane(plane).Fill(128); }
            byte[] prediction = new byte[384]; prediction.AsSpan().Fill(128);
            Span<int> levels = stackalloc int[H264ChromaEncoder.CoefficientCount];
            Span<byte> storage = stackalloc byte[384];
            Span<byte> actual = stackalloc byte[384];
            foreach (int qp in new[] { 0, 26, 51 })
                for (int plane = 1; plane <= 2; plane++)
                    for (int pattern = 0; pattern < 2; pattern++)
                    {
                        byte[] samples = prediction.ToArray();
                        for (int index = 0; index < 64; index++)
                        { samples[256 + (plane - 1) * 64 + index] = (byte)(pattern == 0 ? 192 : (index % 2 == 0 ? 192 : 64)); }
                        H264ChromaEncoder.Analyze(samples.AsSpan(256), qp, prediction.AsSpan(256), levels);
                        bool ac = levels[..120].ContainsAnyExcept(0), dc = levels[120..].ContainsAnyExcept(0);
                        if (pattern == 0 ? !dc || ac : !ac || dc) { throw new InvalidDataException("Chroma test did not isolate DC and AC residuals."); }
                        using VideoFrame frame = CreateFrame(samples, allocator);
                        H264SourceMacroblock source = new(frame, 0, 0, storage);
                        if (H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, 4, 16, 16, 0, 0, qp, 16, actual, null))
                        { throw new InvalidDataException("Skip probe discarded a chroma-only residual."); }
                    }
        }

        private static void VerifyAllocation(H264Picture reference, H264CabacMacroblockState state, MediaBufferAllocator allocator)
        {
            byte[] samples = new byte[384]; samples.AsSpan().Fill(128);
            using VideoFrame accepted = CreateFrame(samples, allocator);
            samples[0] = 0;
            using VideoFrame rejected = CreateFrame(samples, allocator);
            using H264MotionReference interpolation = new(reference, 16, allocator);
            interpolation.Prepare(reference, CancellationToken.None);
            long reserved = allocator.AllocatedBytes;
            int expected = Repeat();
            for (int warmup = 0; warmup < 8; warmup++)
                if (Repeat() != expected) { throw new InvalidDataException("Skip probe changed repeated decisions."); }
            _ = GC.GetAllocatedBytesForCurrentThread();
            if (!GC.TryStartNoGCRegion(1024 * 1024))
            { throw new InvalidOperationException("The quantized skip allocation interval could not be established."); }
            long allocated;
            long controlAllocated;
            int actual;
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                actual = Repeat();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                long controlBefore = GC.GetAllocatedBytesForCurrentThread();
                byte[] control = new byte[37];
                controlAllocated = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
                GC.KeepAlive(control);
            }
            finally { GC.EndNoGCRegion(); }
            if (actual != expected || expected != 512 || allocated != 0 || controlAllocated != (IntPtr.Size == 8 ? 64 : 52)
                || allocator.AllocatedBytes != reserved)
            { throw new InvalidDataException($"Repeated quantized skip allocated {allocated} bytes or changed reserved storage."); }

            int Repeat()
            {
                Span<byte> acceptedStorage = stackalloc byte[384], rejectedStorage = stackalloc byte[384], prediction = stackalloc byte[384];
                H264SourceMacroblock positive = new(accepted, 0, 0, acceptedStorage), negative = new(rejected, 0, 0, rejectedStorage);
                int count = 0;
                for (int iteration = 0; iteration < 512; iteration++)
                {
                    H264MotionReference? prepared = iteration % 2 == 0 ? null : interpolation;
                    if (H264QuantizedSkipAnalysis.TryAnalyze(state, positive, reference, 4, 16, 16, iteration & 3, (iteration >> 2) & 3, 0, 16, prediction, prepared)) { count++; }
                    if (H264QuantizedSkipAnalysis.TryAnalyze(state, negative, reference, 4, 16, 16, 0, 0, 0, 16, prediction, prepared)) { count++; }
                }
                return count;
            }
        }

        private static void Perturb(Span<byte> samples, int pattern, int block)
        {
            switch (pattern)
            {
                case 0: break;
                case 1: samples[0] ^= 1; break;
                case 2: for (int index = 0; index < 256; index++) { samples[index] = (byte)Math.Clamp(samples[index] + 16, 0, 255); } break;
                case 3: for (int index = 256; index < 384; index++) { samples[index] = (byte)Math.Clamp(samples[index] + 8, 0, 255); } break;
                case 4: for (int index = 256; index < 384; index++) { samples[index] = (byte)Math.Clamp(samples[index] + (index % 2 == 0 ? 16 : -16), 0, 255); } break;
                case 5:
                    (int x, int y) = H264Picture.LumaBlockPosition(block);
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                        { samples[(y + row) * 16 + x + column] ^= 128; }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        private static VideoFrame CreateFrame(ReadOnlySpan<byte> samples, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(384);
            samples.CopyTo(writable.Memory.Span);
            using MediaBuffer data = writable.Publish();
            return new(BlockFormat, data, BlockFormat.CreateTightLayout());
        }

        private static void Predict(H264Picture reference, int x, int y, int motionX, int motionY, Span<byte> prediction)
        {
            H264PredictionBlock.Read(reference, 0, x, y, 16, 16, motionX, motionY, prediction[..256]);
            H264PredictionBlock.Read(reference, 1, x, y, 16, 16, motionX, motionY, prediction.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, x, y, 16, 16, motionX, motionY, prediction.Slice(320, 64));
        }
    }
}
