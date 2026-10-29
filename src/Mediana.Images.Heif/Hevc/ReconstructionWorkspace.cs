// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Reuses block scratch storage across the rows assigned to one decoding worker.
    /// </summary>
    internal class ReconstructionWorkspace : IDisposable
    {
        private const int MaximumBlockSamples = 32 * 32;
        private readonly int[] coefficients = new int[MaximumBlockSamples];
        private readonly int[] prediction = new int[MaximumBlockSamples];
        private readonly int[] residual = new int[MaximumBlockSamples];

        public Span<int> Coefficients => coefficients;
        public Span<int> Prediction => prediction;
        public Span<int> Residual => residual;
        public TransformWorkspace Transform { get; } = new TransformWorkspace();

        public void Dispose() => Transform.Dispose();
    }
}
