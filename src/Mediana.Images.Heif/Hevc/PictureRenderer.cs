// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using Mediana.Images.Heif.Container;

using Mediana.Codecs.Hevc;
using Mediana.Core.Video;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Renders a clean aperture within the SPS conformance window into owned RGBA8 rows.
    /// </summary>
    /// <remarks>
    /// Subsampled chroma uses the selected reconstruction kernel before RGB conversion.
    /// Alpha auxiliary samples use their SEI range and clipping operations when present,
    /// otherwise their full integer range, independently of video colour-range flags. Output arrays never alias reconstructed picture planes.
    /// </remarks>
    internal static partial class PictureRenderer
    {
        public static RgbaImage Render(DecodedPicture picture, ImageCrop crop, ColorEncoding color, IccColorDescription? iccProfile,
            bool alpha, PixelBufferScope buffers, ChromaUpsamplingFilter chromaFilter, CancellationToken cancellationToken,
            AlphaChannelInformation? alphaInformation = null, Memory<byte> destination = default)
        {
            ColorConversion? conversion = CreateConversion(picture, color, alpha, alphaInformation);
            int width = crop.Width;
            int height = crop.Height;
            Memory<byte> pixels = buffers.Allocate(checked(width * height * 4), destination);

            if (!alpha && picture.Cb is not null && picture.Cr is not null)
            {
                if (picture.Sequence.ChromaFormat == 3 || (chromaFilter == ChromaUpsamplingFilter.NearestNeighbor
                    && picture.Sequence.Video.ChromaLocation is 0 or 1 or 2 or 3))
                {
                    RenderColor(picture, crop, conversion!, pixels.Span);
                }
                else
                {
                    RenderFilteredColor(picture, crop, conversion!, chromaFilter, pixels.Span, cancellationToken);
                }
            }
            else
            {
                RenderGrayscale(picture, crop, conversion, pixels.Span, alpha, alphaInformation);
            }

            return new RgbaImage(width, height, pixels, new ImageColorInformation(color.Primaries, color.Transfer, iccProfile));
        }

        private static ColorConversion? CreateConversion(DecodedPicture picture, ColorEncoding color, bool alpha,
            AlphaChannelInformation? alphaInformation)
        {
            SequenceParameters sequence = picture.Sequence;
            if (alpha)
            {
                if (alphaInformation is not null && (alphaInformation.BitDepth != sequence.BitDepth))
                {
                    throw new InvalidDataException("Alpha information depth disagrees with the HEVC sequence.");
                }
                return null;
            }

            int matrix = picture.Cb is null ? 1 : color.Matrix;
            return new ColorConversion(sequence.BitDepth, color.FullRange, matrix);
        }

        private static void RenderGrayscale(DecodedPicture picture, ImageCrop crop, ColorConversion? conversion,
            Span<byte> pixels, bool alpha, AlphaChannelInformation? alphaInformation)
        {
            SequenceParameters sequence = picture.Sequence;
            switch (sequence.BitDepth)
            {
                case 8:
                    RenderGray<byte, ByteSampleFormat>(picture.Luma.ByteView, sequence, crop, conversion, pixels, alpha, alphaInformation);
                    break;
                case 10:
                    RenderGray<ushort, WordSampleFormat>(picture.Luma.WordView, sequence, crop, conversion, pixels, alpha, alphaInformation);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported component sample depth.");
            }
        }

        private static void RenderGray<TSample, TFormat>(SamplePlaneView<TSample> plane, SequenceParameters sequence,
            ImageCrop crop, ColorConversion? conversion, Span<byte> pixels, bool alpha, AlphaChannelInformation? alphaInformation)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int maximum = (1 << sequence.BitDepth) - 1;
            for (int y = 0; y < crop.Height; y++)
            {
                ReadOnlySpan<TSample> luma = plane.Row(y + sequence.CropTop + crop.Top).Slice(sequence.CropLeft + crop.Left, crop.Width);
                Span<byte> row = pixels.Slice(y * crop.Width * 4, crop.Width * 4);
                if (alpha)
                {
                    for (int x = 0; x < crop.Width; x++)
                    {
                        int sample = default(TFormat).Read(luma[x]);
                        byte value = alphaInformation is null
                            ? (byte)(((sample * 255) + (maximum / 2)) / maximum)
                            : alphaInformation.Convert(sample);
                        WriteGray(value, row.Slice(x * 4, 4));
                    }
                }
                else
                {
                    for (int x = 0; x < crop.Width; x++)
                    {
                        WriteGray(conversion!.Gray(default(TFormat).Read(luma[x])), row.Slice(x * 4, 4));
                    }
                }
            }
        }

        private static void WriteGray(byte value, Span<byte> target)
        {
            target[0] = value;
            target[1] = value;
            target[2] = value;
            target[3] = 255;
        }

        private static void RenderColor(DecodedPicture picture, ImageCrop crop, ColorConversion conversion, Span<byte> pixels)
        {
            SequenceParameters sequence = picture.Sequence;
            int width = crop.Width;
            int height = crop.Height;
            int left = sequence.CropLeft + crop.Left;
            int top = sequence.CropTop + crop.Top;
            int subWidth = sequence.SubWidthC;
            int subHeight = sequence.SubHeightC;
            ColorPlaneLayout layout = new ColorPlaneLayout(width, height, picture.Luma.Stride,
                picture.Cb!.Stride, subWidth, subHeight, left % subWidth, top % subHeight);
            int lumaOffset = (top * picture.Luma.Stride) + left;
            int chromaOffset = ((top / subHeight) * picture.Cb!.Stride) + (left / subWidth);
            switch (sequence.BitDepth)
            {
                case 8:
                    conversion!.ConvertRows(picture.Luma.ByteView.Samples.Slice(lumaOffset),
                        picture.Cb!.ByteView.Samples.Slice(chromaOffset), picture.Cr!.ByteView.Samples.Slice(chromaOffset), layout, pixels);
                    break;
                case 10:
                    conversion!.ConvertRows(picture.Luma.WordView.Samples.Slice(lumaOffset),
                        picture.Cb!.WordView.Samples.Slice(chromaOffset), picture.Cr!.WordView.Samples.Slice(chromaOffset), layout, pixels);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported component sample depth.");
            }
        }
    }
}
