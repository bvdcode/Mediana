// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Codecs
{
    internal class StartCodePictureReader(int streamIndex, MediaBufferAllocator allocator, StartCodeUnitClassifier classify) : IDisposable
    {
        private static ReadOnlySpan<byte> Prefix => [0, 0, 1];
        private readonly Queue<StartCodePacketTiming> timing = [];
        private WritableMediaBuffer? data;
        private long origin;
        private int start;
        private int length;
        private int scan;
        private bool sawCode;
        private bool sawPicture;

        public void Append(MediaPacket packet)
        {
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet is for a different video stream.", nameof(packet));
            }
            if (packet.Data.IsEmpty)
            {
                throw new InvalidDataException("A video fragment cannot be empty.");
            }
            EnsureCapacity(packet.Data.Length);
            long offset = checked(origin + length);
            packet.Data.Span.CopyTo(data!.Span[length..]);
            if (packet.PresentationTime is not null || packet.DecodeTime is not null || packet.Duration is not null)
            {
                timing.Enqueue(new(offset, packet.PresentationTime, packet.DecodeTime, packet.Duration, packet.IsKeyFrame));
            }
            length += packet.Data.Length;
        }

        public bool TryRead(bool complete, out MediaPacket? packet, CancellationToken token)
        {
            packet = null;
            if (data is null)
            {
                return false;
            }

            ReadOnlySpan<byte> bytes = data.Span[..length];
            while (scan <= length - Prefix.Length)
            {
                token.ThrowIfCancellationRequested();
                int relative = bytes[scan..].IndexOf(Prefix);
                if (relative < 0)
                {
                    scan = length - Prefix.Length + 1;
                    break;
                }
                int boundary = scan + relative;
                ReadOnlySpan<byte> beginning = bytes[(boundary + Prefix.Length)..];
                StartCodeUnitKind kind = beginning.IsEmpty ? StartCodeUnitKind.Incomplete : classify(beginning);
                if (kind == StartCodeUnitKind.Incomplete)
                {
                    if (complete)
                    {
                        throw new InvalidDataException("The last video start-code unit is truncated.");
                    }
                    scan = boundary;
                    return false;
                }
                if (!sawCode)
                {
                    if (bytes[start..boundary].IndexOfAnyExcept((byte)0) >= 0)
                    {
                        throw new InvalidDataException("The video elementary stream does not begin with a start code.");
                    }
                    sawCode = true;
                }
                if (sawPicture && kind is StartCodeUnitKind.Picture or StartCodeUnitKind.NextPicturePrefix)
                {
                    packet = Extract(boundary);
                    return true;
                }
                sawPicture |= kind is StartCodeUnitKind.Picture or StartCodeUnitKind.PictureContinuation;
                scan = boundary + Prefix.Length + 1;
            }
            if (!complete || length == start)
            {
                return false;
            }
            if (!sawPicture)
            {
                throw new InvalidDataException("The elementary video ends without a picture.");
            }
            packet = Extract(length);
            return true;
        }

        private MediaPacket Extract(int end)
        {
            StartCodePacketTiming stamp = default;
            long position = checked(origin + start);
            while (timing.TryPeek(out StartCodePacketTiming candidate) && candidate.Offset <= position)
            {
                stamp = timing.Dequeue();
            }

            using WritableMediaBuffer unit = allocator.Allocate(end - start);
            data!.Span[start..end].CopyTo(unit.Span);
            using MediaBuffer lease = unit.Publish();
            MediaPacket packet = new(streamIndex, lease, stamp.Pts, stamp.Dts, stamp.Duration, stamp.KeyFrame);
            start = scan = end;
            sawCode = sawPicture = false;
            if (end == length)
            {
                Dispose();
            }
            return packet;
        }

        private void EnsureCapacity(int additional)
        {
            if (start != 0)
            {
                data!.Span[start..length].CopyTo(data.Span);
                origin = checked(origin + start);
                length -= start;
                scan -= start;
                start = 0;
            }
            int size = checked(length + additional);
            int capacity = data?.Memory.Length ?? 0;
            if (size <= capacity)
            {
                return;
            }
            int growth = (int)Math.Min(int.MaxValue, Math.Max(4096L, 2L * capacity));
            WritableMediaBuffer next = allocator.Allocate(Math.Max(size, growth));
            if (data is not null)
            {
                data.Span[..length].CopyTo(next.Span);
                data.Dispose();
            }
            data = next;
        }

        public void Dispose()
        {
            data?.Dispose();
            data = null;
            timing.Clear();
            origin = 0;
            start = length = scan = 0;
            sawCode = sawPicture = false;
        }
    }
}
