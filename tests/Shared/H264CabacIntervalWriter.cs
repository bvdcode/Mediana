// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    /// <summary>Subdivides exact intervals without the decoder's streaming renormalization.</summary>
    /// <remarks>Normative probability and transition constants are shared; interval coordinates remain arbitrary precision.</remarks>
    internal class H264CabacIntervalWriter(int qp)
    {
        private const int CodeBits = 8192;
        private readonly Dictionary<int, (int State, bool Mps)> contexts = new();
        private BigInteger low;
        private BigInteger unit = BigInteger.One << (CodeBits - 9);
        private BigInteger width = 510 * (BigInteger.One << (CodeBits - 9));
        private int range = 510;

        public void Initialize(int slot, byte packed) => contexts.Add(slot, (packed >> 1, (packed & 1) != 0));

        public void Decision(bool value, int slot, sbyte slope, sbyte intercept)
        {
            if (!contexts.TryGetValue(slot, out (int State, bool Mps) context))
            {
                int pre = Math.Clamp((int)Math.Floor(slope * Math.Clamp(qp, 0, 51) / 16.0) + intercept, 1, 126);
                context = pre < 64 ? (63 - pre, false) : (pre - 64, true);
            }
            int lps = H264CabacTables.LpsRange(context.State, range);
            int mpsRange = range - lps;
            if (value == context.Mps)
            {
                range = mpsRange;
                context.State = H264CabacTables.NextMps(context.State);
            }
            else
            {
                low += unit * mpsRange;
                range = lps;
                if (context.State == 0)
                {
                    context.Mps = !context.Mps;
                }
                context.State = H264CabacTables.NextLps(context.State);
            }
            width = range * unit;
            Normalize();
            contexts[slot] = context;
        }

        public void Bits(uint value, int count)
        {
            for (int index = count - 1; index >= 0; index--)
            {
                width /= 2;
                unit /= 2;
                if ((value & (1u << index)) != 0)
                {
                    low += width;
                }
            }
        }

        public void Termination(bool value)
        {
            if (value)
            {
                low += (range - 2) * unit;
                width = 2 * unit;
            }
            else
            {
                range -= 2;
                width = range * unit;
                Normalize();
            }
        }

        public byte[] ToEbsp()
        {
            byte[] point = (low + width / 2).ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] rbsp = new byte[CodeBits / 8];
            point.CopyTo(rbsp.AsSpan(rbsp.Length - point.Length));
            List<byte> ebsp = new();
            int zeros = 0;
            foreach (byte value in rbsp)
            {
                if (zeros == 2 && value <= 3)
                {
                    ebsp.Add(3);
                    zeros = 0;
                }
                ebsp.Add(value);
                zeros = value == 0 ? zeros + 1 : 0;
            }
            return ebsp.ToArray();
        }

        private void Normalize()
        {
            while (range < 256)
            {
                range *= 2;
                unit /= 2;
            }
        }
    }
}
