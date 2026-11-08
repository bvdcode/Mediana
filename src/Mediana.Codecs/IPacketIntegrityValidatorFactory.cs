// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Optional codec capability for validating integrity schemes whose coverage depends on coded syntax.</summary>
    public interface IPacketIntegrityValidatorFactory
    {
        IPacketIntegrityValidator CreateIntegrityValidator(EncodedStreamInfo stream);
    }
}
