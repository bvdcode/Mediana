// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>Shared output accounting for one text enumeration.</summary>
    public class DocumentTextBudget
    {
        private readonly DocumentTextOptions _options;
        private int _blocks;
        private long _characters;

        public DocumentTextBudget(DocumentTextOptions? options = null)
        {
            _options = options ?? new();
            _options.Validate();
        }

        public void Consume(string text, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            cancellationToken.ThrowIfCancellationRequested();
            if (_blocks >= _options.MaximumBlocks || text.Length > _options.MaximumCharacters - _characters)
            { throw new InvalidDataException("Document text exceeds its block or character limit."); }
            _blocks++;
            _characters += text.Length;
        }
    }
}
