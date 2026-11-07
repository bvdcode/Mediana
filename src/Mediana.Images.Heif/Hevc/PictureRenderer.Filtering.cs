// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Numerics;
using Mediana.Codecs.Hevc;
using Mediana.Core.Video;
using Mediana.Images.Heif.Container;
using Mediana.Processing;

namespace Mediana.Images.Heif.Hevc
{
    internal static partial class PictureRenderer
    {
        private static void RenderFilteredColor(DecodedPicture picture, ImageCrop crop, ColorConversion conversion,
            ChromaUpsamplingFilter filter, Span<byte> output, CancellationToken cancellationToken)
        {
            SequenceParameters sequence = picture.Sequence;
            ChromaLocation location = sequence.Video.ChromaLocation switch
            {
                0 => ChromaLocation.Left,
                1 => ChromaLocation.Center,
                2 => ChromaLocation.TopLeft,
                3 => ChromaLocation.Top,
                4 => ChromaLocation.BottomLeft,
                5 => ChromaLocation.Bottom,
                _ => throw new InvalidDataException("Invalid HEVC chroma sample location.")
            };
            ChromaUpsampler upsampler = new(sequence.VisibleWidth / sequence.SubWidthC, sequence.VisibleHeight / sequence.SubHeightC,
                sequence.SubWidthC, sequence.SubHeightC, location, filter);
            switch (sequence.BitDepth)
            {
                case 8:
                    RenderFiltered(picture.Luma.ByteView, picture.Cb!.ByteView, picture.Cr!.ByteView,
                        sequence, crop, upsampler, conversion.ConvertRows, output, cancellationToken);
                    break;
                case 10:
                    RenderFiltered(picture.Luma.WordView, picture.Cb!.WordView, picture.Cr!.WordView,
                        sequence, crop, upsampler, conversion.ConvertRows, output, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported component sample depth.");
            }
        }

        private static void RenderFiltered<T>(SamplePlaneView<T> luma, SamplePlaneView<T> cb, SamplePlaneView<T> cr,
            SequenceParameters sequence, ImageCrop crop, ChromaUpsampler upsampler, ColorRowConverter<T> convert,
            Span<byte> output, CancellationToken cancellationToken)
            where T : unmanaged, IBinaryInteger<T>, IUnsignedNumber<T>
        {
            float[] workspace = ArrayPool<float>.Shared.Rent(upsampler.WorkspaceLength);
            T[] rows = [];
            try
            {
                rows = ArrayPool<T>.Shared.Rent(checked(upsampler.OutputWidth * 2));
                Span<T> cbRow = rows.AsSpan(0, upsampler.OutputWidth);
                Span<T> crRow = rows.AsSpan(upsampler.OutputWidth, upsampler.OutputWidth);
                int offset = (sequence.CropTop / sequence.SubHeightC) * cb.Width + sequence.CropLeft / sequence.SubWidthC;
                ReadOnlySpan<T> cbSamples = cb.Samples[offset..];
                ReadOnlySpan<T> crSamples = cr.Samples[offset..];
                ColorPlaneLayout layout = new(crop.Width, 1, crop.Width, crop.Width, 1, 1);
                int maximum = (1 << sequence.BitDepth) - 1;
                for (int y = 0; y < crop.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    upsampler.CopyRow(cbSamples, cb.Width, y + crop.Top, cbRow, workspace, maximum, cancellationToken);
                    upsampler.CopyRow(crSamples, cr.Width, y + crop.Top, crRow, workspace, maximum, cancellationToken);
                    ReadOnlySpan<T> lumaRow = luma.Row(y + sequence.CropTop + crop.Top).Slice(sequence.CropLeft + crop.Left, crop.Width);
                    convert(lumaRow, cbRow.Slice(crop.Left, crop.Width), crRow.Slice(crop.Left, crop.Width), layout,
                        output.Slice(y * crop.Width * 4, crop.Width * 4));
                }
            }
            finally
            {
                if (rows.Length != 0)
                {
                    ArrayPool<T>.Shared.Return(rows);
                }
                ArrayPool<float>.Shared.Return(workspace);
            }
        }
    }
}
