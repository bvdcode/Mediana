// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal record SceneRegion(float X, float Y, float Width, float Height, bool ObjectBox)
    {
        internal VectorPath Path((float X, float Y, float Width, float Height) box)
        {
            float x = X, y = Y, width = Width, height = Height;
            if (ObjectBox)
            {
                x = box.X + (x * box.Width);
                y = box.Y + (y * box.Height);
                width *= box.Width;
                height *= box.Height;
            }
            return width <= 0 || height <= 0 ? new VectorPath() : new VectorPath().Rectangle(x, y, width, height);
        }
    }
}
