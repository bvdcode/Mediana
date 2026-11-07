// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class VopTimingTests
    {
        [Theory]
        [InlineData("bframes")]
        [InlineData("fractional")]
        [InlineData("quarter")]
        public void PrefixClockMatchesCompleteHeaderAndDoesNotConsumeTruncatedInput(string name)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".m4v"));
            Mpeg4VisualConfiguration configuration = Mpeg4VisualConfiguration.Read(data);
            for (int at = Mpeg4VisualStartCodes.Find(data); at >= 0; at = Mpeg4VisualStartCodes.Find(data, at + 4))
            {
                if (data[at + 3] != 0xb6) { continue; }
                Mpeg4VisualBitReader whole = new(data.AsSpan(at + 4));
                Mpeg4VopHeader header = Mpeg4VopHeader.Read(ref whole, configuration);
                Mpeg4VopTiming expected = new(header.Type, header.ModuloTimeBase, header.TimeIncrement, header.IsCoded);
                int prefixLength = 0;
                while (true)
                {
                    Mpeg4VisualBitReader prefix = new(data.AsSpan(at + 4, prefixLength++));
                    if (Mpeg4VopTiming.TryRead(ref prefix, configuration, out Mpeg4VopTiming actual))
                    { Assert.Equal(expected, actual); break; }
                    Assert.Equal(0, prefix.BitPosition);
                }
            }
        }
    }
}
