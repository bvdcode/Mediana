// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Formats.Asn1;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class PdfCmsTestInput
    {
        private static readonly Asn1Tag ExplicitContent = new(TagClass.ContextSpecific, 0, true);
        internal const string DataOid = "1.2.840.113549.1.7.1";

        internal static byte[] Create(byte[]? content, bool constructed = false, string contentType = DataOid, int certificates = 0)
        {
            AsnWriter writer = new(AsnEncodingRules.BER);
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
                using (writer.PushSequence(ExplicitContent))
                using (writer.PushSequence())
                {
                    writer.WriteInteger(1);
                    using (writer.PushSetOf()) { }
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier(contentType);
                        if (content is not null)
                        {
                            using (writer.PushSequence(ExplicitContent))
                            {
                                if (constructed)
                                {
                                    writer.WriteEncodedValue(ConstructedOctets(content));
                                }
                                else
                                {
                                    writer.WriteOctetString(content);
                                }
                            }
                        }
                    }
                    if (certificates > 0)
                    {
                        using (writer.PushSetOf(ExplicitContent))
                        {
                            for (int i = 0; i < certificates; i++)
                            {
                                using (writer.PushSequence()) { }
                            }
                        }
                    }
                    using (writer.PushSetOf()) { }
                }
            }
            return writer.Encode();
        }

        private static byte[] ConstructedOctets(byte[] content)
        {
            using MemoryStream output = new();
            output.Write([0x24, 0x80]);
            int midpoint = content.Length / 2;
            AsnWriter chunks = new(AsnEncodingRules.BER);
            chunks.WriteOctetString(content.AsSpan(0, midpoint));
            chunks.WriteOctetString(content.AsSpan(midpoint));
            output.Write(chunks.Encode());
            output.Write([0, 0]);
            return output.ToArray();
        }

        internal static byte[] Pdf() => PdfTestDocument.Create("1 0 0 rg 0 0 200 100 re f");

        internal static async Task VerifyAsync()
        {
            foreach (bool constructed in new[] { false, true })
            {
                byte[] encoded = Create(Pdf(), constructed);
                PdfImageDecoder decoder = new(renderOptions: new() { Dpi = 72, Supersampling = 1 });
                if (!decoder.CanDecode(encoded.AsSpan(0, decoder.ProbeLength)))
                {
                    throw new InvalidDataException("CMS PDF was not recognized from its content type.");
                }
                MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
                await using RandomAccessStreamByteSource source = new(new MemoryStream(encoded));
                MediaInfo metadata = await engine.ProbeAsync(source);
                ImageInfo? info = metadata.Image;
                if (info is null || info.FormatId != "pdf" || info.Width != 200 || info.Height != 100 || info.PageCount != 1)
                {
                    throw new InvalidDataException("CMS PDF metadata differs from its encapsulated document.");
                }
                List<string> warnings = [];
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator, new() { WarningHandler = warnings.Add }))
                {
                    encoded.AsSpan().Clear();
                    if (frame.Format.Width != 200 || frame.Format.Height != 100
                        || !frame.GetRow(0, 50).Slice(100 * 4, 4).SequenceEqual(new byte[] { 255, 0, 0, 255 })
                        || warnings.Count != 1 || warnings[0] != "CMS signature verification was not performed.")
                    {
                        throw new InvalidDataException("CMS PDF rendering lost its pixels or signature diagnostic.");
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("CMS PDF rendering retained an output buffer.");
                }
            }
            using PdfDocument ordinary = await PdfReader.ReadAsync(new MemoryStream(Pdf()));
            if (ordinary.Warnings.Count != 0)
            {
                throw new InvalidDataException("An ordinary PDF reported CMS signature diagnostics.");
            }
        }
    }
}
