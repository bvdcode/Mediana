// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264SeiReader
    {
        public static bool Validate(ReadOnlySpan<byte> nal, CancellationToken token)
        {
            H264BitReader bits = H264Nal.Payload(nal, 6);
            if ((nal[0] & 0x60) != 0)
            {
                throw new InvalidDataException("An H.264 SEI NAL must have a zero reference indicator.");
            }
            bool pictureTiming = false;
            do
            {
                (int type, int length) = ReadHeader(ref bits, nal.Length);
                if (type == 1)
                {
                    if (pictureTiming)
                    {
                        throw new InvalidDataException("An H.264 SEI NAL contains multiple picture-timing messages.");
                    }
                    pictureTiming = true;
                }
                SkipPayload(ref bits, length, token);
            } while (bits.MoreRbspData());
            bits.ReadTrailingBits();
            return pictureTiming;
        }

        public static H264PictureTiming? ReadPictureTiming(ReadOnlySpan<byte> nal, H264VuiParameters? vui, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (nal.IsEmpty)
            {
                return null;
            }
            H264BitReader bits = H264Nal.Payload(nal, 6);
            do
            {
                (int type, int length) = ReadHeader(ref bits, nal.Length);
                if (type == 1)
                {
                    token.ThrowIfCancellationRequested();
                    return H264PictureTimingReader.Read(ref bits, length, vui);
                }
                SkipPayload(ref bits, length, token);
            } while (bits.MoreRbspData());
            return null;
        }

        private static (int Type, int Length) ReadHeader(ref H264BitReader bits, int maximumBytes)
        {
            int type = ReadExtended(ref bits, int.MaxValue);
            int length = ReadExtended(ref bits, maximumBytes);
            return (type, length);
        }

        private static void SkipPayload(ref H264BitReader bits, int length, CancellationToken token)
        {
            for (int index = 0; index < length; index++)
            {
                if ((index & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                bits.ReadBits(8);
            }
        }

        private static int ReadExtended(ref H264BitReader bits, int maximum)
        {
            int value = 0;
            int next;
            do
            {
                next = (int)bits.ReadBits(8);
                if (next > maximum - value)
                {
                    throw new InvalidDataException("The H.264 SEI field exceeds its permitted length.");
                }
                value += next;
            } while (next == 255);
            return value;
        }
    }
}
