// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Globalization;
using System.Resources;

namespace Mediana.Images.Svg.Resources
{
    internal static class BundledResources
    {
        private static readonly ResourceManager Manager = new(typeof(BundledResources));

        internal static byte[] Read(string name)
        {
            return Manager.GetObject(name, CultureInfo.InvariantCulture) as byte[]
                ?? throw new InvalidOperationException("Bundled resource is missing: " + name);
        }
    }
}
