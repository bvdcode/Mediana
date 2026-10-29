// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;

namespace Mediana.Sample
{
    internal class PacketDigest : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public int Count { get; private set; }
        public long Bytes { get; private set; }

        public void Append(ReadOnlySpan<byte> data)
        {
            hash.AppendData(data);
            Count++;
            Bytes += data.Length;
        }

        public string Complete() => Convert.ToHexStringLower(hash.GetHashAndReset());

        public void Dispose()
        {
            hash.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
