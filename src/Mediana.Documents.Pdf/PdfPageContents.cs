// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal static class PdfPageContents
    {
        /// <summary>Reads one borrowed decoded stream or concatenates multiple streams into one exact owned buffer.</summary>
        internal static ReadOnlyMemory<byte> Read(PdfStorage storage, PdfValue source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            PdfValue contents = storage.Resolve(source, token);
            if (contents.Kind == PdfValueKind.Null)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
            if (contents.Kind != PdfValueKind.Array)
            {
                return storage.Decode(storage.Object(source, token), token);
            }
            PdfValue[] references = contents.Items;
            if (references.Length == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
            if (references.Length == 1)
            {
                return storage.Decode(storage.Object(references[0], token), token);
            }
            ReadOnlyMemory<byte>[] streams = new ReadOnlyMemory<byte>[references.Length];
            int length = 0;
            for (int index = 0; index < references.Length; index++)
            {
                ReadOnlyMemory<byte> bytes = storage.Decode(storage.Object(references[index], token), token);
                if (bytes.Length > storage.Budget.Options.MaximumDecodedBytes - length)
                {
                    throw new InvalidDataException("PDF page contents exceed their limit.");
                }
                streams[index] = bytes;
                length += bytes.Length;
            }
            if (length > Array.MaxLength)
            {
                throw new InvalidDataException("PDF page contents exceed the supported array size.");
            }
            byte[] combined = new byte[length];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> bytes in streams)
            {
                token.ThrowIfCancellationRequested();
                bytes.Span.CopyTo(combined.AsSpan(offset));
                offset += bytes.Length;
            }
            return combined;
        }
    }
}
