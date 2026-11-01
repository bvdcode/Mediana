// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaAudioReader
    {
        private static ReadOnlySpan<uint> Fields => [0xB5, 0x78B5, 0x9F, 0x6264];

        public static async ValueTask<MatroskaAudioSettings> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token)
        {
            reader.ValidateMaster(master, 0xE1);
            double frequency = 8000;
            EbmlElement? outputFrequency = null;
            ulong channels = 1;
            ulong? bitDepth = null;
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                switch (child.Id)
                {
                    case 0xB5: frequency = await reader.ReadPositiveFloatAsync(child, 8000, token).ConfigureAwait(false); break;
                    case 0x78B5: outputFrequency = child; break;
                    case 0x9F: channels = await reader.ReadUnsignedAsync(child, 1, token).ConfigureAwait(false); break;
                    case 0x6264: bitDepth = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                }
            }
            if (channels == 0 || bitDepth == 0)
            {
                throw new InvalidDataException("Matroska audio channels and declared bit depth must be positive.");
            }
            double output = frequency;
            if (outputFrequency is EbmlElement element)
            {
                output = await reader.ReadPositiveFloatAsync(element, frequency, token).ConfigureAwait(false);
            }
            return new(frequency, output, channels, bitDepth);
        }
    }
}
