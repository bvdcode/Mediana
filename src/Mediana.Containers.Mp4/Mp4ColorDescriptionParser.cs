// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4ColorDescriptionParser
    {
        public static IReadOnlyList<VideoColorDescription> Read(Mp4BoxCollection boxes)
        {
            List<VideoColorDescription> descriptions = [];
            foreach (Mp4Box box in boxes.Boxes)
            {
                if (box.Type != "colr")
                {
                    continue;
                }
                ReadOnlySpan<byte> payload = box.Payload.Span;
                string type = Mp4Binary.FourCc(payload, 0);
                VideoColorDescription? description = type switch
                {
                    "nclx" => ReadCicp(payload, true),
                    "nclc" => ReadCicp(payload, false),
                    "rICC" => ReadIcc(payload, true),
                    "prof" => ReadIcc(payload, false),
                    _ => null
                };
                if (description is not null)
                {
                    descriptions.Add(description);
                }
            }
            return descriptions.AsReadOnly();
        }

        public static VideoColorDescription? Select(IReadOnlyList<VideoColorDescription>? descriptions)
        {
            if (descriptions is null || descriptions.Count == 0)
            {
                return null;
            }
            foreach (VideoColorDescription description in descriptions)
            {
                if (description is CicpColorDescription)
                {
                    return description;
                }
            }
            return descriptions[0];
        }

        private static CicpColorDescription ReadCicp(ReadOnlySpan<byte> payload, bool hasRange)
        {
            int expectedLength = hasRange ? 11 : 10;
            if (payload.Length != expectedLength || (hasRange && (payload[10] & 0x7f) != 0))
            {
                throw new InvalidDataException("The MP4 CICP color description has invalid fields or reserved bits.");
            }
            bool? fullRange = null;
            if (hasRange)
            {
                fullRange = (payload[10] & 0x80) != 0;
            }
            return new CicpColorDescription(Mp4Binary.UInt16(payload, 4), Mp4Binary.UInt16(payload, 6),
                Mp4Binary.UInt16(payload, 8), fullRange);
        }

        private static IccColorDescription ReadIcc(ReadOnlySpan<byte> payload, bool restricted)
        {
            if (payload.Length == 4)
            {
                throw new InvalidDataException("The MP4 ICC color description has no profile bytes.");
            }
            return new IccColorDescription(payload[4..], restricted);
        }
    }
}
