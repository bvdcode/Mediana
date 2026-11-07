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
            XiphLacedHeaders headers = XiphLacedHeaders.Read(configuration);
            VorbisIdentification identification = VorbisIdentification.Read(headers.First.Span);
            ReadOnlyMemory<byte> comments = headers.Second, setup = headers.Third;
            if (comments.Length < 7 || comments.Span[0] != 3 || !comments.Span.Slice(1, 6).SequenceEqual("vorbis"u8) ||
                setup.Length < 7 || setup.Span[0] != 5 || !setup.Span.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                throw new InvalidDataException("The Vorbis comment/setup signatures are absent or out of order.");
            }
            return new(identification, comments, setup);
        }

    }
}
