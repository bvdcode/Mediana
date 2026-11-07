// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using Mediana.Codecs.Hevc;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class HevcPictureTestInput
    {
        public static async Task<int> VerifyAsync(string root, string name)
        {
            HevcSliceHeader[] slices = await HevcSliceHeaderTestInput.ReadHeadersAsync(root, name);
            using MemoryStream bytes = new();
            await using (GZipStream reference = new(File.OpenRead(Path.Combine(root, name + ".yuv.gz")), CompressionMode.Decompress))
            { await reference.CopyToAsync(bytes); }
            ReadOnlyMemory<byte> expected = bytes.ToArray();
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            HevcPictureOrderCounter counter = new(slices[0].Picture.Sequence.ReferencePictures.Log2PictureOrderCount);
            using (HevcReferenceBuffer references = new())
            using (HevcPictureDecoder decoder = new(allocator))
            {
                int frameSize = 0;
                for (int i = 0; i < slices.Length; i++)
                {
                    HevcSliceHeader slice = slices[i];
                    int order = counter.Derive(slice.NalType, slice.TemporalId, slice.PictureOrderCount, i == 0);
                    HevcReferenceSet selected = references.Select(slice.References, order, slice.Picture.Sequence.ReferencePictures.Log2PictureOrderCount);
                    HevcReferencePicture picture = decoder.Decode([slice], selected, order, CancellationToken.None);
                    bool stored = false;
                    try
                    {
                        frameSize = VerifyPicture(picture.Pixels, expected.Span, order, name);
                        if (slice.NalType is >= 16 and <= 21 || (slice.NalType & 1) != 0)
                        { references.Store(picture, slice.Picture.Sequence.MaximumDecodedPictures); stored = true; }
                    }
                    finally { if (!stored) { picture.Dispose(); } }
                }
                if (expected.Length != checked(frameSize * slices.Length)) { throw new InvalidDataException("HEVC complete sequence length differs."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("HEVC sequence retained decoded picture or working storage."); }
            return slices.Length;
        }

        private static int VerifyPicture(DecodedPicture picture, ReadOnlySpan<byte> reference, int order, string name)
        {
            SequenceParameters sequence = picture.Sequence;
            int sampleBytes = (sequence.BitDepth + 7) / 8;
            int lumaSize = sequence.VisibleWidth * sequence.VisibleHeight * sampleBytes;
            int chromaSize = sequence.VisibleWidth / sequence.SubWidthC * (sequence.VisibleHeight / sequence.SubHeightC) * sampleBytes;
            int frameSize = lumaSize + 2 * chromaSize, offset = checked(order * frameSize);
            for (int component = 0; component < 3; component++)
            {
                SamplePlane plane = component switch
                {
                    0 => picture.Luma,
                    1 => picture.Cb ?? throw new InvalidDataException("Missing reference Cb."),
                    2 => picture.Cr ?? throw new InvalidDataException("Missing reference Cr."),
                    _ => throw new ArgumentOutOfRangeException(nameof(component))
                };
                int scaleX = component == 0 ? 1 : sequence.SubWidthC, scaleY = component == 0 ? 1 : sequence.SubHeightC;
                int width = sequence.VisibleWidth / scaleX, height = sequence.VisibleHeight / scaleY;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sourceX = x + sequence.CropLeft / scaleX, sourceY = y + sequence.CropTop / scaleY;
                        int actual = sequence.BitDepth switch
                        {
                            8 => plane.ReadByteView.Row(sourceY)[sourceX],
                            10 => plane.ReadWordView.Row(sourceY)[sourceX],
                            _ => throw new InvalidDataException("Unsupported reference precision.")
                        };
                        int wanted = sampleBytes == 1 ? reference[offset] : BinaryPrimitives.ReadUInt16LittleEndian(reference[offset..]);
                        if (actual != wanted)
                        { throw new InvalidDataException($"HEVC {name}, POC {order}, component {component} at {x},{y}: {actual}, expected {wanted}."); }
                        offset += sampleBytes;
                    }
                }
            }
            return frameSize;
        }
    }
}
