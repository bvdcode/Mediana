// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp3
{
    public class Id3TextFrame
    {
        internal Id3TextFrame(string frameId, string? description, string[] values)
        {
            FrameId = frameId;
            Description = description;
            Values = Array.AsReadOnly(values);
        }

        public string FrameId { get; }
        public string? Description { get; }
        public IReadOnlyList<string> Values { get; }
    }
}
