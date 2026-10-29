// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        private H264PictureIdentity?[] identitiesL0 = [];
        private H264PictureIdentity?[] identitiesL1 = [];

        public H264PictureIdentity Identity { get; } = new();

        public void CaptureReferences(IReadOnlyList<H264ReferencePicture?> l0, IReadOnlyList<H264ReferencePicture?> l1)
        {
            identitiesL0 = Capture(l0);
            identitiesL1 = Capture(l1);
        }

        public H264PictureIdentity ReferenceIdentity(H264ReferenceList list, int index)
        {
            H264PictureIdentity?[] identities = list switch
            {
                H264ReferenceList.L0 => identitiesL0,
                H264ReferenceList.L1 => identitiesL1,
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
