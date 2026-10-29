// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Containers.Mp3
{
    /// <summary>Trailing ID3v1 fields interpreted as Latin-1, available after reading EOF.
    /// Kept separate from ID3v2 text; the format declares no character encoding.</summary>
    public record Id3v1Tag(string Title, string Artist, string Album, string Year, string Comment, byte? Track, byte Genre)
    {
        internal static Id3v1Tag Read(ReadOnlySpan<byte> data)
        {
            if (data.Length != 128 || !data[..3].SequenceEqual("TAG"u8))
            {
                throw new InvalidDataException("An ID3v1 tag must contain the TAG signature and 128 bytes.");
            }
            bool trackPresent = data[125] == 0 && data[126] != 0;
            return new Id3v1Tag(Text(data.Slice(3, 30)), Text(data.Slice(33, 30)), Text(data.Slice(63, 30)),
                Text(data.Slice(93, 4)), Text(data.Slice(97, trackPresent ? 28 : 30)),
                trackPresent ? data[126] : null, data[127]);
        }

        private static string Text(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data).TrimEnd('\0', ' ');
    }
}
