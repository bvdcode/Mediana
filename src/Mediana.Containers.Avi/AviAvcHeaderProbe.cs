// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    internal class AviAvcHeaderProbe(IRandomAccessByteSource source, MediaBufferAllocator allocator) : IDisposable
    {
        private const int HeaderBytes = 4096;
        private const int FramingPrefixBytes = 5;
        private const int StartCodeBytes = 3;
        private readonly WritableMediaBuffer buffer = allocator.Allocate(
            H264HeaderLimits.DefaultMaxParameterSetBytes + StartCodeBytes);
        private long end;
        private long window = -1;
        private int available;

        public async ValueTask<H264SliceHeader> ReadAsync(AviPacketEntry entry, AviAvcParameterSets parameters,
            bool openingPacket, CancellationToken token)
        {
            window = -1;
            end = checked(entry.Offset + entry.Length);
            if (openingPacket)
            {
                ReadOnlyMemory<byte> prefix = await PeekAsync(entry.Offset, FramingPrefixBytes, token).ConfigureAwait(false);
                parameters.SelectPacketFraming(prefix.Span, entry.Length);
            }
            int lengthSize = parameters.LengthSize;
            long position = entry.Offset;
            for (int index = 0; index < H264HeaderLimits.DefaultMaxNalsPerPacket; index++)
            {
                token.ThrowIfCancellationRequested();
                long nalStart;
                int nalLength;
                if (lengthSize == 0)
                {
                    nalStart = await FindAnnexBUnitAsync(position, requireLeadingZeros: index == 0, token).ConfigureAwait(false);
                    if (nalStart == end) { break; }
                    nalLength = (int)Math.Min(HeaderBytes, end - nalStart);
                }
                else
                {
                    ReadOnlyMemory<byte> size = await PeekAsync(position, lengthSize, token).ConfigureAwait(false);
                    if (size.Length != lengthSize) { break; }
                    nalStart = position + lengthSize;
                    uint length = H264NalReader.ReadLength(size.Span, lengthSize);
                    if (length == 0 || length > end - nalStart)
                    {
                        throw new InvalidDataException("The AVI AVC NAL unit exceeds its chunk.");
                    }
                    nalLength = (int)length;
                }

                ReadOnlyMemory<byte> header = await PeekAsync(nalStart, Math.Min(nalLength, HeaderBytes), token).ConfigureAwait(false);
                int type = H264Nal.Type(header.Span);
                if (type is 1 or 5)
                {
                    if (lengthSize == 0)
                    {
                        int next = header.Span.IndexOf(H264NalReader.StartCode);
                        if (next >= 0) { header = header[..next]; }
                    }
                    H264BitReader bits = H264Nal.Payload(header.Span, type);
                    H264SliceHeader picture = H264SliceHeaderReader.Read(ref bits, parameters.Configuration, header.Span[0]);
                    if (picture.FirstMacroblock != 0)
                    {
                        throw new InvalidDataException("An AVI AVC chunk must begin with the first slice of its picture.");
                    }
                    return picture;
                }

                if (type is 7 or 8)
                {
                    position = await ReadParameterSetAsync(nalStart, nalLength, lengthSize, parameters, token).ConfigureAwait(false);
                }
                else
                {
                    position = lengthSize == 0 ? nalStart + 1 : nalStart + nalLength;
                }
                if (position == end) { break; }
            }
            throw new InvalidDataException("The AVI AVC chunk does not contain a complete opening slice header.");
        }

        private async ValueTask<long> ReadParameterSetAsync(long start, int length, int lengthSize,
            AviAvcParameterSets parameters, CancellationToken token)
        {
            int limit = H264HeaderLimits.DefaultMaxParameterSetBytes;
            if (lengthSize != 0)
            {
                if (length > limit) { throw new NotSupportedException("The AVI AVC parameter set exceeds the configured byte limit."); }
                ReadOnlyMemory<byte> payload = await PeekAsync(start, length, token).ConfigureAwait(false);
                parameters.Add(payload.Span);
                return start + length;
            }

            ReadOnlyMemory<byte> bytes = await PeekAsync(start, limit + StartCodeBytes, token, reusePartial: true).ConfigureAwait(false);
            int prefix = bytes.Span.IndexOf(H264NalReader.StartCode);
            if (prefix < 0 && start + bytes.Length != end)
            {
                bytes = await PeekAsync(start, limit + StartCodeBytes, token).ConfigureAwait(false);
                prefix = bytes.Span.IndexOf(H264NalReader.StartCode);
            }
            int count = prefix >= 0 ? prefix : bytes.Length;
            ReadOnlySpan<byte> nal = bytes.Span[..count].TrimEnd((byte)0);
            if (nal.Length > limit || prefix < 0 && start + bytes.Length != end)
            {
                throw new NotSupportedException("The AVI AVC parameter set exceeds the configured byte limit.");
            }
            parameters.Add(nal);
            return start + count;
        }

        private async ValueTask<long> FindAnnexBUnitAsync(long position, bool requireLeadingZeros, CancellationToken token)
        {
            while (position < end)
            {
                ReadOnlyMemory<byte> bytes = await PeekAsync(position, HeaderBytes, token, reusePartial: true).ConfigureAwait(false);
                int prefix = bytes.Span.IndexOf(H264NalReader.StartCode);
                if (prefix >= 0)
                {
                    if (requireLeadingZeros && bytes.Span[..prefix].IndexOfAnyExcept((byte)0) >= 0)
                    {
                        throw new InvalidDataException("The AVI AVC chunk does not begin with an Annex B start code.");
                    }
                    return position + prefix + StartCodeBytes;
                }

                if (position + bytes.Length == end)
                {
                    break;
                }
                int advance = bytes.Length - (StartCodeBytes - 1);
                if (requireLeadingZeros && bytes.Span[..advance].IndexOfAnyExcept((byte)0) >= 0)
                {
                    throw new InvalidDataException("The AVI AVC chunk does not begin with an Annex B start code.");
                }
                position += advance;
            }
            return end;
        }

        private async ValueTask<ReadOnlyMemory<byte>> PeekAsync(long position, int requested, CancellationToken token, bool reusePartial = false)
        {
            int length = (int)Math.Min(requested, end - position);
            long offset = position - window;
            if (offset >= 0 && offset < available &&
                (available - offset >= length || reusePartial && available - offset >= StartCodeBytes))
            {
                return buffer.Memory.Slice((int)offset, Math.Min(length, available - (int)offset));
            }
            int read = (int)Math.Min(Math.Max(length, HeaderBytes), end - position);
            await source.ReadExactlyAtAsync(position, buffer.Memory[..read], token).ConfigureAwait(false);
            window = position;
            available = read;
            return buffer.Memory[..length];
        }

        public void Dispose() => buffer.Dispose();
    }
}
