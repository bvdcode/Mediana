// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        private readonly List<H264PictureReferences> referenceSlices = [];

        public H264PictureIdentity Identity { get; } = new();

        public void CaptureReferences(IReadOnlyList<H264ReferencePicture?> l0, IReadOnlyList<H264ReferencePicture?> l1)
        {
            referenceSlices.Add(new H264PictureReferences(FirstMacroblock, Capture(l0), Capture(l1)));
        }

        public H264PictureIdentity ReferenceIdentity(H264ReferenceList list, int index, int macroblock = 0)
        {
            int low = 0;
            int high = referenceSlices.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (referenceSlices[middle].FirstMacroblock <= macroblock)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            if (low == 0)
            {
                throw new InvalidDataException("The H.264 co-located macroblock has no reference list.");
            }
            H264PictureReferences slice = referenceSlices[low - 1];
            H264PictureIdentity?[] identities = list switch
            {
                H264ReferenceList.L0 => slice.L0,
                H264ReferenceList.L1 => slice.L1,
                _ => throw new ArgumentOutOfRangeException(nameof(list))
            };
            if ((uint)index >= (uint)identities.Length || identities[index] is not H264PictureIdentity identity)
            {
                throw new InvalidDataException("The H.264 co-located motion has no reference identity.");
            }
            return identity;
        }

        private static H264PictureIdentity?[] Capture(IReadOnlyList<H264ReferencePicture?> references)
        {
            if (references.Count > 32)
            {
                throw new InvalidDataException("The H.264 reference list exceeds its supported size.");
            }
            H264PictureIdentity?[] result = new H264PictureIdentity?[references.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = references[index]?.Picture.Identity;
            }
            return result;
        }
    }
}
