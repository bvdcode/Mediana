// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    internal class AviVisualHeaderProbe : IDisposable
    {
        private const int ScanBytes = 256;
        private const int LayerBytes = 512;
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private WritableMediaBuffer? buffer;
        private byte[] openingLayer = [];
        private byte[] openingSignal = [];
        internal Mpeg4VisualConfiguration? Configuration { get; private set; }
        private bool firstPicture = true;
        internal AviVisualHeaderProbe(IRandomAccessByteSource source, MediaBufferAllocator allocator, ReadOnlyMemory<byte> configuration)
        {
            this.source = source; this.allocator = allocator;
            if (!configuration.IsEmpty)
            {
                Configuration = Mpeg4VisualConfiguration.Read(configuration.Span);
                for (int at = Mpeg4VisualStartCodes.Find(configuration.Span); at >= 0; at = Mpeg4VisualStartCodes.Find(configuration.Span, at + 4))
                {
                    int next = Mpeg4VisualStartCodes.Find(configuration.Span, at + 4);
                    ReadOnlySpan<byte> header = configuration.Span[at..(next < 0 ? configuration.Length : next)];
                    byte code = header[3];
                    if (code is >= 0x20 and <= 0x2f) { Configuration = Mpeg4VisualConfiguration.Read(header); openingLayer = header.ToArray(); }
                    else if (code == 0xb5) { _ = Mpeg4VisualSignal.Read(header[4..]); openingSignal = header.ToArray(); }
                }
            }
        }
        internal byte[] OpeningConfiguration()
        {
            byte[] result = new byte[checked(openingSignal.Length + openingLayer.Length)];
            openingSignal.CopyTo(result, 0); openingLayer.CopyTo(result, openingSignal.Length);
            return result;
        }
        internal async ValueTask<Mpeg4VopTiming> ReadAsync(AviPacketEntry entry, Mpeg4VisualClock clock, CancellationToken token)
        {
            long cursor = entry.Offset, end = checked(entry.Offset + entry.Length);
            while (end - cursor >= 4)
            {
                int length = (int)Math.Min(ScanBytes, end - cursor);
                await LoadAsync(cursor, length, token).ConfigureAwait(false);
                int at = Mpeg4VisualStartCodes.Find(buffer!.Span[..length]);
                if (at < 0) { cursor += length - 3; continue; }
                long start = cursor + at; byte code = buffer.Span[at + 3];
                if (code == 0xb6 && Configuration is not null)
                {
                    Mpeg4VisualBitReader available = new(buffer.Span.Slice(at + 4, length - at - 4));
                    if (Mpeg4VopTiming.TryRead(ref available, Configuration, out Mpeg4VopTiming timing)) { firstPicture = false; return timing; }
                }
                int read = (int)Math.Min(LayerBytes, end - start);
                await LoadAsync(start, read, token).ConfigureAwait(false);
                int next = Mpeg4VisualStartCodes.Find(buffer.Span[..read], 4), count = next < 0 ? read : next;
                if (code is >= 0x20 and <= 0x2f)
                {
                    Configuration = Mpeg4VisualConfiguration.Read(buffer.Span[..count]);
                    if (firstPicture) { openingLayer = buffer.Span[..count].ToArray(); }
                }
                else if (code == 0xb5)
                {
                    _ = Mpeg4VisualSignal.Read(buffer.Span.Slice(4, count - 4));
                    if (firstPicture) { openingSignal = buffer.Span[..count].ToArray(); }
                }
                else if (code == 0xb3) { clock.ReadGroup(buffer.Span.Slice(4, count - 4)); }
                else if (code == 0xb2 && IsPacked(buffer.Span.Slice(4, count - 4)))
                { throw new NotSupportedException("Packed DivX/Xvid AVI contains multiple VOPs per chunk and is not supported by this reader."); }
                else if (code == 0xb6)
                {
                    Mpeg4VisualConfiguration config = Configuration ?? throw new InvalidDataException("The AVI MPEG-4 Visual layer header is missing.");
                    while (true)
                    {
                        Mpeg4VisualBitReader bits = new(buffer.Span.Slice(4, read - 4));
                        if (Mpeg4VopTiming.TryRead(ref bits, config, out Mpeg4VopTiming timing)) { firstPicture = false; return timing; }
                        if (read == end - start) { throw new InvalidDataException("The AVI MPEG-4 Visual picture clock is truncated."); }
                        read = (int)Math.Min(end - start, (long)read * 2);
                        await LoadAsync(start, read, token).ConfigureAwait(false);
                    }
                }
                cursor = start + 4;
            }
            throw new InvalidDataException("The AVI MPEG-4 Visual packet contains no complete VOP header.");
        }
        private static bool IsPacked(ReadOnlySpan<byte> data)
        {
            if (!data.StartsWith("DivX"u8)) { return false; }
            int end = data.IndexOf((byte)0); if (end < 0) { end = data.Length; }
            return end > 4 && data[end - 1] == (byte)'p';
        }
        private async ValueTask LoadAsync(long offset, int length, CancellationToken token)
        {
            if (buffer is null || buffer.Memory.Length < length)
            {
                buffer?.Dispose(); buffer = null; buffer = allocator.Allocate(Math.Max(LayerBytes, length));
            }
            await source.ReadExactlyAtAsync(offset, buffer.Memory[..length], token).ConfigureAwait(false);
        }
        public void Dispose() { buffer?.Dispose(); buffer = null; }
    }
}
