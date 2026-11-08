// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1MotionCandidates
    {
        private void ExtraCompound()
        {
            Span<Av1MotionCandidate> same = stackalloc Av1MotionCandidate[4];
            Span<Av1MotionCandidate> different = stackalloc Av1MotionCandidate[4];
            Span<int> sameCount = stackalloc int[2];
            Span<int> differentCount = stackalloc int[2];
            sameCount.Clear();
            differentCount.Clear();
            int extent = Math.Min(Math.Min(Math.Min(width, height), 16), Math.Min(sequence.MiColumns - column, sequence.MiRows - row));
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < extent;)
                {
                    int y = pass == 0 ? row - 1 : row + index;
                    int x = pass == 0 ? column + index : column - 1;
                    if (!Inside(y, x)) { break; }
                    Av1MotionBlock block = blocks[y * sequence.MiColumns + x];
                    for (int candidateList = 0; candidateList < 2; candidateList++)
                    {
                        int candidateReference = candidateList == 0 ? block.Reference : block.SecondReference;
                        if (candidateReference == 0) { continue; }
                        int motionY = candidateList == 0 ? block.Row : block.SecondRow;
                        int motionX = candidateList == 0 ? block.Column : block.SecondColumn;
                        for (int list = 0; list < 2; list++)
                        {
                            int targetReference = list == 0 ? reference : secondReference;
                            if (candidateReference == targetReference && sameCount[list] < 2)
                            {
                                same[list * 2 + sameCount[list]++] = new(motionY, motionX, 2);
                            }
                            else if (differentCount[list] < 2)
                            {
                                int sign = Backward(candidateReference) == Backward(targetReference) ? 1 : -1;
                                different[list * 2 + differentCount[list]++] = new(motionY * sign, motionX * sign, 2);
                            }
                        }
                    }
                    int length = pass == 0 ? block.Width : block.Height;
                    if (length == 0) { break; }
                    index += length;
                }
            }
            Span<Av1MotionCandidate> combined = stackalloc Av1MotionCandidate[2];
            combined.Clear();
            for (int list = 0; list < 2; list++)
            {
                for (int index = 0; index < 2; index++)
                {
                    Av1MotionCandidate vector = index < sameCount[list] ? same[list * 2 + index]
                        : index - sameCount[list] < differentCount[list] ? different[list * 2 + index - sameCount[list]] : default;
                    combined[index] = list == 0 ? combined[index] with { Row = vector.Row, Column = vector.Column, Weight = 2 }
                        : combined[index] with { SecondRow = vector.Row, SecondColumn = vector.Column };
                }
            }
            if (count == 1)
            {
                Av1MotionCandidate first = candidates[0];
                int selected = first.Row == combined[0].Row && first.Column == combined[0].Column &&
                    first.SecondRow == combined[0].SecondRow && first.SecondColumn == combined[0].SecondColumn ? 1 : 0;
                candidates[count++] = combined[selected];
            }
            else
            {
                candidates[count++] = combined[0];
                candidates[count++] = combined[1];
            }
        }
    }
}
