// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264MotionPredictionTests
    {
        [Fact]
        public void MedianAndUniqueReferenceMatchUseAvailableNeighbours()
        {
            using H264Picture picture = CreatePicture();
            picture.SetMotion(H264ReferenceList.L0, 12, 16, 4, 4, 0, 4, 20);
            picture.SetMotion(H264ReferenceList.L0, 16, 12, 4, 4, 1, 12, 4);
            picture.SetMotion(H264ReferenceList.L0, 32, 12, 4, 4, 0, 8, 12);
            Assert.Equal((8, 12), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 16, 0, 0));
            Assert.Equal((12, 4), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 16, 1, 0));
            Assert.Equal((8, 12), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 16, 2, 0));
        }

        [Fact]
        public void MissingTopRightUsesTopLeftAndTopRowUsesTheLeftVector()
        {
            using H264Picture picture = CreatePicture();
            picture.SetMotion(H264ReferenceList.L0, 12, 0, 4, 4, 0, -7, 9);
            Assert.Equal((-7, 9), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 0, 16, 16, 0, 0));
            picture.SetMotion(H264ReferenceList.L0, 12, 12, 4, 4, 2, 31, -17);
            Assert.Equal((31, -17), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 16, 2, 0));
        }

        [Fact]
        public void SplitMacroblocksPreferTheSpecifiedEdgeNeighbour()
        {
            using H264Picture picture = CreatePicture();
            picture.SetMotion(H264ReferenceList.L0, 12, 16, 4, 4, 0, 4, 20);
            picture.SetMotion(H264ReferenceList.L0, 16, 12, 4, 4, 0, 12, 4);
            picture.SetMotion(H264ReferenceList.L0, 24, 12, 4, 4, 0, 8, 12);
            Assert.Equal((12, 4), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 8, 0, 0));
            Assert.Equal((4, 20), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 16, 8, 0, 1));
            Assert.Equal((4, 20), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 8, 16, 0, 0));
            Assert.Equal((8, 12), H264MotionPrediction.Predict(picture, H264ReferenceList.L0, 16, 16, 8, 16, 0, 1));
        }

        [Fact]
        public void SkipUsesZeroAtBordersOrBesideAnUnmovedReferenceZero()
        {
            using H264Picture picture = CreatePicture();
            Assert.Equal((0, 0), H264MotionPrediction.Skip(picture, 16, 0));
            picture.SetMotion(H264ReferenceList.L0, 12, 16, 4, 4, 0, 4, 20);
            picture.SetMotion(H264ReferenceList.L0, 16, 12, 4, 4, 0, 0, 0);
            Assert.Equal((0, 0), H264MotionPrediction.Skip(picture, 16, 16));
            picture.SetMotion(H264ReferenceList.L0, 16, 12, 4, 4, 0, 12, 4);
            picture.SetMotion(H264ReferenceList.L0, 32, 12, 4, 4, 0, 8, 12);
            Assert.Equal((8, 12), H264MotionPrediction.Skip(picture, 16, 16));
        }

        [Fact]
        public void ConstrainedIntraPredictionExcludesReconstructedInterNeighbours()
        {
            using H264Picture picture = CreatePicture();
            picture.SetMotion(H264ReferenceList.L0, 0, 16, 16, 16, 0, 0, 0);
            picture.SetMotion(H264ReferenceList.L0, 16, 0, 16, 16, 0, 0, 0);
            for (int index = 0; index < picture.Modes.Length; index++)
            {
                if (picture.Modes[index] == (H264Picture.InterMode | 128))
                {
                    picture.Modes[index] = H264Picture.InterMode;
                }
            }
            picture.Plane(0).Fill(80);
            picture.Plane(1).Fill(80);
            byte[] luma = new byte[256];
            picture.PredictMacroblock(0, 16, 16, 2, luma);
            Assert.All(luma, value => Assert.Equal(80, value));
            picture.PredictMacroblock(0, 16, 16, 2, luma, constrained: true);
            Assert.All(luma, value => Assert.Equal(128, value));
            byte[] small = new byte[16];
            picture.PredictLuma4x4(16, 16, 2, small, constrained: true);
            Assert.All(small, value => Assert.Equal(128, value));
            byte[] chroma = new byte[64];
            picture.PredictMacroblock(1, 8, 8, 0, chroma, constrained: true);
            Assert.All(chroma, value => Assert.Equal(128, value));
        }

        private static H264Picture CreatePicture() => new(new H264SequenceParameterSet
        {
            WidthInMbs = 3,
            HeightInMapUnits = 3,
            FrameMbsOnly = true,
            Width = 48,
            Height = 48
        }, new MediaBufferAllocator(1024 * 1024), H264SliceKind.P);
    }
}
