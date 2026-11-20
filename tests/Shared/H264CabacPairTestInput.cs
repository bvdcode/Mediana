// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264CabacPairTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (H264SliceKind kind in new[] { H264SliceKind.P, H264SliceKind.B })
            {
                for (int initialization = 0; initialization < 3; initialization++)
                {
                    for (int modes = 0; modes < 8; modes++)
                    {
                        for (int skips = 0; skips < 4; skips++)
                        {
                            foreach (int first in new[] { 0, 4, 6 })
                            {
                                for (int seed = 0; seed < 3; seed++)
                                {
                                    VerifyPair(allocator, kind, initialization, modes, skips, first, seed);
                                }
                            }
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("CABAC pair syntax retained picture storage.");
            }
        }

        private static void VerifyPair(MediaBufferAllocator allocator, H264SliceKind kind, int initialization,
            int modes, int skips, int first, int seed)
        {
            bool leftField = (modes & 1) != 0;
            bool topField = (modes & 2) != 0;
            bool topSkipped = (skips & 1) != 0;
            bool bottomSkipped = (skips & 2) != 0;
            bool inferred = first <= 4 && leftField;
            bool field = skips == 3 ? inferred : (modes & 4) != 0;
            H264SliceHeader basis = H264BidirectionalPredictionTestInput.Header();
            H264SliceHeader header = basis with
            {
                Sequence = basis.Sequence with
                {
                    Width = 32,
                    Height = 64,
                    WidthInMbs = 2,
                    HeightInMapUnits = 2,
                    FrameMbsOnly = false,
                    MbAdaptiveFrameField = true
                },
                Picture = basis.Picture with { Cabac = true },
                Kind = kind,
                Qp = 26,
                CabacInitIdc = initialization,
                FirstMacroblock = first
            };
            using H264Picture picture = new(header.Sequence, allocator, kind);
            using H264CabacMacroblockState state = new(picture, allocator, first);
            picture.Grid.SetFieldPair(0, false);
            picture.Grid.SetFieldPair(2, topField);
            picture.Grid.SetFieldPair(4, leftField);
            for (int address = 0; address < 6; address++)
            {
                state.Types[address] = (address + seed) % 3 == 0
                    ? H264CabacMacroblockState.Skipped : H264CabacMacroblockState.Inter;
            }
            H264CabacIntervalWriter writer = CreateWriter(header);
            int skipBase = kind switch
            {
                H264SliceKind.P => 11,
                H264SliceKind.B => 24,
                _ => throw new InvalidOperationException("Invalid predictive slice kind.")
            };
            int fieldContext = 70 + (first <= 4 && leftField ? 1 : 0) + (first <= 2 && topField ? 1 : 0);
            writer.Decision(topSkipped, skipBase + Context(false, inferred), 0, 0);
            if (topSkipped)
            {
                writer.Decision(bottomSkipped, skipBase + Context(true, inferred), 0, 0);
                if (!bottomSkipped)
                {
                    writer.Decision(field, fieldContext, 0, 0);
                }
            }
            else
            {
                writer.Decision(field, fieldContext, 0, 0);
                writer.Bits(0x25, 6);
                writer.Decision(bottomSkipped, skipBase + Context(true, field), 0, 0);
            }
            if (!bottomSkipped)
            {
                writer.Bits(0x16, 6);
            }
            writer.Termination(true);
            H264BitReader bits = new(writer.ToEbsp());
            H264CabacReader reader = new(ref bits, header);
            H264CabacPairSyntax syntax = new(picture, header, reader, state);
            if (syntax.ReadSkip(ref bits, 6) != topSkipped || picture.Grid.IsField(6) != field)
            {
                throw new InvalidDataException("The upper CABAC macroblock was decoded before its final pair mode.");
            }
            state.Types[6] = topSkipped ? H264CabacMacroblockState.Skipped : H264CabacMacroblockState.Inter;
            if (!topSkipped)
            {
                VerifyBypass(ref bits, reader, 0x25);
            }
            if (syntax.ReadSkip(ref bits, 7) != bottomSkipped || picture.Grid.IsField(7) != field)
            {
                throw new InvalidDataException("The lower CABAC macroblock lost its prefetched skip flag.");
            }
            if (!bottomSkipped)
            {
                VerifyBypass(ref bits, reader, 0x16);
            }
            if (!reader.Termination(ref bits))
            {
                throw new InvalidDataException("CABAC pair syntax consumed an incorrect number of bins.");
            }

            int Context(bool bottom, bool currentField)
            {
                int left = (bottom, currentField, leftField) switch
                {
                    (false, _, _) => 4,
                    (true, true, true) or (true, false, false) => 5,
                    (true, true, false) or (true, false, true) => 4
                };
                int top = (bottom, currentField, topField) switch
                {
                    (false, true, true) => 2,
                    (true, false, _) => 6,
                    (false, false, _) or (false, true, false) or (true, true, _) => 3
                };
                return Condition(left) + Condition(top);
            }

            int Condition(int address)
            {
                if (address < first || (address == 6 ? topSkipped : (address + seed) % 3 == 0))
                {
                    return 0;
                }
                return 1;
            }
        }

        private static H264CabacIntervalWriter CreateWriter(H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = new(header.Qp);
            H264CabacContext[] common = H264CabacPredictiveContexts.Create(header.Qp, header.CabacInitIdc);
            H264CabacContext[] bidirectional = H264CabacBidirectionalContexts.Create(header.Qp, header.CabacInitIdc);
            foreach (int slot in new[] { 11, 12, 13, 24, 25, 26, 70, 71, 72 })
            {
                H264CabacContext context = slot is >= 24 and <= 26 ? bidirectional[slot - 24]
                    : common[H264CabacPredictiveContexts.GetIndex(slot)];
                writer.Initialize(slot, (byte)(context.State * 2 + (context.MostProbableSymbol ? 1 : 0)));
            }
            return writer;
        }

        private static void VerifyBypass(ref H264BitReader bits, H264CabacReader reader, int expected)
        {
            int value = 0;
            for (int index = 0; index < 6; index++)
            {
                value = value * 2 + (reader.Bypass(ref bits) ? 1 : 0);
            }
            if (value != expected)
            {
                throw new InvalidDataException("CABAC pair syntax did not preserve macroblock payload order.");
            }
        }
    }
}
