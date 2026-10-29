// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Combines prediction with decoded residuals using one worker's reusable storage.
    /// </summary>
    /// <remarks>
    /// Scratch arrays and the pooled transform intermediate belong to the current worker.
    /// Completed samples are copied into picture-owned planes before the next block reuses
    /// scratch storage. Transform skip uses (d * 2^(5 + log2Size) + half) / 2^(20 - depth).
    /// </remarks>
    internal class BlockReconstructor(DecodedPicture picture, ResidualDecoder residualDecoder,
        PictureParameters parameters, QuantizationGroup quantization, ReconstructionWorkspace workspace, PicturePartitions? partitions = null)
    {
        public void Reconstruct(int x, int y, int log2Size, int component, int mode, bool coded, bool bypass)
        {
            Span<int> coefficients = workspace.Coefficients;
            Span<int> prediction = workspace.Prediction;
            SamplePlane plane = Plane(component);
            int size = 1 << log2Size;
            int depth = parameters.Sequence.BitDepth;
            Span<int> top = stackalloc int[65];
            Span<int> left = stackalloc int[65];
            top[0] = Neighbour(plane, x - 1, y - 1, x, y, component);
            left[0] = top[0];
            for (int i = 0; i < (2 * size); i++)
            {
                top[i + 1] = Neighbour(plane, x + i, y - 1, x, y, component);
                left[i + 1] = Neighbour(plane, x - 1, y + i, x, y, component);
            }

            IntraReferenceSamples.Substitute(top, left, size, depth);
            IntraPredictor.Predict(top, left, prediction, size, mode, depth, component == 0, parameters.Sequence.StrongIntraSmoothing, parameters.Sequence.ChromaFormat == 3);
            Span<int> output = workspace.Residual.Slice(0, size * size);
            output.Clear();
            if (coded)
            {
                bool skip = residualDecoder.Read(coefficients, log2Size, component, mode, bypass);
                if (bypass)
                {
                    coefficients.Slice(0, size * size).CopyTo(output);
                }
                else
                {
                    int qp = quantization.ForComponent(component);
                    CoefficientMath.Dequantize(coefficients, parameters.Scaling, component, qp, log2Size, depth);

                    if (skip)
                    {
                        int shift = 20 - depth;
                        for (int i = 0; i < output.Length; i++)
                        {
                            output[i] = ((coefficients[i] << (5 + log2Size)) + (1 << (shift - 1))) >> shift;
                        }
                    }
                    else
                    {
                        IntegerTransform.Inverse(coefficients, output, size, depth, component == 0, workspace.Transform);
                    }
                }
            }

            plane.Store(x, y, size, prediction, output);
        }

        private int Neighbour(SamplePlane plane, int x, int y, int currentX, int currentY, int component)
        {
            if (partitions is not null)
            {
                int scaleX = component == 0 ? 1 : parameters.Sequence.SubWidthC;
                int scaleY = component == 0 ? 1 : parameters.Sequence.SubHeightC;
                if (!partitions.SameSlice(x * scaleX, y * scaleY, currentX * scaleX, currentY * scaleY))
                {
                    return -1;
                }
            }
            return plane.Neighbour(x, y);
        }

        private SamplePlane Plane(int component)
        {
            switch (component)
            {
                case 0:
                    return picture.Luma;
                case 1:
                    return picture.Cb ?? throw new InvalidOperationException("Picture has no Cb plane.");
                case 2:
                    return picture.Cr ?? throw new InvalidOperationException("Picture has no Cr plane.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(component));
            }
        }
    }
}
