// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal static class H264TestBits
    {
        public static string Ue(int value)
        {
            string binary = Convert.ToString(value + 1, 2);
            return new string('0', binary.Length - 1) + binary;
        }

        public static byte[] Rbsp(string content, bool trailing = true)
        {
            string bits = content.Replace(" ", string.Empty, StringComparison.Ordinal) + (trailing ? "1" : string.Empty);
            bits = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
            List<byte> bytes = [];
            int zeros = 0;
            for (int index = 0; index < bits.Length; index += 8)
            {
                byte value = Convert.ToByte(bits.Substring(index, 8), 2);
                if (zeros == 2 && value <= 3)
                {
                    bytes.Add(3);
                    zeros = 0;
                }
                bytes.Add(value);
                zeros = value == 0 ? zeros + 1 : 0;
            }
            return bytes.ToArray();
        }
    }
}
