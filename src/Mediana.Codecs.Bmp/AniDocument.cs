// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Timing;
using Mediana.Formats.Riff;

namespace Mediana.Codecs.Bmp
{
    internal class AniDocument
    {
        private readonly ReadOnlyMemory<byte> sequence;
        private readonly ReadOnlyMemory<byte> rates;
        private readonly uint defaultRate;

        public AniDocument(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> data = encoded.Span;
            if (data.Length < 12 || !data.StartsWith("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("ACON"u8) ||
                (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8 != (ulong)data.Length)
            {
                throw new InvalidDataException("Invalid ANI RIFF header or length.");
            }
            ReadOnlyMemory<byte> header = default;
            IcoImageDecoder iconProbe = new();
            bool framesSeen = false, ratesSeen = false, sequenceSeen = false;
            foreach (RiffChunk chunk in RiffChunkReader.Read(encoded[12..], cancellationToken: token))
            {
                switch (chunk.Type)
                {
                    case "anih":
                        if (!header.IsEmpty || chunk.Payload.Length != 36) { throw new InvalidDataException("Invalid or duplicate ANI header."); }
                        header = chunk.Payload;
                        break;
                    case "rate":
                        if (ratesSeen) { throw new InvalidDataException("Duplicate ANI rates."); }
                        ratesSeen = true; rates = chunk.Payload;
                        break;
                    case "seq ":
                        if (sequenceSeen) { throw new InvalidDataException("Duplicate ANI sequence."); }
                        sequenceSeen = true; sequence = chunk.Payload;
                        break;
                    case "LIST":
                        if (chunk.Payload.Length < 4) { throw new InvalidDataException("Truncated ANI list type."); }
                        if (!chunk.Payload.Span[..4].SequenceEqual("fram"u8)) { break; }
                        if (framesSeen) { throw new InvalidDataException("Duplicate ANI frame list."); }
                        framesSeen = true;
                        foreach (RiffChunk frame in RiffChunkReader.Read(chunk.Payload[4..], cancellationToken: token))
                        {
                            if (frame.Type != "icon") { continue; }
                            if (!iconProbe.CanDecode(frame.Payload.Span)) { throw new InvalidDataException("ANI frame does not contain ICO/CUR data."); }
                            Frames.Add(frame.Payload);
                        }
                        break;
                }
            }
            if (header.IsEmpty || !framesSeen || Frames.Count == 0) { throw new InvalidDataException("ANI header or frames are missing."); }
            ReadOnlySpan<byte> h = header.Span;
            uint count = Number(h, 4), steps = Number(h, 8), flags = Number(h, 32);
            if (Number(h, 0) != 36 || count != Frames.Count || steps == 0 || steps > int.MaxValue)
            { throw new InvalidDataException("Invalid ANI frame or step count."); }
            if ((flags & ~3u) != 0 || (flags & 1) == 0) { throw new NotSupportedException("ANI requires icon-form frames and known animation flags."); }
            StepCount = (int)steps;
            if ((flags & 2) != 0)
            {
                if (!sequenceSeen || sequence.Length != (long)StepCount * 4) { throw new InvalidDataException("ANI sequence length disagrees with its header."); }
            }
            else if (sequenceSeen || steps != count) { throw new InvalidDataException("ANI implicit sequence must match the frame count."); }
            if (ratesSeen && rates.Length != (long)StepCount * 4) { throw new InvalidDataException("ANI rate count disagrees with its header."); }
            defaultRate = Number(h, 28);
            DeclaredWidth = Number(h, 12); DeclaredHeight = Number(h, 16);
            for (int i = 0; i < StepCount; i++)
            {
                token.ThrowIfCancellationRequested();
                if (FrameIndex(i) >= Frames.Count) { throw new InvalidDataException("ANI sequence references a missing frame."); }
                Duration = checked(Duration + Rate(i));
            }
        }

        public List<ReadOnlyMemory<byte>> Frames { get; } = [];
        public int StepCount { get; }
        public uint DeclaredWidth { get; }
        public uint DeclaredHeight { get; }
        public long Duration { get; }
        public static TimeBase Clock { get; } = new(1, 60);
        public int FrameIndex(int step)
        {
            if (sequence.IsEmpty) { return step; }
            uint index = Number(sequence.Span, step * 4);
            if (index > int.MaxValue) { throw new InvalidDataException("ANI frame index is out of range."); }
            return (int)index;
        }
        public uint Rate(int step) => rates.IsEmpty ? defaultRate : Number(rates.Span, step * 4);
        public (int Step, long Start) Select(ImageDecodeRequest request, CancellationToken token)
        {
            if (request.PageIndex >= StepCount) { throw new ArgumentOutOfRangeException(nameof(request)); }
            long time = request.Time?.Rescale(Clock, TimestampRounding.Floor).Value ?? 0;
            bool timed = request.Time is MediaTimestamp { Value: > 0 };
            if (request.Time is MediaTimestamp requested && requested.Value != 0)
            {
                if (request.PageIndex != 0) { throw new ArgumentException("Select an ANI step or a time, not both.", nameof(request)); }
                if (requested.CompareTo(new(Duration, Clock)) >= 0) { throw new ArgumentOutOfRangeException(nameof(request)); }
            }
            long start = 0;
            for (int step = 0; step < StepCount; step++)
            {
                token.ThrowIfCancellationRequested();
                long end = start + Rate(step);
                if (timed ? time < end : step == request.PageIndex) { return (step, start); }
                start = end;
            }
            throw new InvalidDataException("ANI has no step at the requested position.");
        }
        private static uint Number(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }
}
