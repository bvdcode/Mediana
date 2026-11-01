// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4ParseBudget(Mp4ReadOptions options, CancellationToken cancellationToken)
    {
        private int boxes;
        private int metadataValues;
        private int metadataTextBytes;

        public void TakeMetadataValue()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (metadataValues == options.MaxMetadataValues)
            {
                throw new NotSupportedException("The MP4 metadata value count exceeds the configured limit.");
            }
            metadataValues++;
        }

        public void TakeMetadataText(int bytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes > options.MaxMetadataTextBytes - metadataTextBytes)
            {
                throw new NotSupportedException("Decoded MP4 metadata text exceeds the configured byte limit.");
            }
            metadataTextBytes += bytes;
        }

        public void TakeBox()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (boxes == options.MaxBoxes)
            {
                throw new NotSupportedException("The MP4 box count exceeds the configured limit.");
            }
            boxes++;
        }
    }
}
