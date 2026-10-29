// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Mediana.Codecs.Jbig2
{
    /// <summary>Decoded pages in page-association order.</summary>
    public class Jbig2Document
    {
        internal Jbig2Document(Jbig2Page[] pages)
        {
            Pages = Array.AsReadOnly(pages);
        }

        /// <summary>The decoded pages.</summary>
        public IReadOnlyList<Jbig2Page> Pages { get; }
    }
}
