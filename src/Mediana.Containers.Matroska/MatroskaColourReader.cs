// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaColourReader
    {
        private static ReadOnlySpan<uint> Fields => [0x55B1, 0x55B2, 0x55B3, 0x55B4, 0x55B5, 0x55B6,
            0x55B7, 0x55B8, 0x55B9, 0x55BA, 0x55BB, 0x55BC, 0x55BD, 0x55D0];

        public static async ValueTask<MatroskaColourSettings> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token)
        {
            reader.ValidateMaster(master, 0x55B0);
            MatroskaColourSettings settings = new();
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                if (child.Id == 0x55D0)
                {
                    settings = settings with { HasMasteringMetadata = true };
                    continue;
                }
                ulong defaultValue = child.Id switch
                {
                    0x55B1 or 0x55BA or 0x55BB => 2,
                    _ => 0
                };
                ulong value = await reader.ReadUnsignedAsync(child, defaultValue, token).ConfigureAwait(false);
                settings = child.Id switch
                {
                    0x55B1 => settings with { MatrixCoefficients = value },
                    0x55B2 => settings with { BitsPerChannel = value },
                    0x55B3 => settings with { ChromaSubsamplingHorz = value },
                    0x55B4 => settings with { ChromaSubsamplingVert = value },
                    0x55B5 => settings with { CbSubsamplingHorz = value },
                    0x55B6 => settings with { CbSubsamplingVert = value },
                    0x55B7 => settings with { ChromaSitingHorz = value },
                    0x55B8 => settings with { ChromaSitingVert = value },
                    0x55B9 => settings with { Range = value },
                    0x55BA => settings with { TransferCharacteristics = value },
                    0x55BB => settings with { Primaries = value },
                    0x55BC => settings with { MaxCll = value },
                    0x55BD => settings with { MaxFall = value },
                    _ => throw new InvalidOperationException("The Matroska Colour field set is inconsistent.")
                };
            }
            return settings;
        }
    }
}
