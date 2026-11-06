// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Formats.Metadata
{
    /// <summary>Inspects TIFF/Exif directories without decoding samples or interpreting vendor MakerNotes.
    /// Native fractions, text and binary values remain distinct. Calendar text is not assigned a time zone.</summary>
    public class ExifMetadataReader
    {
        private ExifMetadataReader() { }
        public static MediaMetadata Read(ReadOnlyMemory<byte> tiff, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Read(new TiffDirectoryReader(tiff), cancellationToken);
        }
        internal static MediaMetadata Read(TiffDirectoryReader reader, CancellationToken token)
        {
            Stack<(ulong Offset, string Scope, bool Exit)> pending = new();
            pending.Push((reader.FirstOffset, "IFD0", false));
            HashSet<ulong> active = [], completed = [];
            List<MediaMetadata> directories = [];
            while (pending.TryPop(out (ulong Offset, string Scope, bool Exit) directory))
            {
                token.ThrowIfCancellationRequested();
                if (directory.Offset == 0) { continue; }
                if (directory.Exit) { active.Remove(directory.Offset); completed.Add(directory.Offset); continue; }
                if (active.Contains(directory.Offset)) { throw new InvalidDataException("Cyclic Exif directory reference."); }
                if (completed.Contains(directory.Offset)) { continue; }
                active.Add(directory.Offset);
                pending.Push((directory.Offset, directory.Scope, true));
                TiffDirectory source = reader.Read(directory.Offset, token);
                List<(ulong Offset, string Scope)> children = [];
                List<MediaProperty> properties = [];
                foreach (TiffField field in source.Fields.Values)
                {
                    properties.Add(new("exif", ExifTagNames.Get(directory.Scope, field.Tag), field.Values(token)));
                    if (field.Tag is 330 or 34665 or 34853 or 40965)
                    {
                        if (field.Type is not (4 or 13 or 16 or 18) || (field.Tag != 330 && field.Count != 1))
                        { throw new InvalidDataException("Invalid Exif directory pointer."); }
                        for (int index = 0; (ulong)index < field.Count; index++)
                        {
                            string scope = field.Tag switch
                            {
                                330 => directory.Scope + "/SubIFD/" + index,
                                34665 => "Exif",
                                34853 => "GPS",
                                40965 => "Interoperability",
                                _ => throw new InvalidDataException("Invalid Exif directory tag.")
                            };
                            children.Add((field.Unsigned(index), scope));
                        }
                    }
                }
                directories.Add(new(directory.Scope, properties));
                children.Add((source.NextOffset, directory.Scope + "/next"));
                for (int index = children.Count - 1; index >= 0; index--) { pending.Push((children[index].Offset, children[index].Scope, false)); }
            }
            return new("Exif", children: directories);
        }
    }
}
