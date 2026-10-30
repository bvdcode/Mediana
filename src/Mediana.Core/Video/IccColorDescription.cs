// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Original ICC profile bytes, retained without validating or applying the profile.</summary>
    public class IccColorDescription : VideoColorDescription
    {
        public IccColorDescription(ReadOnlySpan<byte> profile, bool restricted)
        {
            if (profile.IsEmpty)
            {
                throw new ArgumentException("An ICC color description requires profile bytes.", nameof(profile));
            }
            Profile = profile.ToArray();
            Restricted = restricted;
        }

        public ReadOnlyMemory<byte> Profile { get; }
        public bool Restricted { get; }

        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            throw new NotSupportedException("Color conversion requires support for the source ICC profile.");
        }
    }
}
