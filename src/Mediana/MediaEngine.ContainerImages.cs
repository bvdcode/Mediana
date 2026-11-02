// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core.Memory;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Searches exposed picture/attachment capabilities in the explicitly supplied order.
        /// Uses registered image signatures, not names or declared media types. External references are not read.
        /// Null means no registered image was recognized among the inspected exposed entries, not proof of absence.
        /// Prefixes share the container allocator and are released before reading the selected owned payload.
        /// Borrows the single-consumer session; preserves packet/source cursors. Per-read session failure rules apply.
        /// Does not decode, retrieve external resources, select a video frame or retry after image decode failure.</summary>
        public async ValueTask<ContainerImage?> ReadContainerImageAsync(ContainerSession session, ContainerImageSearchOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            int probeLength = GetImageProbeLength(options.Image, cancellationToken);
            session.ValidateActive();
            (ContainerImageCandidate Candidate, string DecoderId)? selected = null;
            WritableMediaBuffer? prefix = null;
            int visited = 0;
            try
            {
                foreach (ContainerImageCandidate candidate in ContainerImageCandidate.Enumerate(session, options.Sources))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (visited == options.MaxCandidates)
                    {
                        throw new NotSupportedException($"Container image search exceeds the configured limit of {options.MaxCandidates} declarations.");
                    }
                    visited++;
                    switch (candidate.DataKind)
                    {
                        case PictureDataKind.ExternalReference:
                            continue;
                        case PictureDataKind.Embedded:
                            break;
                        default:
                            throw new InvalidDataException("The container returned an unknown picture data kind.");
                    }
                    prefix ??= session.Allocator.Allocate(probeLength);
                    int count = await candidate.ReadPrefixAsync(session, prefix.Memory, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    IImageDecoder? decoder = FindImageDecoder(prefix.Memory.Span[..count]);
                    if (decoder is null)
                    {
                        continue;
                    }
                    if (candidate.ByteLength > options.Image.MaxInputBytes)
                    {
                        throw new NotSupportedException("The recognized container image exceeds the configured input byte limit.");
                    }
                    selected = (candidate, decoder.ImplementationId);
                    break;
                }
            }
            finally
            {
                prefix?.Dispose();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (selected is null)
            {
                return null;
            }
            ContainerImageCandidate image = selected.Value.Candidate;
            MediaBuffer data = await image.ReadAsync(session, cancellationToken).ConfigureAwait(false);
            bool transferred = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (data.Memory.Length != image.ByteLength)
                {
                    throw new InvalidDataException("Container image bytes differ from their declared extent.");
                }
                ContainerImage result = new(image.Source, image.Index, selected.Value.DecoderId, data);
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred)
                {
                    data.Dispose();
                }
            }
        }
    }
}
