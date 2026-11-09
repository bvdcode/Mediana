// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Codecs.Hevc
{
    /// <summary>
    /// Owns the arithmetic engine and adaptive contexts of one I/P/B slice decoding substream.
    /// </summary>
    internal class HevcCabacReader
    {
        private CabacDecoder decoder;
        private CabacContext[]? wavefrontContexts;
        private bool hasWavefrontContexts;
        private readonly CabacContext[] contexts;
        private readonly ReadOnlyMemory<int> offsets;
        private readonly int initializationType;

        public HevcCabacReader(BitReader reader, int sliceQp, int initializationType = 0)
        {
            this.initializationType = initializationType;
            offsets = HevcCabacInitialization.GetOffsets(initializationType);
            contexts = new CabacContext[offsets.Span[^1]];
            decoder = new CabacDecoder(reader);
            HevcCabacInitialization.Initialize(contexts, initializationType, sliceQp);
        }

        public void StoreWavefrontContexts()
        {
            wavefrontContexts ??= new CabacContext[contexts.Length];
            contexts.CopyTo(wavefrontContexts, 0);
            hasWavefrontContexts = true;
        }

        public void StartTile(BitReader reader, int sliceQp)
        {
            decoder = new CabacDecoder(reader);
            HevcCabacInitialization.Initialize(contexts, initializationType, sliceQp);
            hasWavefrontContexts = false;
        }

        public void StartWavefrontRow(BitReader reader, int sliceQp)
        {
            decoder = new CabacDecoder(reader);
            if (!hasWavefrontContexts) { HevcCabacInitialization.Initialize(contexts, initializationType, sliceQp); }
            else { wavefrontContexts!.CopyTo(contexts, 0); }
        }

        public void CopyWavefrontContexts(HevcCabacReader previous)
        {
            if (initializationType != previous.initializationType) { throw new InvalidOperationException("HEVC WPP rows must use the same context initialization."); }
            if (!previous.hasWavefrontContexts) { throw new InvalidOperationException("The preceding WPP row has not stored its contexts."); }
            CabacContext[] snapshot = previous.wavefrontContexts ??
                throw new InvalidOperationException("The preceding WPP row has not stored its contexts.");
            snapshot.CopyTo(contexts, 0);
        }

        public bool Read(CabacElement element, int index = 0)
        {
            ReadOnlySpan<int> groups = offsets.Span;
            int group = (int)element, start = groups[group];
            return decoder.ReadBin(ref contexts.AsSpan(start, groups[group + 1] - start)[index]);
        }

        public bool Bypass() => decoder.ReadBypass();
        public int Bits(int count) => unchecked((int)decoder.ReadBypassBits(count));
        public bool Termination() => decoder.ReadTermination();

        public int Unary(int maximum)
        {
            int value = 0;
            while ((value < maximum) && Bypass())
            {
                value++;
            }
            return value;
        }

        public int ExpGolomb(int order)
        {
            int value = 0;
            while (Bypass())
            {
                if (order >= 16)
                {
                    throw new InvalidDataException("CABAC Exp-Golomb value exceeds coefficient precision.");
                }
                value += 1 << order++;
            }

            return value + Bits(order);
        }

        public int RemainingLevel(int rice)
        {
            if ((rice < 0) || (rice > 4))
            {
                throw new ArgumentOutOfRangeException(nameof(rice));
            }
            int prefix = Unary(4);
            if (prefix == 4)
            {
                return (4 << rice) + ExpGolomb(rice + 1);
            }
            return (prefix << rice) + Bits(rice);
        }

        public int QpDelta()
        {
            int magnitude = 0;
            while ((magnitude < 5) && Read(CabacElement.QpDelta, magnitude == 0 ? 0 : 1))
            {
                magnitude++;
            }
            if (magnitude == 5)
            {
                magnitude += ExpGolomb(0);
            }
            if (magnitude > 38)
            {
                throw new InvalidDataException("Luma QP delta exceeds the 8/10-bit range.");
            }
            return (magnitude != 0) && Bypass() ? -magnitude : magnitude;
        }
    }
}
