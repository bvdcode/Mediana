namespace Mediana.Codecs.H264
{
    internal static class H264InterMacroblock
    {
        // Rec. ITU-T H.264, Table 9-4 (4:2:0 inter coded_block_pattern).
        private static ReadOnlySpan<byte> CodedPatterns =>
            [0, 16, 1, 2, 4, 8, 32, 3, 5, 10, 12, 15, 47, 7, 11, 13, 14, 6, 9, 31, 35, 37, 42, 44,
                33, 34, 36, 40, 39, 43, 45, 46, 17, 18, 20, 24, 19, 21, 26, 28, 23, 27, 29, 30, 22, 25, 38, 41];

        public static void Read(ref H264BitReader bits, H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int address, ref int qp, bool skip = false)
        {
            int type = skip ? 0 : bits.ReadUe(30);
            if (type >= 5)
            {
                H264IntraMacroblock.Read(ref bits, target, header, address, type - 5, ref qp);
                return;
            }
            int x = (address % header.Sequence.WidthInMbs) * 16;
            int y = (address / header.Sequence.WidthInMbs) * 16;
            Span<byte> prediction = stackalloc byte[384];
            if (skip)
            {
                (int motionX, int motionY) = H264MotionPrediction.Skip(target, x, y);
                PredictPartition(target, header, references, x, y, 16, 16, 0, motionX, motionY, prediction);
            }
            else
            {
                ReadPrediction(ref bits, target, header, references, x, y, type, prediction);
            }
            int pattern = skip ? 0 : CodedPatterns[bits.ReadUe(47)];
            if (pattern != 0)
            {
                qp = (qp + bits.ReadSe(-26, 25) + 52) % 52;
            }
            target.Quantizers[address] = (byte)qp;
            H264Residuals.ReadInterLuma(ref bits, target, x, y, pattern, qp, prediction[..256]);
            H264Residuals.ReadChroma(ref bits, target, header.Picture, x / 2, y / 2, pattern >> 4, qp, prediction[256..]);
        }

        private static void ReadPrediction(ref H264BitReader bits, H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int x, int y, int type, scoped Span<byte> prediction)
        {
            int partitions = type switch
            {
                0 => 1,
                1 or 2 => 2,
                3 or 4 => 4,
                _ => throw new InvalidDataException("Invalid H.264 P macroblock type.")
            };
            Span<int> subtypes = stackalloc int[4];
            Span<int> indices = stackalloc int[4];
            subtypes.Clear();
            indices.Clear();
            if (partitions == 4)
            {
                for (int part = 0; part < partitions; part++)
                {
                    subtypes[part] = bits.ReadUe(3);
                }
            }
            if (type != 4 && header.ActiveReferencesL0 > 1)
            {
                for (int part = 0; part < partitions; part++)
                {
                    indices[part] = header.ActiveReferencesL0 == 2 ? 1 - (int)bits.ReadBits(1) : bits.ReadUe(header.ActiveReferencesL0 - 1);
                }
            }
            for (int part = 0; part < partitions; part++)
            {
                (int width, int height) = type switch
                {
                    0 => (16, 16),
                    1 => (16, 8),
                    2 => (8, 16),
                    3 or 4 => subtypes[part] switch
                    {
                        0 => (8, 8),
                        1 => (8, 4),
                        2 => (4, 8),
                        3 => (4, 4),
                        _ => throw new InvalidDataException("Invalid H.264 P sub-macroblock type.")
                    },
                    _ => throw new InvalidDataException("Invalid H.264 P macroblock type.")
                };
                int partX = x + (type == 2 ? part * 8 : partitions == 4 ? (part % 2) * 8 : 0);
                int partY = y + (type == 1 ? part * 8 : partitions == 4 ? (part / 2) * 8 : 0);
                int areaWidth = partitions == 4 ? 8 : width;
                int areaHeight = partitions == 4 ? 8 : height;
                for (int subY = 0; subY < areaHeight; subY += height)
                {
                    for (int subX = 0; subX < areaWidth; subX += width)
                    {
                        (int motionX, int motionY) = H264MotionPrediction.Predict(target, partX + subX, partY + subY,
                            width, height, indices[part], part);
                        motionX += bits.ReadSe(-32768, 32767);
                        motionY += bits.ReadSe(-32768, 32767);
                        if (motionX is < -32768 or > 32767 || motionY is < -32768 or > 32767)
                        {
                            throw new InvalidDataException("The H.264 motion vector exceeds its supported range.");
                        }
                        PredictPartition(target, header, references, partX + subX, partY + subY, width, height,
                            indices[part], motionX, motionY, prediction);
                    }
                }
            }
        }

        private static void PredictPartition(H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int x, int y, int width, int height,
            int referenceIndex, int motionX, int motionY, Span<byte> prediction)
        {
            if ((uint)referenceIndex >= (uint)references.Count || references[referenceIndex] is not H264ReferencePicture reference)
            {
                throw new InvalidDataException("The H.264 partition uses an unavailable reference picture.");
            }
            target.SetMotion(x, y, width, height, referenceIndex, motionX, motionY);
            Span<byte> block = stackalloc byte[256];
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int blockWidth = width / scale;
                int blockHeight = height / scale;
                Span<byte> samples = block[..(blockWidth * blockHeight)];
                if (plane == 0)
                {
                    H264MotionCompensation.Luma(reference.Picture, x, y, motionX, motionY, width, height, samples);
                }
                else
                {
                    H264MotionCompensation.Chroma(reference.Picture, plane, x / 2, y / 2, motionX, motionY, blockWidth, blockHeight, samples);
                }
                header.Weights?.Apply(referenceIndex, plane, samples);
                int destination = plane switch
                {
                    0 => 0,
                    1 => 256,
                    2 => 320,
                    _ => throw new ArgumentOutOfRangeException(nameof(plane))
                };
                int stride = 16 / scale;
                destination += ((y % 16) / scale) * stride + (x % 16) / scale;
                for (int row = 0; row < blockHeight; row++)
                {
                    samples.Slice(row * blockWidth, blockWidth).CopyTo(prediction.Slice(destination + row * stride, blockWidth));
                }
            }
        }
    }
}
