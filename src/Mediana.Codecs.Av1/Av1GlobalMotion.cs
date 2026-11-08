// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1GlobalMotion
    {
        private readonly Av1GlobalMotionModels models;

        internal Av1GlobalMotion(Av1GlobalMotionModels models) => this.models = models;

        internal Av1GlobalMotionModel this[int reference] => models[reference - 1];

        public bool Equals(Av1GlobalMotion other)
        {
            for (int index = 0; index < Av1ReferenceIndices.Count; index++)
            {
                if (models[index] != other.models[index]) { return false; }
            }
            return true;
        }

        public override int GetHashCode()
        {
            HashCode result = new();
            for (int index = 0; index < Av1ReferenceIndices.Count; index++) { result.Add(models[index]); }
            return result.ToHashCode();
        }

        internal static Av1GlobalMotion Read(ref Av1BitReader bits, bool intra, bool highPrecision, Av1GlobalMotion previous)
        {
            if (intra) { return default; }
            Av1GlobalMotionModels models = default;
            for (int index = 0; index < Av1ReferenceIndices.Count; index++)
            {
                models[index] = Av1GlobalMotionModel.Read(ref bits, highPrecision, previous.models[index]);
            }
            return new(models);
        }
    }
}
