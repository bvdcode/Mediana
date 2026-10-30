// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Flac
{
    /// <summary>Forward-only native FLAC metadata and compressed byte chunks; borrows its source at the file signature.
    /// Packet boundaries are arbitrary and carry no frame timestamps. Audio framing, CRCs, PCM and MD5 verification
    /// belong to a decoder. Pictures retain their original metadata backing storage within the caller's budget.</summary>
    public class FlacReader : IContainerReader
    {
        private const int ScratchBytes = 4096;
        private readonly IByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly FlacReadOptions options;
        private readonly WritableMediaBuffer scratch;
        private readonly List<FlacPicture> pictures = [];
        private long position;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        private FlacReader(IByteSource source, MediaBufferAllocator allocator, FlacReadOptions options)
        {
            this.source = source;
            this.allocator = allocator;
            this.options = options;
            scratch = allocator.Allocate(ScratchBytes);
        }

        public FlacConfiguration Configuration { get; private set; } = null!;
        public ContainerInfo Info { get; private set; } = null!;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];
        public IReadOnlyList<FlacComment> Comments { get; private set; } = [];
        public IReadOnlyList<FlacPictureInfo> Pictures { get; private set; } = [];
        public string? Vendor { get; private set; }
        public long AudioOffset { get; private set; }
        public MediaDuration? Duration => Configuration.TotalSamples == 0 ? null
            : new MediaDuration(Configuration.TotalSamples, Streams[0].TimeBase);

        public static async ValueTask<FlacReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            FlacReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new FlacReadOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            FlacReader reader = new(source, allocator, options);
            bool opened = false;
            try
            {
                await reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
                reader.Info = new ContainerInfo("flac", reader.Duration);
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
            if (!scratch.Memory.Span[..4].SequenceEqual("fLaC"u8))
            {
                throw new NotSupportedException("The source is not a native FLAC file.");
            }
            List<FlacComment> comments = [];
            HashSet<int> unique = [];
            int blocks = 0;
            bool last;
            do
            {
                if (blocks == options.MaxMetadataBlocks)
                {
                    throw new NotSupportedException("The FLAC metadata block count exceeds its configured limit.");
                }
                await ReadExactlyAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
                int type = scratch.Memory.Span[0] & 0x7f;
                last = (scratch.Memory.Span[0] & 0x80) != 0;
                int length = scratch.Memory.Span[1] << 16 | scratch.Memory.Span[2] << 8 | scratch.Memory.Span[3];
                if ((blocks == 0 && type != 0) || type == 127 || (type is 0 or 3 or 4 or 5 && !unique.Add(type)))
                {
                    throw new InvalidDataException("FLAC requires STREAMINFO first and unique STREAMINFO, seek table, comments and cuesheet blocks.");
                }
                if (length > options.MaxMetadataBytes - position)
                {
                    throw new NotSupportedException("The FLAC metadata extent exceeds its configured byte limit.");
                }
                if (source.Length is long size && length > size - position)
                {
                    throw new InvalidDataException("The FLAC metadata extent exceeds the source.");
                }
                blocks++;
                switch (type)
                {
                    case 0:
                        if (length != FlacConfiguration.StreamInfoBytes)
                        {
                            throw new InvalidDataException("FLAC STREAMINFO must contain exactly 34 bytes.");
                        }
                        await ReadExactlyAsync(scratch.Memory[..length], cancellationToken).ConfigureAwait(false);
                        Configuration = FlacConfiguration.ReadStreamInfo(scratch.Memory.Span[..length]);
                        Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, FlacConfiguration.CodecId,
                            new TimeBase(1, Configuration.SampleRate), scratch.Memory.Span[..length],
                            codedDuration: Configuration.TotalSamples == 0 ? null : new MediaDuration(Configuration.TotalSamples,
                                new TimeBase(1, Configuration.SampleRate))) });
                        break;
                    case 1:
                    case 2:
                    case 5:
                        if (type == 2 && length < 4)
                        {
                            throw new InvalidDataException("A FLAC application block requires its four-byte identifier.");
                        }
                        await SkipAsync(length, type == 1, cancellationToken).ConfigureAwait(false);
                        break;
                    case 3:
                    case 4:
                    case 6:
                        await ReadMetadataAsync(type, length, comments, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException("The FLAC metadata block type is reserved.");
                }
            } while (!last);
            Comments = comments.AsReadOnly();
            Pictures = Array.AsReadOnly(pictures.Select(picture => picture.Info).ToArray());
            AudioOffset = position;
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async ValueTask ReadMetadataAsync(int type, int length, List<FlacComment> comments, CancellationToken cancellationToken)
        {
            if (type == 6 && pictures.Count == options.MaxPictures)
            {
                throw new NotSupportedException("The FLAC picture count exceeds its configured limit.");
            }
            using WritableMediaBuffer block = allocator.Allocate(Math.Max(1, length));
            await ReadExactlyAsync(block.Memory[..length], cancellationToken).ConfigureAwait(false);
            switch (type)
            {
                case 3:
                    FlacMetadataParser.ValidateSeekTable(block.Memory.Span[..length], cancellationToken);
                    break;
                case 4:
                    Vendor = FlacMetadataParser.ReadComments(block.Memory.Span[..length], comments, options, Configuration, cancellationToken);
                    break;
                case 6:
                    FlacPictureInfo info = FlacMetadataParser.ReadPicture(block.Memory.Span[..length], options, out int offset);
                    if (info.PictureType is 1 or 2 && pictures.Any(picture => picture.Info.PictureType == info.PictureType))
                    {
                        throw new InvalidDataException("A FLAC file cannot contain duplicate icon picture types.");
                    }
                    pictures.Add(new FlacPicture(info, block.Publish(), offset));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await source.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            position = checked(position + buffer.Length);
        }

        private async ValueTask SkipAsync(int bytes, bool padding, CancellationToken cancellationToken)
        {
            while (bytes != 0)
            {
                int count = Math.Min(bytes, scratch.Memory.Length);
                await ReadExactlyAsync(scratch.Memory[..count], cancellationToken).ConfigureAwait(false);
                if (padding && scratch.Memory.Span[..count].ContainsAnyExcept((byte)0))
                {
                    throw new InvalidDataException("FLAC padding bytes must be zero.");
                }
                bytes -= count;
            }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (ended)
                {
                    succeeded = true;
                    return null;
                }
                using WritableMediaBuffer writable = allocator.Allocate(options.PacketBytes);
                int count = await source.ReadAsync(writable.Memory, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (count < 0 || count > options.PacketBytes)
                {
                    throw new InvalidDataException("The FLAC source returned an invalid read length.");
                }
                if (count == 0)
                {
                    ended = true;
                    succeeded = true;
                    return null;
                }
                using MediaBuffer complete = writable.Publish();
                using MediaBuffer buffer = complete.Slice(0, count);
                MediaPacket packet = new(0, buffer);
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

        public MediaBuffer ReadPicture(int index)
        {
            Enter();
            try
            {
                RequireActive();
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, pictures.Count);
                FlacPicture picture = pictures[index];
                return picture.Block.Slice(picture.DataOffset, picture.Info.ByteLength);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The FLAC reader has failed and must be disposed.");
            }
        }

        private void ReleaseStorage()
        {
            scratch.Dispose();
            foreach (FlacPicture picture in pictures)
            {
                picture.Block.Dispose();
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent FLAC reader operations are not supported.");
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
