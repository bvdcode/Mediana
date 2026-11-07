// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal class MatroskaHeaderFields
    {
        private byte seen;
        private ulong ebmlVersion = 1;
        private ulong ebmlReadVersion = 1;
        private ulong maximumIdWidth = 4;
        private ulong maximumSizeWidth = 8;
        private ulong documentVersion = 1;
        private ulong documentReadVersion = 1;
        private string? documentType;

        public void Read(uint id, ReadOnlySpan<byte> data)
        {
            byte field = id switch
            {
                0x4286 => 1,
                0x42F7 => 2,
                0x42F2 => 4,
                0x42F3 => 8,
                0x4282 => 16,
                0x4287 => 32,
                0x4285 => 64,
                0x4281 => throw new NotSupportedException("EBML document-type extensions are not supported."),
                _ => 0
            };
            if (field == 0)
            {
                return;
            }
            if ((seen & field) != 0)
            {
                throw new InvalidDataException("An EBML document-header field is duplicated.");
            }
            seen |= field;
            switch (id)
            {
                case 0x4286:
                    ebmlVersion = EbmlInteger.ReadUnsigned(data, 1);
                    break;
                case 0x42F7:
                    ebmlReadVersion = EbmlInteger.ReadUnsigned(data, 1);
                    break;
                case 0x42F2:
                    maximumIdWidth = EbmlInteger.ReadUnsigned(data, 4);
                    break;
                case 0x42F3:
                    maximumSizeWidth = EbmlInteger.ReadUnsigned(data, 8);
                    break;
                case 0x4282:
                    documentType = EbmlValue.ReadString(data);
                    break;
                case 0x4287:
                    documentVersion = EbmlInteger.ReadUnsigned(data, 1);
                    break;
                case 0x4285:
                    documentReadVersion = EbmlInteger.ReadUnsigned(data, 1);
                    break;
                default:
                    throw new InvalidOperationException("The EBML document-header field is not mapped.");
            }
        }

        public MatroskaHeader Complete(long bodyOffset)
        {
            if (string.IsNullOrEmpty(documentType) || ebmlVersion == 0 || ebmlReadVersion == 0 ||
                documentVersion == 0 || documentReadVersion == 0 || ebmlReadVersion > ebmlVersion ||
                documentReadVersion > documentVersion || maximumIdWidth != 4 || maximumSizeWidth is < 1 or > 8)
            {
                throw new InvalidDataException("The Matroska EBML document header is incomplete or invalid.");
            }
            if (documentType is not ("matroska" or "webm"))
            {
                throw new NotSupportedException("The EBML document type is neither Matroska nor WebM.");
            }
            if (ebmlReadVersion > 1 || documentReadVersion > 4)
            {
                throw new NotSupportedException("The document requires a newer EBML or Matroska reader version.");
            }
            return new(bodyOffset, ebmlVersion, documentVersion, documentReadVersion, (int)maximumSizeWidth, documentType);
        }
    }
}
