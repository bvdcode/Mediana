// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>ProRes frame-level geometry and color signaling from SMPTE RDD 36.</summary>
    public record ProResFrameHeader(int FrameBytes, int HeaderBytes, int Width, int Height,
        int ChromaFormat, int InterlaceMode, int AlphaType, int AspectCode, VideoColorInfo Color,
        ReadOnlyMemory<byte> LumaWeights, ReadOnlyMemory<byte> ChromaWeights)
    {
        private static readonly byte[] DefaultWeights = CreateDefaultWeights();
        public const string CodecId = "prores";

        public int BitDepth => ChromaFormat switch
        {
            2 => 10,
            3 => 12,
            _ => throw new InvalidDataException("The ProRes chroma format is reserved.")
        };

        public VideoScanMode ScanMode => InterlaceMode switch
        {
            0 => VideoScanMode.Progressive,
            1 => VideoScanMode.TopFieldFirst,
            2 => VideoScanMode.BottomFieldFirst,
            _ => throw new InvalidDataException("The ProRes field order is reserved.")
        };

        public SampleAspectRatio? SampleAspectRatio => AspectCode switch
        {
            0 => null,
            1 => Core.Video.SampleAspectRatio.Square,
            2 => new(4 * Height, 3 * Width),
            3 => new(16 * Height, 9 * Width),
            _ => throw new InvalidDataException("The ProRes aspect ratio code is reserved.")
        };

        /// <summary>Reads the declared fields and optional matrices from a frame or its bounded header prefix.</summary>
        public static ProResFrameHeader Read(ReadOnlyMemory<byte> encoded)
        {
            ReadOnlySpan<byte> data = encoded.Span;
            if (data.Length < 28 || !data.Slice(4, 4).SequenceEqual("icpf"u8))
            {
                throw new InvalidDataException("The ProRes frame signature or header is truncated.");
            }
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data);
            int header = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
            int width = BinaryPrimitives.ReadUInt16BigEndian(data[16..]);
            int height = BinaryPrimitives.ReadUInt16BigEndian(data[18..]);
            int chroma = data[20] >> 6;
            int interlace = (data[20] >> 2) & 3;
            int alpha = data[25] & 15;
            if (size > int.MaxValue || size < 36 || header < 20 || header + 8 > size ||
                width == 0 || height == 0 || chroma is not (2 or 3) || interlace == 3 || alpha > 2)
            {
                throw new InvalidDataException("The ProRes geometry, component layout or header extent is invalid.");
            }
            if (BinaryPrimitives.ReadUInt16BigEndian(data[10..]) > 1)
            {
                throw new NotSupportedException("The ProRes bitstream version is unsupported.");
            }
            int offset = 28;
            ReadOnlyMemory<byte> luma = DefaultWeights;
            if ((data[27] & 2) != 0)
            {
                luma = ReadWeights(encoded, ref offset, Math.Min(header + 8, data.Length));
            }
            ReadOnlyMemory<byte> chromaWeights = luma;
            if ((data[27] & 1) != 0)
            {
                chromaWeights = ReadWeights(encoded, ref offset, Math.Min(header + 8, data.Length));
            }
            VideoColorInfo color = new(CicpColorimetry.GetPrimaries(data[22]), CicpColorimetry.GetTransfer(data[23]),
                CicpColorimetry.GetMatrix(data[24]), ColorRange.Limited, ChromaLocation.Left);
            return new((int)size, header + 8, width, height, chroma, interlace, alpha, data[21] >> 4, color, luma, chromaWeights);
        }

        private static byte[] CreateDefaultWeights()
        {
            byte[] weights = new byte[64];
            weights.AsSpan().Fill(4);
            return weights;
        }

        private static ReadOnlyMemory<byte> ReadWeights(ReadOnlyMemory<byte> encoded, ref int offset, int end)
        {
            if (end - offset < 64)
            {
                throw new InvalidDataException("The ProRes quantization matrix is truncated.");
            }
            ReadOnlyMemory<byte> result = encoded.Slice(offset, 64);
            foreach (byte weight in result.Span)
            {
                if (weight == 0)
                {
                    throw new InvalidDataException("ProRes quantization weights must be positive.");
                }
            }
            offset += 64;
            return result;
        }
    }
}
