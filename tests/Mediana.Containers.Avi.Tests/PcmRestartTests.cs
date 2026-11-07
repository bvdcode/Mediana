// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Formats;

namespace Mediana.Containers.Avi.Tests
{
    public class PcmRestartTests
    {
        [Fact]
        public void RestartRequiresTheSelectedStreamAndCompletePcmBlocks()
        {
            byte[] configuration = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(configuration, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(configuration.AsSpan(2), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(configuration.AsSpan(4), 48000);
            BinaryPrimitives.WriteUInt32LittleEndian(configuration.AsSpan(8), 192000);
            BinaryPrimitives.WriteUInt16LittleEndian(configuration.AsSpan(12), 4);
            BinaryPrimitives.WriteUInt16LittleEndian(configuration.AsSpan(14), 16);
            EncodedStreamInfo stream = new(7, MediaKind.Audio, "pcm", StreamFormats.WavePcm, new(1, 48000), configuration);
            IRandomAccessInspector inspector = new PcmDecoderFactory().CreateRandomAccessInspector(stream);
            using WritableMediaBuffer storage = new MediaBufferAllocator(12).Allocate(12);
            using MediaBuffer bytes = storage.Publish();
            using MediaPacket complete = new(7, bytes);
            using MediaBuffer partial = bytes.Slice(0, 11);
            using MediaPacket incomplete = new(7, partial);
            using MediaPacket wrong = new(0, bytes);
            Assert.True(inspector.IsRandomAccess(complete));
            Assert.False(inspector.IsRandomAccess(incomplete));
            Assert.Throws<ArgumentException>(() => inspector.IsRandomAccess(wrong));
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => inspector.IsRandomAccess(complete, canceled.Token));
        }
    }
}
