// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class StreamDurationTests
    {
        [Theory]
        [InlineData(MediaKind.Audio)]
        [InlineData(MediaKind.Video)]
        public void DurationIsOptionalAndIndependentOfThePacketClock(MediaKind kind)
        {
            EncodedStreamInfo unknown = new(0, kind, "test", new TimeBase(1, 90000));
            Assert.Null(unknown.CodedDuration);
            MediaDuration duration = new(48000, new TimeBase(1, 48000));
            EncodedStreamInfo known = new(0, kind, "test", new TimeBase(1, 90000), codedDuration: duration);
            Assert.Equal(duration, known.CodedDuration);
            Assert.Throws<ArgumentException>(() => new EncodedStreamInfo(0, kind, "test", new TimeBase(1, 1), codedDuration: default(MediaDuration)));
        }
    }
}
