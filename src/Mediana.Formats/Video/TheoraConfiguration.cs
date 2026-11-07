// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Borrowed Theora identification, comment and setup headers.</summary>
    public class TheoraConfiguration
    {
        private TheoraConfiguration(TheoraIdentification identification, XiphLacedHeaders headers)
        { Identification = identification; CommentHeader = headers.Second; SetupHeader = headers.Third; }

        public TheoraIdentification Identification { get; }
        public ReadOnlyMemory<byte> CommentHeader { get; }
        public ReadOnlyMemory<byte> SetupHeader { get; }

        public static TheoraConfiguration Read(ReadOnlyMemory<byte> configuration)
        {
            XiphLacedHeaders headers = XiphLacedHeaders.Read(configuration);
            TheoraIdentification identification = TheoraIdentification.Read(headers.First.Span);
            if (headers.Second.Length < 7 || headers.Second.Span[0] != 0x81 || !headers.Second.Span.Slice(1, 6).SequenceEqual("theora"u8) ||
                headers.Third.Length < 7 || headers.Third.Span[0] != 0x82 || !headers.Third.Span.Slice(1, 6).SequenceEqual("theora"u8))
            { throw new InvalidDataException("The Theora comment/setup signatures are absent or out of order."); }
            return new(identification, headers);
        }
    }
}
