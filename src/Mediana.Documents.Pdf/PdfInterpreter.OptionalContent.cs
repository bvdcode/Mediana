// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private bool _contentVisible = true;

        private void BeginMarkedContent(Stack<bool> visibility, string tag, PdfValue properties, PdfValue resources)
        {
            if (visibility.Count >= storage.Budget.Options.MaximumDepth)
            {
                throw new InvalidDataException("PDF marked content exceeds its nesting limit.");
            }
            visibility.Push(_contentVisible);
            if (tag == "OC")
            {
                PdfValue membership = properties.Kind == PdfValueKind.Name ? Get(Get(resources, "Properties"), properties.Name()) : properties;
                bool enabled = optionalContent.IsVisible(membership, token, ref _operations);
                _contentVisible &= enabled;
            }
        }

        private void AddPaint(IPdfDrawCommand command)
        {
            if (_contentVisible) { _commands.Add(command); }
        }
    }
}
