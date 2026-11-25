// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Threading.Channels;
using Mediana.Codecs;
using Mediana.Core;

namespace Mediana.Execution
{
    internal class BufferedMediaEncodeSource<TFrame> : IEncodedPacketSource where TFrame : class, IDisposable
    {
        private const int PreparedFrameCapacity = 2;
        private readonly Func<CancellationToken, ValueTask<TFrame?>> read;
        private readonly MediaEncodeSource<TFrame> encoder;
        private readonly Channel<TFrame> frames = CreateQueue<TFrame>(PreparedFrameCapacity);
        private readonly Channel<MediaPacket> packets = CreateQueue<MediaPacket>(1);
        private CancellationTokenSource? lifetime;
        private Task? workers;
        private bool disposed;
        private bool faulted;

        public BufferedMediaEncodeSource(Func<CancellationToken, ValueTask<TFrame?>> read, IMediaEncoder<TFrame> encoder)
        {
            this.read = read;
            this.encoder = new(ReadPreparedFrameAsync, encoder);
        }

        public EncodedStreamInfo Stream => encoder.Stream;

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The buffered encoder has failed.");
            }

            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workers is null)
                {
                    lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    CancellationToken token = lifetime.Token;
                    Task preparing = Task.Run(() => PublishAsync(read, frames.Writer, token));
                    Task encoding = Task.Run(() => PublishAsync(encoder.ReadPacketAsync, packets.Writer, token));
                    workers = Task.WhenAll(preparing, encoding);
                }
                MediaPacket? packet = await ReadAsync(packets.Reader, cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return packet;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    await StopAsync().ConfigureAwait(false);
                }
            }
        }

        private ValueTask<TFrame?> ReadPreparedFrameAsync(CancellationToken token) => ReadAsync(frames.Reader, token);

        private static async ValueTask<T?> ReadAsync<T>(ChannelReader<T> reader, CancellationToken token) where T : class
        {
            if (await reader.WaitToReadAsync(token).ConfigureAwait(false) && reader.TryRead(out T? value))
            {
                return value;
            }
            return null;
        }

        private static async Task PublishAsync<T>(Func<CancellationToken, ValueTask<T?>> read, ChannelWriter<T> writer,
            CancellationToken token) where T : class, IDisposable
        {
            Task producing = FillAsync(read, writer, token);
            await producing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Exception? error = producing.IsCanceled ? new OperationCanceledException(token) : producing.Exception?.InnerException;
            writer.TryComplete(error);
        }

        private static async Task FillAsync<T>(Func<CancellationToken, ValueTask<T?>> read, ChannelWriter<T> writer,
            CancellationToken token) where T : class, IDisposable
        {
            while (await writer.WaitToWriteAsync(token).ConfigureAwait(false))
            {
                T? item = await read(token).ConfigureAwait(false);
                if (item is null)
                {
                    return;
                }
                if (!writer.TryWrite(item))
                {
                    item.Dispose();
                    throw new InvalidOperationException("The buffered encoder queue closed during a write.");
                }
            }
        }

        private static Channel<T> CreateQueue<T>(int capacity) => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true
        });

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await encoder.DisposeAsync().ConfigureAwait(false);
                GC.SuppressFinalize(this);
            }
        }

        private async ValueTask StopAsync()
        {
            using CancellationTokenSource? cancellation = lifetime;
            lifetime = null;
            if (cancellation is null)
            {
                return;
            }
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (workers is not null)
                    {
                        await workers.ConfigureAwait(false);
                    }
                }
                finally
                {
                    try { Release(frames.Reader); }
                    finally { Release(packets.Reader); }
                }
            }
        }

        private static void Release<T>(ChannelReader<T> reader) where T : class, IDisposable
        {
            if (reader.TryRead(out T? value))
            {
                try { value.Dispose(); }
                finally { Release(reader); }
            }
        }
    }
}
