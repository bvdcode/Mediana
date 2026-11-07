// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Original ICC profile bytes, retained without validating or applying the profile.</summary>
    public class IccColorDescription : VideoColorDescription
    {
        public IccColorDescription(ReadOnlySpan<byte> profile, bool restricted)
            : this((ReadOnlyMemory<byte>)profile.ToArray(), restricted)
        {
        }

        private IccColorDescription(ReadOnlyMemory<byte> profile, bool restricted)
        {
            if (profile.IsEmpty)
            {
                throw new ArgumentException("An ICC color description requires profile bytes.", nameof(profile));
            }
            Profile = profile;
            Restricted = restricted;
        }

        /// <summary>Copies ordered borrowed profile fragments into one owned allocation.</summary>
        public static IccColorDescription FromSegments(IReadOnlyList<ReadOnlyMemory<byte>> segments, bool restricted)
        {
            ArgumentNullException.ThrowIfNull(segments);
            int length = 0;
            foreach (ReadOnlyMemory<byte> segment in segments)
            {
                length = checked(length + segment.Length);
            }
            byte[] profile = new byte[length];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in segments)
            {
                segment.Span.CopyTo(profile.AsSpan(offset));
                offset += segment.Length;
            }
            return new IccColorDescription((ReadOnlyMemory<byte>)profile, restricted);
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
