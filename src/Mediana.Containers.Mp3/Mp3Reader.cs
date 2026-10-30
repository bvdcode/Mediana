// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Mp3
{
    /// <summary>Forward-only Layer III frames and ID3 metadata; borrows its source at the file start.
    /// Packets include complete frame headers and share bit-reservoir dependencies. Coded timestamps
    /// preserve delay/padding. No PCM decoding, CRC checking, gapless trimming or resynchronization.</summary>
    public class Mp3Reader : IContainerReader
    {
        private readonly IByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly Mp3ReadOptions options;
        private readonly WritableMediaBuffer scratch;
        private MediaBuffer? tag;
        private MediaBuffer? pending;
        private IReadOnlyList<Id3Picture> pictures = [];
        private long position;
        private long frames;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        private Mp3Reader(IByteSource source, MediaBufferAllocator allocator, Mp3ReadOptions options)
        {
            this.source = source;
            this.allocator = allocator;
            this.options = options;
            scratch = allocator.Allocate(128);
        }

        public Mp3FrameHeader FirstFrame { get; private set; } = null!;
        public ContainerInfo Info { get; private set; } = null!;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];
        public IReadOnlyList<Id3TextFrame> TextFrames { get; private set; } = [];
        public IReadOnlyList<Id3PictureInfo> Pictures { get; private set; } = [];
        public Mp3VbrInfo? VbrInfo { get; private set; }
        public Id3v1Tag? TrailingTag { get; private set; }
        public long AudioOffset { get; private set; }
        /// <summary>Coded sample extent, declared by Xing/Info or known after EOF. Includes codec delay/padding.</summary>
        public MediaDuration? CodedDuration => ended ? Duration(frames)
            : VbrInfo?.FrameCount is uint count ? Duration(count) : null;

        public static async ValueTask<Mp3Reader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            Mp3ReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new Mp3ReadOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Mp3Reader reader = new(source, allocator, options);
            bool opened = false;
            try
            {
                await reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
                reader.Info = new ContainerInfo("mp3", reader.CodedDuration);
                opened = true;
                return reader;
            }
            finally
            {
                if (!opened)
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            await ReadExactlyAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
            if (scratch.Memory.Span[..3].SequenceEqual("ID3"u8))
            {
                await ReadTagAsync(cancellationToken).ConfigureAwait(false);
                await ReadExactlyAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(scratch.Memory.Span[..4]);
            using MediaBuffer first = await ReadFrameAsync(header, cancellationToken).ConfigureAwait(false);
            VbrInfo = Mp3VbrInfo.Read(first.Memory.Span, header);
            if (VbrInfo is null)
            {
                pending = first.Retain();
            }
            else
            {
                if (VbrInfo.FrameCount > options.MaxAudioFrames)
                {
                    throw new NotSupportedException("The declared MP3 frame count exceeds its configured limit.");
                }
                await ReadExactlyAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
                Mp3FrameHeader next = Mp3FrameHeader.Read(scratch.Memory.Span[..4]);
                ValidateFormat(header, next);
                header = next;
                pending = await ReadFrameAsync(header, cancellationToken).ConfigureAwait(false);
            }
            FirstFrame = header;
            AudioOffset = position - header.FrameBytes;
            Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, Mp3FrameHeader.CodecId,
                new TimeBase(1, header.SampleRate), pending.Memory.Span[..4], codedDuration: VbrInfo?.FrameCount is uint count
                    ? new MediaDuration((long)count * header.SamplesPerFrame, new TimeBase(1, header.SampleRate)) : null) });
        }

        private async ValueTask ReadTagAsync(CancellationToken cancellationToken)
        {
            await ReadExactlyAsync(scratch.Memory.Slice(4, 6), cancellationToken).ConfigureAwait(false);
            Id3TagHeader header = Id3TagHeader.Read(scratch.Memory.Span[..10], options);
            using WritableMediaBuffer body = allocator.Allocate(Math.Max(1, header.BodyBytes));
            await ReadExactlyAsync(body.Memory[..header.BodyBytes], cancellationToken).ConfigureAwait(false);
            Id3TagData data = Id3TagParser.Read(body.Memory.Span[..header.BodyBytes], header.Version, header.Flags, options, cancellationToken);
            if (header.HasFooter)
            {
                await ReadExactlyAsync(scratch.Memory.Slice(10, 10), cancellationToken).ConfigureAwait(false);
                if (!scratch.Memory.Span.Slice(10, 3).SequenceEqual("3DI"u8)
                    || !scratch.Memory.Span.Slice(13, 7).SequenceEqual(scratch.Memory.Span.Slice(3, 7)))
                {
                    throw new InvalidDataException("The ID3 footer disagrees with its header.");
                }
            }
            TextFrames = data.TextFrames;
            pictures = data.Pictures;
            Pictures = Array.AsReadOnly(pictures.Select(picture => picture.Info).ToArray());
            if (pictures.Count != 0)
            {
                tag = body.Publish();
            }
        }

        private async ValueTask<MediaBuffer> ReadFrameAsync(Mp3FrameHeader header, CancellationToken cancellationToken)
        {
            if (header.FrameBytes > options.MaxFrameBytes)
            {
                throw new NotSupportedException("The MP3 frame exceeds its configured byte limit.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(header.FrameBytes);
            scratch.Memory[..4].CopyTo(writable.Memory);
            await ReadExactlyAsync(writable.Memory[4..], cancellationToken).ConfigureAwait(false);
            return writable.Publish();
        }

        private static void ValidateFormat(Mp3FrameHeader original, Mp3FrameHeader next)
        {
            if (original.Version != next.Version || original.SampleRate != next.SampleRate || original.ChannelCount != next.ChannelCount)
            {
                throw new NotSupportedException("Changing MP3 version, sample rate or channel count is not supported.");
            }
        }

        private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await source.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            position = checked(position + buffer.Length);
        }

        private async ValueTask<bool> ReadHeaderAsync(CancellationToken cancellationToken)
        {
            int count = await source.ReadAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (count is < 0 or > 4)
            {
                throw new InvalidDataException("The MP3 source returned an invalid read count.");
            }
            if (count == 0)
            {
                return false;
            }
            position = checked(position + count);
            await ReadExactlyAsync(scratch.Memory.Slice(count, 4 - count), cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (!ended && pending is null)
                {
                    await ReadNextAsync(cancellationToken).ConfigureAwait(false);
                }
                if (ended)
                {
                    succeeded = true;
                    return null;
                }
                MediaTimestamp timestamp = new(checked(frames * FirstFrame.SamplesPerFrame), Streams[0].TimeBase);
                MediaPacket packet = new(0, pending ?? throw new InvalidOperationException("An MP3 frame is missing."),
                    timestamp, timestamp, Duration(1));
                pending.Dispose();
                pending = null;
                frames++;
                succeeded = true;
                return packet;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    ReleaseStorage();
                }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask ReadNextAsync(CancellationToken cancellationToken)
        {
            bool found = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (found && scratch.Memory.Span[..3].SequenceEqual("TAG"u8))
            {
                await ReadExactlyAsync(scratch.Memory[4..], cancellationToken).ConfigureAwait(false);
                TrailingTag = Id3v1Tag.Read(scratch.Memory.Span);
                if (await ReadHeaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("The ID3v1 trailer must be the end of the source.");
                }
                found = false;
            }
            if (!found)
            {
                if (VbrInfo?.FrameCount is uint count && frames != count)
                {
                    throw new InvalidDataException("The MP3 audio frame count disagrees with Xing/Info.");
                }
                ended = true;
                return;
            }
            if (frames == options.MaxAudioFrames || (VbrInfo?.FrameCount is uint declared && frames >= declared))
            {
                throw new InvalidDataException("The MP3 frame count exceeds its declared or configured limit.");
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(scratch.Memory.Span[..4]);
            ValidateFormat(FirstFrame, header);
            pending = await ReadFrameAsync(header, cancellationToken).ConfigureAwait(false);
        }

        public MediaBuffer ReadPicture(int index)
        {
            Enter();
            try
            {
                RequireActive();
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, pictures.Count);
                Id3Picture picture = pictures[index];
                return (tag ?? throw new InvalidOperationException("ID3 picture storage is missing."))
                    .Slice(picture.Offset, picture.Info.ByteLength);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private MediaDuration Duration(long count) => new(checked(count * FirstFrame.SamplesPerFrame), Streams[0].TimeBase);

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The MP3 reader has failed and must be disposed.");
            }
        }

        private void ReleaseStorage()
        {
            scratch.Dispose();
            tag?.Dispose();
            pending?.Dispose();
            pending = null;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MP3 reader operations are not supported.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                ReleaseStorage();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
