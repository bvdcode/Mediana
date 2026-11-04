// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Formats.Asn1;

namespace Mediana.Documents.Pdf
{
    internal static class CmsPdfContent
    {
        private static readonly Asn1Tag ExplicitContent = new(TagClass.ContextSpecific, 0, true);
        private static ReadOnlySpan<byte> SignedDataOid => [0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x02];
        private static ReadOnlySpan<byte> DataOid => [0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x01];

        internal static bool IsSignedDataPrefix(ReadOnlySpan<byte> prefix)
        {
            if (prefix.Length < 2 || prefix[0] != 0x30)
            {
                return false;
            }
            int lengthBytes = prefix[1] > 0x80 ? prefix[1] & 0x7f : 0;
            int contentOffset = 2 + lengthBytes;
            return lengthBytes <= 4 && prefix.Length >= contentOffset + SignedDataOid.Length
                && prefix.Slice(contentOffset, SignedDataOid.Length).SequenceEqual(SignedDataOid);
        }

        internal static ReadOnlyMemory<byte> Unwrap(ReadOnlyMemory<byte> encoded, PdfReaderOptions options,
            CancellationToken token, out bool signedContent)
        {
            signedContent = IsSignedDataPrefix(encoded.Span);
            if (!signedContent)
            {
                return encoded;
            }
            token.ThrowIfCancellationRequested();
            AsnReader root = new(encoded, AsnEncodingRules.BER);
            AsnReader contentInfo = root.ReadSequence();
            RequireOid(contentInfo, SignedDataOid);
            AsnReader wrapper = contentInfo.ReadSequence(ExplicitContent);
            AsnReader signedData = wrapper.ReadSequence();
            if (!signedData.TryReadInt32(out int version) || version is not (1 or 3 or 4 or 5))
            {
                throw new InvalidDataException("Invalid CMS SignedData version.");
            }
            int entries = 0;
            ReadSet(signedData.ReadSetOf(), options, token, ref entries);
            AsnReader encapsulated = signedData.ReadSequence();
            RequireOid(encapsulated, DataOid);
            if (!encapsulated.HasData)
            {
                throw new NotSupportedException("Detached CMS signatures contain no PDF document.");
            }
            AsnReader octets = encapsulated.ReadSequence(ExplicitContent);
            ReadOnlyMemory<byte> pdf;
            if (!octets.TryReadPrimitiveOctetString(out pdf))
            {
                pdf = octets.ReadOctetString();
            }
            octets.ThrowIfNotEmpty();
            encapsulated.ThrowIfNotEmpty();
            foreach (int tag in new[] { 0, 1 })
            {
                if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, tag)))
                {
                    ReadSet(signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, tag, true)), options, token, ref entries);
                }
            }
            ReadSet(signedData.ReadSetOf(), options, token, ref entries);
            signedData.ThrowIfNotEmpty();
            wrapper.ThrowIfNotEmpty();
            contentInfo.ThrowIfNotEmpty();
            root.ThrowIfNotEmpty();
            token.ThrowIfCancellationRequested();
            if (!pdf.Span.StartsWith("%PDF-"u8))
            {
                throw new InvalidDataException("CMS encapsulated content is not a PDF document.");
            }
            return pdf;
        }

        private static void RequireOid(AsnReader reader, ReadOnlySpan<byte> expected)
        {
            if (!reader.ReadEncodedValue().Span.SequenceEqual(expected))
            {
                throw new NotSupportedException("The CMS content type is not supported for PDF rendering.");
            }
        }

        private static void ReadSet(AsnReader reader, PdfReaderOptions options, CancellationToken token, ref int entries)
        {
            while (reader.HasData)
            {
                token.ThrowIfCancellationRequested();
                if (++entries > options.MaximumObjects)
                {
                    throw new InvalidDataException("CMS entries exceed the PDF object limit.");
                }
                reader.ReadEncodedValue();
            }
        }
    }
}
