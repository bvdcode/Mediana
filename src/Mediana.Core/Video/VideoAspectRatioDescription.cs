// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>A source pixel aspect ratio, either explicitly declared or defined by its enclosing format.</summary>
    public class VideoAspectRatioDescription
    {
        public VideoAspectRatioDescription(SampleAspectRatio value, bool isDefault = false)
        {
            value.Validate();
            Value = value;
            IsDefault = isDefault;
        }

        public SampleAspectRatio Value { get; }
        /// <summary>A format default applies only when the codec does not declare an aspect ratio.</summary>
        public bool IsDefault { get; }

        /// <summary>Explicit source declarations override codec declarations; format defaults do not.</summary>
        public SampleAspectRatio Resolve(SampleAspectRatio? codecAspectRatio)
        {
            codecAspectRatio?.Validate();
            if (IsDefault && codecAspectRatio is SampleAspectRatio declared)
            {
                return declared;
            }
            return Value;
        }
    }
}
