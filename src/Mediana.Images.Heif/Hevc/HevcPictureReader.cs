// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class HevcPictureReader(HeicDecodeOptions options)
    {
        private readonly Dictionary<int, SequenceParameters> sequences = new Dictionary<int, SequenceParameters>();
        private readonly Dictionary<int, PictureParameters> pictures = new Dictionary<int, PictureParameters>();
        private readonly List<IntraSliceHeader> slices = new List<IntraSliceHeader>();
        private int? pictureType;

        public void Read(NalPayload unit)
        {
            if ((unit.Type >= 16) && (unit.Type <= 21))
            {
                AddSlice(unit);
                return;
            }
            if ((unit.Type >= 35) && (unit.Type <= 40))
            {
                return;
            }
            switch (unit.Type)
            {
                case 32:
                    break;
                case 33:
                    SequenceParameters sequence = SequenceParameters.Read(unit, options);
                    if (!sequences.TryAdd(sequence.Id, sequence))
                    {
                        throw new InvalidDataException("Duplicate HEVC SPS identifier.");
                    }
                    break;
                case 34:
                    PictureParameters picture = PictureParameters.Read(unit, sequences);
                    if (!pictures.TryAdd(picture.Id, picture))
                    {
                        throw new InvalidDataException("Duplicate HEVC PPS identifier.");
                    }
                    break;
                default:
                    throw new UnsupportedHeicFeatureException("HEVC NAL unit type " + unit.Type);
            }
        }

        public Task<DecodedPicture> DecodeAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (slices.Count == 0)
            {
                throw new InvalidDataException("HEVC image contains no intra random-access picture.");
            }
            PicturePartitions? partitions = slices.Count > 1 ? new PicturePartitions(slices) : null;
            IntraSliceHeader first = slices[0];
            if ((partitions is null) && first.Picture.EntropyCodingSync)
            {
                int rows = ((first.Picture.Sequence.Height - 1) / (1 << first.Picture.Sequence.Log2Ctb)) + 1;
                if (first.RowSubstreams.Count != rows)
                {
                    throw new InvalidDataException("WPP entry points do not cover every picture row.");
                }
            }
            return new IntraPictureDecoder(first, partitions).DecodeAsync(workerCount, cancellationToken);
        }

        private void AddSlice(NalPayload unit)
        {
            IntraSliceHeader header = IntraSliceHeader.Read(unit, pictures);
            if (slices.Count == 0)
            {
                if (header.StartCtbAddress != 0)
                {
                    throw new InvalidDataException("The first HEVC slice does not start the picture.");
                }
                pictureType = unit.Type;
            }
            else if (header.StartCtbAddress == 0)
            {
                throw new UnsupportedHeicFeatureException("multiple HEVC pictures per image item");
            }
            else if ((header.StartCtbAddress <= slices[slices.Count - 1].StartCtbAddress) ||
                !ReferenceEquals(header.Picture, slices[0].Picture) || (unit.Type != pictureType) ||
                (header.PictureOrderCount != slices[0].PictureOrderCount))
            {
                throw new InvalidDataException("Inconsistent HEVC slices in one image item.");
            }
            slices.Add(header);
        }
    }
}
