// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264ReferenceBuffer
    {
        private static void SeparateFields(List<H264ReferencePicture> list)
        {
            for (int index = 0; index < list.Count; index++)
            {
                H264ReferencePicture reference = list[index];
                if (reference.Structure != H264PictureStructure.Frame)
                {
                    continue;
                }
                list[index] = reference.GetField(H264PictureStructure.TopField);
                list.Insert(++index, reference.GetField(H264PictureStructure.BottomField));
            }
        }

        private static IReadOnlyList<H264ReferencePicture> GetFrames(IReadOnlyList<H264ReferencePicture> list)
        {
            if (list.All(value => value.Structure == H264PictureStructure.Frame))
            {
                return list;
            }
            List<H264ReferencePicture> frames = [];
            foreach (H264ReferencePicture reference in list)
            {
                switch (reference.Structure)
                {
                    case H264PictureStructure.Frame:
                        frames.Add(reference);
                        break;
                    case H264PictureStructure.TopField:
                        H264ReferencePicture? bottom = list.FirstOrDefault(value => value.FrameIdentity == reference.FrameIdentity &&
                            value.Structure == H264PictureStructure.BottomField && value.LongTermIndex == reference.LongTermIndex);
                        if (bottom is not null)
                        {
                            frames.Add(H264ReferencePicture.GetFrame(reference, bottom));
                        }
                        break;
                    case H264PictureStructure.BottomField:
                        break;
                    default:
                        throw new InvalidDataException("Invalid H.264 reference picture structure.");
                }
            }
            return frames;
        }

        private void ReleaseUnusedPictures(IReadOnlyList<H264ReferencePicture> remaining)
        {
            for (int index = 0; index < references.Count; index++)
            {
                H264Picture picture = references[index].Picture;
                if (remaining.Any(value => ReferenceEquals(value.Picture, picture)))
                {
                    continue;
                }
                bool first = true;
                for (int previous = 0; previous < index; previous++)
                {
                    if (ReferenceEquals(references[previous].Picture, picture))
                    {
                        first = false;
                        break;
                    }
                }
                if (first)
                {
                    picture.Dispose();
                }
            }
        }
    }
}
