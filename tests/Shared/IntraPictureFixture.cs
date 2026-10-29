// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Mediana.Images.Heif;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Tests
{
    internal static class IntraPictureFixture
    {
        public static async Task<IntraSliceHeader> ReadSliceAsync(string file)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif", file));
            Dictionary<int, SequenceParameters> sequences = new Dictionary<int, SequenceParameters>();
            Dictionary<int, PictureParameters> pictures = new Dictionary<int, PictureParameters>();
            foreach (NalPayload nal in ReadAnnexB(encoded))
            {
                switch (nal.Type)
                {
                    case 33:
                        SequenceParameters sps = SequenceParameters.Read(nal, HeicDecodeOptions.Default);
                        sequences.Add(sps.Id, sps);
                        break;
                    case 34:
                        PictureParameters pps = PictureParameters.Read(nal, sequences);
                        if (!pps.EntropyCodingSync)
                        {
                            throw new InvalidDataException("Expected a wavefront picture.");
                        }
                        pictures.Add(pps.Id, pps);
                        break;
                    case 19:
                    case 20:
                        return IntraSliceHeader.Read(nal, pictures);
                }
            }
            throw new InvalidDataException("The picture fixture has no IDR slice.");
        }

        public static string PlaneHash(DecodedPicture picture)
        {
            List<byte> samples = new List<byte>();
            AddPlane(samples, picture.Luma, picture.Sequence.BitDepth);
            if (picture.Cb is not null)
            {
                AddPlane(samples, picture.Cb, picture.Sequence.BitDepth);
            }
            if (picture.Cr is not null)
            {
                AddPlane(samples, picture.Cr, picture.Sequence.BitDepth);
            }
            return Convert.ToHexString(SHA256.HashData(samples.ToArray()));
        }

        private static void AddPlane(List<byte> samples, SamplePlane plane, int depth)
        {
            if (depth == 8)
            {
                samples.AddRange(plane.ByteView.Samples.ToArray());
                return;
            }
            foreach (ushort sample in plane.WordView.Samples)
            {
                samples.Add((byte)(sample & 255));
                if (depth == 10)
                {
                    samples.Add((byte)(sample >> 8));
                }
            }
        }

        public static IEnumerable<NalPayload> ReadAnnexB(byte[] data)
        {
            int start = 4;
            for (int i = 4; i <= data.Length; i++)
            {
                if ((i == data.Length) || (((i + 4) <= data.Length) && (data[i] == 0) && (data[i + 1] == 0) && (data[i + 2] == 0) && (data[i + 3] == 1)))
                {
                    yield return new NalPayload(data.AsMemory(start, i - start));
                    start = i + 4;
                    i += 3;
                }
            }
        }
    }
}
