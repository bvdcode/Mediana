// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Container header declarations, independent of a reader implementation.
    /// Declared duration is not a scan result or a guarantee of decoded or gapless playback length.</summary>
    public class ContainerInfo
    {
        public ContainerInfo(string formatId, MediaDuration? declaredDuration = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
            declaredDuration?.TimeBase.Validate();
            FormatId = formatId;
            DeclaredDuration = declaredDuration;
        }

        /// <summary>Stable container-family identifier, independent of the filename and implementation ID.</summary>
        public string FormatId { get; }
        /// <summary>Duration declared by container metadata, including its original time base. Null means unknown.</summary>
        public MediaDuration? DeclaredDuration { get; }
    }
}
