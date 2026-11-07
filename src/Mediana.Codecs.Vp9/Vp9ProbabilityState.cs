// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9ProbabilityState
    {
        private readonly Vp9ProbabilityContext?[] contexts = new Vp9ProbabilityContext?[4];
        private bool previousKey;
        internal Vp9ProbabilityContext Working { get; } = new();

        internal void Begin(Vp9FrameHeader header)
        {
            if (header.ShowExisting) { throw new InvalidOperationException("VP9 show-existing frames do not carry probabilities."); }
            if (header.IsIntra || header.ErrorResilient)
            {
                Working.Reset();
                if (header.KeyFrame || header.ErrorResilient || header.ResetContext == 3)
                { for (int i = 0; i < contexts.Length; i++) { Save(i); } }
                else if (header.ResetContext == 2) { Save(header.SelectedContext); }
            }
            Working.CopyFrom(contexts[header.Context] ?? throw new InvalidDataException("A VP9 probability context has not been initialized."));
        }

        internal void End(Vp9FrameHeader header, Vp9CoefficientCounts coefficients, Vp9SymbolCounts symbols)
        {
            if (!header.ErrorResilient && !header.ParallelDecoding)
            {
                Working.RestoreForAdaptation(contexts[header.Context] ?? throw new InvalidDataException("A VP9 probability context has not been initialized."), header.IsIntra);
                Vp9ProbabilityAdaptation.Coefficients(Working, coefficients, header.IsIntra, previousKey);
                if (!header.IsIntra) { Vp9ProbabilityAdaptation.Symbols(Working, symbols, header.HighPrecisionMotion); }
            }
            if (header.RefreshContext) { Save(header.Context); }
            previousKey = header.KeyFrame;
        }

        internal void Save(int index)
        {
            Vp9ProbabilityContext target = contexts[index] ??= new();
            target.CopyFrom(Working);
        }
    }
}
