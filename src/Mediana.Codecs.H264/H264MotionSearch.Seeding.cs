// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal ref partial struct H264MotionSearch
    {
        private void Seed(H264Picture target, int range)
        {
            if (range == 0 || (best == 0 && motionX == predicted.X && motionY == predicted.Y)) { return; }
            Span<(int X, int Y)> seeds = stackalloc (int, int)[9];
            seeds[0] = (0, 0);
            seeds[1] = (Math.Clamp(predicted.X / 4, -range, range) * 4,
                Math.Clamp(predicted.Y / 4, -range, range) * 4);
            int count = 2;
            int x = position.X, y = position.Y;
            AddSeed(target.MotionAt(H264ReferenceList.L0, x - 1, y), range, seeds, ref count);
            AddSeed(target.MotionAt(H264ReferenceList.L0, x, y - 1), range, seeds, ref count);
            AddSeed(target.MotionAt(H264ReferenceList.L0, x + 16, y - 1), range, seeds, ref count);
            AddSeed(target.MotionAt(H264ReferenceList.L0, x - 1, y - 1), range, seeds, ref count);
            AddSeed(reference.MotionAt(H264ReferenceList.L0, x, y), range, seeds, ref count);
            AddSeed(reference.MotionAt(H264ReferenceList.L0, x + 16, y), range, seeds, ref count);
            AddSeed(reference.MotionAt(H264ReferenceList.L0, x, y + 16), range, seeds, ref count);
            for (int index = 2; index < count; index++) { Evaluate(seeds[index].X, seeds[index].Y); }
        }

        private static void AddSeed(H264MotionSample sample, int range, Span<(int X, int Y)> seeds, ref int count)
        {
            if (!sample.Available || sample.ReferenceIndex != 0) { return; }
            int x = Math.Clamp(sample.X / 4, -range, range) * 4;
            int y = Math.Clamp(sample.Y / 4, -range, range) * 4;
            for (int index = 0; index < count; index++)
            {
                if (seeds[index] == (x, y)) { return; }
            }
            seeds[count++] = (x, y);
        }
    }
}
