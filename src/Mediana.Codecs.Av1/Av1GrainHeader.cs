// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1GrainHeader
    {
        internal static Av1GrainParameters? Read(ref Av1BitReader bits, Av1FrameContext context, Av1ReferencePictures references)
        {
            if (!context.Geometry.FilmGrain || !(context.ShowFrame || context.Showable) || !bits.Flag()) { return null; }
            ushort seed = (ushort)bits.Read(16);
            bool update = context.Type switch
            {
                Av1FrameType.Inter => bits.Flag(),
                Av1FrameType.Key or Av1FrameType.IntraOnly or Av1FrameType.Switch => true,
                _ => throw new InvalidDataException("The AV1 frame type is invalid.")
            };
            if (update) { return Av1GrainParameters.ReadUpdate(ref bits, seed, context.Geometry.Monochrome); }
            int index = bits.Read(3);
            bool selected = false;
            for (int reference = 0; reference < Av1ReferenceIndices.Count; reference++)
            {
                if (context.Inter.References[reference] == index) { selected = true; break; }
            }
            if (!selected) { throw new InvalidDataException("The AV1 film-grain reference is not a selected prediction reference."); }
            Av1FrameHeader previous = references.Get(index).Header;
            if (!previous.Geometry.FilmGrain) { throw new InvalidDataException("The AV1 reference has no film-grain parameter state."); }
            return previous.Grain is Av1GrainParameters grain ? grain with { Seed = seed } : null;
        }
    }
}
