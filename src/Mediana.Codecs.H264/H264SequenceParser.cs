namespace Mediana.Codecs.H264
{
    internal static class H264SequenceParser
    {
        public static H264SequenceParameterSet Parse(ReadOnlySpan<byte> nal, H264DecoderOptions options)
        {
            H264BitReader bits = H264Nal.Payload(nal, 7);
            int profile = (int)bits.ReadBits(8);
            int constraints = (int)bits.ReadBits(8);
            if ((constraints & 3) != 0)
            {
                throw new InvalidDataException("Reserved H.264 constraint bits are nonzero.");
            }
            int level = (int)bits.ReadBits(8);
            int id = bits.ReadUe(31);
            H264SequenceParameterSet sequence = new() { Id = id, Profile = profile, Constraints = constraints, Level = level };
            if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
            {
                int chromaFormat = bits.ReadUe(3);
                bool separateColourPlane = chromaFormat == 3 && bits.ReadFlag();
                int lumaDepth = bits.ReadUe(6) + 8;
                int chromaDepth = bits.ReadUe(6) + 8;
                bool bypass = bits.ReadFlag();
                bool scaling = bits.ReadFlag();
                if (scaling)
                {
                    int count = chromaFormat == 3 ? 12 : 8;
                    for (int index = 0; index < count; index++)
                    {
                        if (bits.ReadFlag())
                        {
                            ReadScalingList(ref bits, index < 6 ? 16 : 64);
                        }
                    }
                }
                sequence = sequence with
                {
                    ChromaFormat = chromaFormat,
                    SeparateColourPlane = separateColourPlane,
                    LumaBitDepth = lumaDepth,
                    ChromaBitDepth = chromaDepth,
                    TransformBypass = bypass,
                    ScalingMatrix = scaling
                };
            }
            int frameBits = bits.ReadUe(12) + 4;
            int orderType = bits.ReadUe(2);
            int orderBits = 0;
            bool deltaAlwaysZero = false;
            int nonReferenceOffset = 0;
            int topToBottomOffset = 0;
            H264PictureOrderCycle orderCycle = H264PictureOrderCycle.Empty;
            if (orderType == 0)
            {
                orderBits = bits.ReadUe(12) + 4;
            }
            else if (orderType == 1)
            {
                deltaAlwaysZero = bits.ReadFlag();
                nonReferenceOffset = bits.ReadSe();
                topToBottomOffset = bits.ReadSe();
                int cycle = bits.ReadUe(255);
                Span<int> offsets = stackalloc int[cycle];
                for (int index = 0; index < cycle; index++)
                {
                    offsets[index] = bits.ReadSe();
                }
                orderCycle = new H264PictureOrderCycle(offsets);
            }
            int references = bits.ReadUe(16);
            bool gapsAllowed = bits.ReadFlag();
            int widthMbs = checked(bits.ReadUe(65535) + 1);
            int heightMapUnits = checked(bits.ReadUe(65535) + 1);
            bool frameOnly = bits.ReadFlag();
            bool mbAdaptive = !frameOnly && bits.ReadFlag();
            bool direct8x8Inference = bits.ReadFlag();
            int left = 0;
            int right = 0;
            int top = 0;
            int bottom = 0;
            if (bits.ReadFlag())
            {
                left = bits.ReadUe();
                right = bits.ReadUe();
                top = bits.ReadUe();
                bottom = bits.ReadUe();
            }
            H264VuiParameters? vui = bits.ReadFlag() ? H264VuiParser.Parse(ref bits, references) : null;
            bits.ReadTrailingBits();
            int chromaArray = sequence.SeparateColourPlane ? 0 : sequence.ChromaFormat;
            int cropUnitX = chromaArray switch
            {
                0 or 3 => 1,
                1 or 2 => 2,
                _ => throw new InvalidDataException("The H.264 chroma array type is invalid.")
            };
            int cropUnitY = (chromaArray == 1 ? 2 : 1) * (frameOnly ? 1 : 2);
            long codedWidth = (long)widthMbs * 16;
            long codedHeight = (long)heightMapUnits * (frameOnly ? 16 : 32);
            long width = codedWidth - ((long)left + right) * cropUnitX;
            long height = codedHeight - ((long)top + bottom) * cropUnitY;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("H.264 cropping removes the complete picture.");
            }
            if (codedWidth * codedHeight > options.MaxPicturePixels)
            {
                throw new NotSupportedException("The H.264 picture exceeds the configured pixel limit.");
            }
            return sequence with
            {
                FrameNumberBits = frameBits,
                PictureOrderType = orderType,
                PictureOrderBits = orderBits,
                DeltaPictureOrderAlwaysZero = deltaAlwaysZero,
                NonReferencePictureOrderOffset = nonReferenceOffset,
                TopToBottomPictureOrderOffset = topToBottomOffset,
                PictureOrderCycle = orderCycle,
                ReferenceFrames = references,
                FrameNumberGapsAllowed = gapsAllowed,
                Vui = vui,
                WidthInMbs = widthMbs,
                HeightInMapUnits = heightMapUnits,
                FrameMbsOnly = frameOnly,
                MbAdaptiveFrameField = mbAdaptive,
                Direct8x8Inference = direct8x8Inference,
                CropLeft = checked(left * cropUnitX),
                CropTop = checked(top * cropUnitY),
                Width = (int)width,
                Height = (int)height
            };
        }

        private static void ReadScalingList(ref H264BitReader bits, int size)
        {
            int last = 8;
            int next = 8;
            for (int index = 0; index < size; index++)
            {
                if (next != 0)
                {
                    next = (last + bits.ReadSe(-128, 127) + 256) % 256;
                }
                if (next != 0)
                {
                    last = next;
                }
            }
        }
    }
}
