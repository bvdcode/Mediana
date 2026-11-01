// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal class CommandConfirmation(bool assumeYes, bool interactive, TextReader input, TextWriter diagnostics)
    {
        public bool CanConfirm => assumeYes || interactive;

        public async Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assumeYes)
            {
                return true;
            }
            if (!interactive)
            {
                return false;
            }
            await diagnostics.WriteAsync(question + " [y/N] ");
            await diagnostics.FlushAsync(cancellationToken);
            string? answer = (await input.ReadLineAsync(cancellationToken))?.Trim();
            bool confirmed = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
            if (!confirmed)
            {
                await diagnostics.WriteLineAsync("Operation cancelled.");
            }
            return confirmed;
        }
    }
}
