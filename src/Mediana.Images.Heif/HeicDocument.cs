// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mediana.Images.Heif.Container;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Owns an immutable copy of a HEIC file and its parsed image metadata.
    /// </summary>
    /// <remarks>
    /// Repeated and concurrent decode calls use independent workers and return independent
    /// pixel arrays. Loading never retains caller buffers or streams. No native codec is used.
    /// </remarks>
    public class HeicDocument
    {
        private readonly HeifContainer container;
        private readonly HeicDecodeOptions options;
        private readonly IReadOnlyDictionary<uint, ImageProperties> metadata;

        private HeicDocument(byte[] ownedInput, HeicDecodeOptions options)
        {
            this.options = options;
            container = new HeifContainerReader(options).Read(ownedInput);
            Dictionary<uint, ImageProperties> images = new Dictionary<uint, ImageProperties>();
            foreach (HeifItem item in container.Items.Values)
            {
                if ((item.Type == HeifBox.Code("hvc1")) || (item.Type == HeifBox.Code("grid")) || (item.Type == HeifBox.Code("iden")) ||
                    (item.Type == HeifBox.Code("iovl")))
                {
                    images.Add(item.Id, ImageProperties.Read(container, item, options));
                }
            }

            metadata = new ReadOnlyDictionary<uint, ImageProperties>(images);
        }

        /// <summary>
        /// Gets the primary image item identifier from the file.
        /// </summary>
        public uint PrimaryItemId => container.PrimaryItemId;

        /// <summary>
        /// Copies a complete encoded HEIC file and parses its bounded metadata.
        /// </summary>
        /// <param name="encodedFile">The complete file; it may be reused or changed after this method returns.</param>
        /// <param name="options">Resource limits, or null to use the default limits.</param>
        /// <returns>An immutable document that owns its encoded input.</returns>
        public static HeicDocument Load(ReadOnlyMemory<byte> encodedFile, HeicDecodeOptions? options = null)
        {
            HeicDecodeOptions limits = options ?? HeicDecodeOptions.Default;
            if (encodedFile.Length > limits.MaxInputBytes)
            {
                throw new InvalidDataException("Input byte limit exceeded.");
            }

            return new HeicDocument(encodedFile.ToArray(), limits);
        }

        /// <summary>
        /// Asynchronously reads a complete HEIC file from the current stream position without closing the stream.
        /// </summary>
        /// <param name="stream">A readable stream; ownership remains with the caller.</param>
        /// <param name="options">Resource limits, or null to use the default limits.</param>
        /// <param name="cancellationToken">Cancels reading before document creation.</param>
        /// <returns>A document that owns its encoded input independently of the stream.</returns>
        public static async Task<HeicDocument> LoadAsync(Stream stream, HeicDecodeOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream), "A readable HEIC input stream is required.");
            }

            HeicDecodeOptions limits = options ?? HeicDecodeOptions.Default;
            const int CopyBufferBytes = 81920;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(CopyBufferBytes, limits.MaxInputBytes));
            try
            {
                using MemoryStream encoded = new MemoryStream();
                while (true)
                {
                    int remaining = limits.MaxInputBytes - (int)encoded.Length;
                    int count = remaining == 0 ? 1 : Math.Min(remaining, buffer.Length);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (read > remaining)
                    {
                        throw new InvalidDataException("Input byte limit exceeded.");
                    }

                    await encoded.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new HeicDocument(encoded.ToArray(), limits);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Decodes the primary image into a new, caller-owned RGBA8 image.
        /// </summary>
        /// <returns>The image after container transformations and auxiliary alpha composition.</returns>
        public Task<RgbaImage> DecodePrimaryAsync(CancellationToken cancellationToken = default) => DecodeImageAsync(PrimaryItemId, cancellationToken);

        /// <summary>
        /// Decodes one image item with fresh picture and worker storage.
        /// </summary>
        /// <param name="itemId">An image item identifier belonging to this document.</param>
        /// <returns>A new RGBA8 image whose pixels remain valid independently of the document.</returns>
        public Task<RgbaImage> DecodeImageAsync(uint itemId, CancellationToken cancellationToken = default)
        {
            if (!container.Items.ContainsKey(itemId))
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), "The document has no item with this identifier.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ImageItemDecoder(container, metadata, options, cancellationToken).DecodeAsync(itemId);
        }
    }
}
