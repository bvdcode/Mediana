// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;

namespace Mediana.Cli
{
    internal static class CommandTextResources
    {
        private static readonly Lazy<Task<TextOptions>> Resources = new(LoadAsync);

        internal static async ValueTask<TextOptions> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Resources.Value.WaitAsync(cancellationToken);
        }

        internal static Core.MediaResourceSet Shared { get; } = Core.MediaResourceSet.Empty.WithAsync<TextOptions>(GetAsync);

        private static Task<TextOptions> LoadAsync() => TextResources.LoadSystemAsync(new()
        {
            SubstituteMissingFonts = true,
            InlineGraphicProvider = new FluentEmojiProvider()
        });
    }
}
