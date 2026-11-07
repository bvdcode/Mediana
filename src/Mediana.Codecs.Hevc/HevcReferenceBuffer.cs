// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    /// <summary>Owns reconstructed references and applies the signalled short/long-term picture set.</summary>
    internal class HevcReferenceBuffer : IDisposable
    {
        private readonly Dictionary<int, HevcReferencePicture> pictures = [];
        private bool disposed;
        public int Count => pictures.Count;

        public HevcReferenceSet Select(HevcSliceReferences syntax, int order, int log2Order)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfLessThan(log2Order, 4);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(log2Order, 16);
            int modulus = 1 << log2Order;
            if ((order & (modulus - 1)) != syntax.PictureOrderCountLsb)
            { throw new InvalidDataException("HEVC reference selection disagrees with picture order."); }
            HashSet<int> retained = [];
            List<HevcReferenceEntry> before = [], after = [], longTerm = [];
            foreach (HevcShortTermReference reference in syntax.ShortTerm.Pictures)
            {
                long target = order + reference.Delta;
                if (target < int.MinValue || target > int.MaxValue || target == order)
                { throw new InvalidDataException("Invalid HEVC short-term picture order."); }
                int key = (int)target;
                if (!retained.Add(key)) { throw new InvalidDataException("Duplicate HEVC reference picture."); }
                if (pictures.TryGetValue(key, out HevcReferencePicture? picture))
                {
                    if (reference.UsedByCurrentPicture)
                    {
                        if (reference.Delta < 0) { before.Add(new(picture, false)); }
                        else { after.Add(new(picture, false)); }
                    }
                }
                else if (reference.UsedByCurrentPicture)
                { throw new InvalidDataException($"Missing HEVC short-term reference picture {key}."); }
            }
            foreach (HevcLongTermReference reference in syntax.LongTerm)
            {
                HevcReferencePicture? selected = null;
                if (reference.HasMsbCycle)
                {
                    long target = order - (long)syntax.PictureOrderCountLsb + reference.PictureOrderCountLsb - reference.MsbCycle * modulus;
                    if (target < int.MinValue || target > int.MaxValue || target == order)
                    { throw new InvalidDataException("Invalid HEVC long-term picture order."); }
                    pictures.TryGetValue((int)target, out selected);
                }
                else
                {
                    foreach (HevcReferencePicture picture in pictures.Values)
                    {
                        if ((picture.Order & (modulus - 1)) != reference.PictureOrderCountLsb) { continue; }
                        if (selected is not null) { throw new InvalidDataException("Ambiguous HEVC long-term picture order."); }
                        selected = picture;
                    }
                }
                if (selected is null)
                {
                    if (reference.UsedByCurrentPicture) { throw new InvalidDataException("Missing HEVC long-term reference picture."); }
                    continue;
                }
                if (!retained.Add(selected.Order)) { throw new InvalidDataException("Duplicate HEVC short/long-term reference picture."); }
                if (reference.UsedByCurrentPicture) { longTerm.Add(new(selected, true)); }
            }
            before.Sort((a, b) => b.Picture.Order.CompareTo(a.Picture.Order));
            after.Sort((a, b) => a.Picture.Order.CompareTo(b.Picture.Order));
            foreach (int key in pictures.Keys.ToArray())
            {
                if (!retained.Contains(key)) { pictures[key].Dispose(); pictures.Remove(key); }
            }
            return new(before.ToArray(), after.ToArray(), longTerm.ToArray());
        }

        public void Store(HevcReferencePicture picture, int maximum)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(picture);
            if (maximum is < 1 or > 16 || pictures.Count >= maximum || !pictures.TryAdd(picture.Order, picture))
            { throw new InvalidDataException("The HEVC decoded picture buffer is full or repeats picture order."); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            foreach (HevcReferencePicture picture in pictures.Values) { picture.Dispose(); }
            pictures.Clear(); disposed = true;
        }
    }
}
