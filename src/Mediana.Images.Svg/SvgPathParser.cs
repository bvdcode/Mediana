// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Mediana.Images.Svg
{
    /// <summary>Parses SVG path data, including elliptical arcs and smooth/relative commands.</summary>
    public static class SvgPathParser
    {
        /// <summary>Creates a mutable vector path from an SVG d attribute.</summary>
        public static VectorPath Parse(string data, int maximumCommands = 1_000_000, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCommands);
            return new SvgPathDataParser(data, maximumCommands, cancellationToken).Run();
        }


    }
}
