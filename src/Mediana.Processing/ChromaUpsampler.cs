// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Immutable separable reconstruction of unsigned integer chroma planes with explicit sample siting.</summary>
    /// <remarks>Plans are reusable and thread-safe. Row processing uses caller-owned workspace and allocates no buffers.
    /// Edges extend the nearest source sample. Bicubic uses Catmull-Rom; Lanczos has radius three.
    /// Intermediate values retain filter overshoot; only the final sample is rounded and clipped.</remarks>
    public class ChromaUpsampler
    {
        private readonly ChromaUpsamplingAxis horizontal;
        private readonly ChromaUpsamplingAxis vertical;

        public ChromaUpsampler(int sourceWidth, int sourceHeight, int horizontalFactor, int verticalFactor,
            ChromaLocation location, ChromaUpsamplingFilter filter)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
            if ((horizontalFactor != 1 && horizontalFactor != 2) || (verticalFactor != 1 && verticalFactor != 2))
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalFactor), "Chroma factors must be one or two.");
            }
            _ = ReconstructionKernel.Taps(filter);
            (int x, int y) = ChromaSamplePosition.InHalfLumaSamples(location);
            horizontal = new(sourceWidth, horizontalFactor, x, filter);
            vertical = new(sourceHeight, verticalFactor, y, filter);
        }

        public int OutputWidth => horizontal.Length;
        public int OutputHeight => vertical.Length;
        public int WorkspaceLength => horizontal.SourceLength;

        /// <summary>Reconstructs one full-resolution row. Source stride and all dimensions are measured in samples.</summary>
        public void CopyRow<T>(ReadOnlySpan<T> source, int stride, int y, Span<T> output, Span<float> workspace,
            int maximum, CancellationToken cancellationToken = default)
            where T : unmanaged, IBinaryInteger<T>, IUnsignedNumber<T>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stride < horizontal.SourceLength || source.Length < checked((vertical.SourceLength - 1) * stride + horizontal.SourceLength)
                || output.Length < OutputWidth || workspace.Length < WorkspaceLength)
            {
                throw new ArgumentException("Chroma storage does not cover the plane, row or workspace.");
            }
            if (source.Overlaps(output))
            {
                throw new ArgumentException("Chroma reconstruction requires separate source and output storage.", nameof(output));
            }
            if ((uint)y >= (uint)OutputHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, ushort.MaxValue);
            _ = T.CreateChecked(maximum);
            Span<float> row = workspace[..WorkspaceLength];
            row.Clear();
            int top = vertical.First(y);
            ReadOnlySpan<float> verticalWeights = vertical.Weights(y);
            for (int tap = 0; tap < vertical.Taps; tap++)
            {
                int sourceY = Math.Clamp(top + tap, 0, vertical.SourceLength - 1);
                ReadOnlySpan<T> samples = source.Slice(sourceY * stride, WorkspaceLength);
                float weight = verticalWeights[tap];
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] += float.CreateChecked(samples[x]) * weight;
                }
            }
            for (int x = 0; x < OutputWidth; x++)
            {
                if ((x & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                int left = horizontal.First(x);
                ReadOnlySpan<float> weights = horizontal.Weights(x);
                float value = 0;
                for (int tap = 0; tap < horizontal.Taps; tap++)
                {
                    value += row[Math.Clamp(left + tap, 0, row.Length - 1)] * weights[tap];
                }
                output[x] = T.CreateChecked(Math.Clamp((int)MathF.Floor(value + 0.5f), 0, maximum));
            }
        }
    }
}
