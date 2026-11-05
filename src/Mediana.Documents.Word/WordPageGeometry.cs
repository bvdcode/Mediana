// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordPageGeometry(double Width, double Height, double Left, double Right, double Top, double Bottom,
        double Header, double Footer);
}
