using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264EightByEightTestInput
    {
        public static async Task VerifyExplicitPicturesAsync(string root)
        {
            byte[] stream = await File.ReadAllBytesAsync(Path.Combine(root, "intra8-modes.h264"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "intra8-modes.yuv"));
            H264Configuration configuration = new(4, new Dictionary<int, H264SequenceParameterSet>(), new Dictionary<int, H264PictureParameterSet>());
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                int offset = 0;
                int frame = 0;
                while (offset < stream.Length)
                {
                    if (!stream.AsSpan(offset, 4).SequenceEqual(new byte[] { 0, 0, 0, 1 }))
                    {
                        throw new InvalidDataException("The explicit prediction input has an invalid NAL prefix.");
                    }
                    offset += 4;
                    int next = stream.AsSpan(offset).IndexOf(new byte[] { 0, 0, 0, 1 });
                    int length = next < 0 ? stream.Length - offset : next;
                    ReadOnlySpan<byte> nal = stream.AsSpan(offset, length);
                    switch (H264Nal.Type(nal))
                    {
                        case 7 or 8:
                            configuration = configuration.WithParameterSet(nal, new H264DecoderOptions());
                            break;
                        case 5:
                            DecodeAndVerify(nal, configuration, references, allocator, reference.AsSpan(frame * 1536, 1536));
                            frame++;
                            break;
                        default:
                            throw new InvalidDataException("The explicit prediction input has an unexpected NAL type.");
                    }
                    offset += length;
                }
                if (frame != 9)
                {
                    throw new InvalidDataException("The explicit prediction input has an unexpected picture count.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The explicit prediction decoder retained picture storage.");
            }
        }

        public static async Task<(int Intra, int Inter)> VerifySequenceAsync(string root, string name, int count, int? initialization)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv"));
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            int intra = 0;
            int inter = 0;
            using (H264ReferenceBuffer references = new())
            {
                for (int index = 0; index < count; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), index);
                    H264SequenceParameterSet sequence = configuration.Sequences[0];
                    int frameBytes = sequence.Width * sequence.Height * 3 / 2;
                    if (expected.Length != count * frameBytes || configuration.Pictures[0].Cabac != initialization.HasValue || !configuration.Pictures[0].Transform8x8)
                    {
                        throw new InvalidDataException("The 8x8 sequence has unexpected parameters or reference length.");
                    }
                    H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                    H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                    if (initialization.HasValue && header.Kind == H264SliceKind.P && header.CabacInitIdc != initialization.Value)
                    {
                        throw new InvalidDataException("The 8x8 sequence has an unexpected CABAC initialization selection.");
                    }
                    (int pictureIntra, int pictureInter) = DecodeAndVerify(nal, configuration, references, allocator,
                        expected.AsSpan(index * frameBytes, frameBytes));
                    intra += pictureIntra;
                    inter += pictureInter;
                }
            }
            if (allocator.AllocatedBytes != 0 || intra == 0 || inter == 0)
            {
                throw new InvalidOperationException("The sequence did not exercise intra/inter 8x8 or retained storage.");
            }
            return (intra, inter);
        }

        private static (int Intra, int Inter) DecodeAndVerify(ReadOnlySpan<byte> nal, H264Configuration configuration,
            H264ReferenceBuffer references, MediaBufferAllocator allocator, ReadOnlySpan<byte> expected)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264Picture picture = H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator);
            bool stored = false;
            try
            {
                H264TestInput.VerifyPicture(picture, expected, header.FrameNumber);
                int intra = 0;
                int inter = 0;
                for (int address = 0; address < picture.Transforms8x8.Length; address++)
                {
                    if (picture.Transforms8x8[address] == 0)
                    {
                        continue;
                    }
                    int position = picture.BlockIndex(0, address % header.Sequence.WidthInMbs * 16, address / header.Sequence.WidthInMbs * 16);
                    if (picture.Modes[position] == H264Picture.InterMode)
                    {
                        inter++;
                    }
                    else
                    {
                        intra++;
                    }
                }
                references.Store(picture, header);
                stored = true;
                return (intra, inter);
            }
            finally
            {
                if (!stored)
                {
                    picture.Dispose();
                }
            }
        }

        public static async Task VerifyPredictionVectorsAsync(string root)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "intra8-modes.yuv"));
            const int frameBytes = 32 * 32 * 3 / 2;
            if (reference.Length != 9 * frameBytes)
            {
                throw new InvalidDataException("The intra 8x8 prediction reference has an unexpected size.");
            }
            byte[] prediction = new byte[64];
            byte[] top = new byte[16];
            byte[] left = new byte[8];
            for (int mode = 0; mode < 9; mode++)
            {
                ReadOnlySpan<byte> frame = reference.AsSpan(mode * frameBytes, frameBytes);
                for (int block = 0; block < 4; block++)
                {
                    int x = 16 + block % 2 * 8;
                    int y = 16 + block / 2 * 8;
                    for (int index = 0; index < 16; index++)
                    {
                        bool topRightUnavailable = x + index >= 32;
                        top[index] = frame[(y - 1) * 32 + (topRightUnavailable ? x + 7 : x + index)];
                    }
                    for (int index = 0; index < 8; index++)
                    {
                        left[index] = frame[(y + index) * 32 + x - 1];
                    }
                    H264IntraPrediction.Luma8x8(mode, top, left, frame[(y - 1) * 32 + x - 1], prediction);
                    for (int row = 0; row < 8; row++)
                    {
                        if (!prediction.AsSpan(row * 8, 8).SequenceEqual(frame.Slice((y + row) * 32 + x, 8)))
                        {
                            throw new InvalidDataException($"Intra 8x8 prediction differs in mode {mode}, block {block}, row {row}.");
                        }
                    }
                }
            }
        }
    }
}
