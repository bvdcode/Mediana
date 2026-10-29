// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Hevc
{
    internal static class HrdParameters
    {
        public static void Skip(BitReader reader, int maxSubLayer)
        {
            bool nal = reader.ReadFlag();
            bool vcl = reader.ReadFlag();
            bool subPicture = false;
            if (nal || vcl)
            {
                subPicture = reader.ReadFlag();
                if (subPicture)
                {
                    reader.SkipBits(19);
                }
                reader.SkipBits(8);
                if (subPicture)
                {
                    reader.SkipBits(4);
                }
                reader.SkipBits(15);
            }

            for (int i = 0; i <= maxSubLayer; i++)
            {
                bool fixedRate = reader.ReadFlag();
                if (!fixedRate)
                {
                    fixedRate = reader.ReadFlag();
                }
                bool lowDelay = false;
                if (fixedRate)
                {
                    reader.ReadUev();
                }
                else
                {
                    lowDelay = reader.ReadFlag();
                }
                int count = lowDelay ? 1 : HevcSyntax.Ue(reader, 31, "cpb_cnt_minus1") + 1;
                if (nal)
                {
                    SkipSubLayer(reader, count, subPicture);
                }
                if (vcl)
                {
                    SkipSubLayer(reader, count, subPicture);
                }
            }
        }

        private static void SkipSubLayer(BitReader reader, int count, bool subPicture)
        {
            for (int i = 0; i < count; i++)
            {
                reader.ReadUev();
                reader.ReadUev();
                if (subPicture)
                {
                    reader.ReadUev();
                    reader.ReadUev();
                }

                reader.ReadFlag();
            }
        }
    }
}
