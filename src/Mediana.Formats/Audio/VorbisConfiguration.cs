// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Borrowed Xiph-laced Vorbis headers. The source memory must remain immutable for the configuration lifetime.</summary>
    public class VorbisConfiguration
    {
        private VorbisConfiguration(VorbisIdentification identification, ReadOnlyMemory<byte> comments, ReadOnlyMemory<byte> setup)
        {
            Identification = identification; CommentHeader = comments; SetupHeader = setup;
        }

        public VorbisIdentification Identification { get; }
        public ReadOnlyMemory<byte> CommentHeader { get; }
        public ReadOnlyMemory<byte> SetupHeader { get; }

        public static VorbisConfiguration Read(ReadOnlyMemory<byte> configuration)
        {
            ReadOnlySpan<byte> bytes = configuration.Span;
            if (bytes.IsEmpty || bytes[0] != 2) { throw new InvalidDataException("Vorbis configuration requires three Xiph-laced headers."); }
            int offset = 1;
            int identificationBytes = ReadLength(bytes, ref offset), commentBytes = ReadLength(bytes, ref offset);
            if (identificationBytes > bytes.Length - offset || commentBytes > bytes.Length - offset - identificationBytes)
            {
                throw new InvalidDataException("A Vorbis header extends past its configuration.");
            }
            VorbisIdentification identification = VorbisIdentification.Read(bytes.Slice(offset, identificationBytes));
            offset += identificationBytes;
            ReadOnlyMemory<byte> comments = configuration.Slice(offset, commentBytes), setup = configuration[(offset + commentBytes)..];
            if (comments.Length < 7 || comments.Span[0] != 3 || !comments.Span.Slice(1, 6).SequenceEqual("vorbis"u8) ||
                setup.Length < 7 || setup.Span[0] != 5 || !setup.Span.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                throw new InvalidDataException("The Vorbis comment/setup signatures are absent or out of order.");
            }
            return new(identification, comments, setup);
        }

        private static int ReadLength(ReadOnlySpan<byte> bytes, ref int offset)
        {
            int length = 0;
            while (true)
            {
                if (offset == bytes.Length) { throw new InvalidDataException("The Vorbis Xiph header lacing is truncated."); }
                byte value = bytes[offset++]; length = checked(length + value);
                if (value != 255) { return length; }
            }
        }
    }
}
