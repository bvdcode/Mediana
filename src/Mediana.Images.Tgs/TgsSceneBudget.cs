// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using System.Threading;
using Mediana.Drawing;

namespace Mediana.Images.Tgs
{
    internal class TgsSceneBudget(TgsReaderOptions limits, CancellationToken token)
    {
        private int items;
        private int commands;

        internal void Visit(int depth, VectorPath? path)
        {
            token.ThrowIfCancellationRequested();
            if (++items > limits.MaximumItems || depth >= limits.MaximumDepth)
            {
                throw new InvalidDataException("Lottie expanded scene exceeds limits.");
            }
            commands = checked(commands + (path?.CommandCount ?? 0));
            if (commands > limits.MaximumPathCommands)
            {
                throw new InvalidDataException("Lottie expanded scene exceeds MaximumPathCommands.");
            }
        }
    }
}
