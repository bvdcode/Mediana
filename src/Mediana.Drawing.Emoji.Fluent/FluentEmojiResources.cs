// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Resources;

namespace Mediana.Drawing.Emoji.Fluent
{
    internal static class FluentEmojiResources
    {
        private static readonly ResourceManager Manager = new(typeof(FluentEmojiResources));
        internal static byte[] Archive => (byte[]?)Manager.GetObject("Archive")
            ?? throw new InvalidOperationException("Fluent Emoji resources are missing.");
    }
}
