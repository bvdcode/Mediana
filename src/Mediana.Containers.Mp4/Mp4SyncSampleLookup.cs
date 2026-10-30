// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SyncSampleLookup
    {
        public static int AtOrBefore(Mp4Table? sync, int sample)
        {
            if (sync is null)
            {
                return sample;
            }
            int low = 0;
            int high = sync.Count;
            uint number = (uint)sample + 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (sync.UInt32(middle) <= number)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low == 0 ? -1 : checked((int)sync.UInt32(low - 1) - 1);
        }
    }
}
