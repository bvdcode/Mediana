// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp3SyntaxTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "syntax-references.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                string name = item.GetProperty("name").GetString()!;
                byte[] file = await File.ReadAllBytesAsync(Path.Combine(root, name + ".mp3"));
                if (Convert.ToHexStringLower(SHA256.HashData(file)) != item.GetProperty("input_sha256").GetString())
                {
                    throw new InvalidDataException("The MP3 syntax reference input differs.");
                }
                MediaBufferAllocator allocator = new(128 * 1024);
                using MemoryStream input = new(file);
                await using StreamByteSource source = new(input, leaveOpen: true);
                await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
                {
                    using Mp3BitReservoir reservoir = new(allocator, reader.FirstFrame.Version, 4096);
                    using IncrementalHash fields = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using IncrementalHash main = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using IncrementalHash scales = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    Mp3FrameInfo info = new();
                    int frames = 0;
                    int history = 0;
                    int maximumBack = 0;
                    int totalBits = 0;
                    int scaleBits = 0;
                    int reusedGroups = 0;
                    int[] blocks = new int[4];
                    byte[][] values = [new byte[39], new byte[39]];
                    byte[] limits = new byte[39];
                    while (true)
                    {
                        using MediaPacket? packet = await reader.ReadPacketAsync();
                        if (packet is null)
                        {
                            break;
                        }
                        Mp3FrameHeader header = info.Read(packet.Data.Span);
                        if (header.HasCrc != item.GetProperty("crc").GetBoolean())
                        {
                            throw new InvalidDataException("The MP3 protection flag differs.");
                        }
                        Append(fields, [info.MainDataBegin, info.MainDataBits, info.ChannelCount,
                            info.GranuleCount, info.LeftScaleFactorReuse, info.RightScaleFactorReuse]);
                        foreach (Mp3GranuleInfo granule in info.Granules)
                        {
                            Append(fields, [granule.PartBits, granule.BigValues, granule.GlobalGain, granule.ScaleFactorCompression,
                                granule.BlockType, granule.MixedBlock ? 1 : 0, granule.Table0, granule.Table1, granule.Table2,
                                granule.SubblockGain0, granule.SubblockGain1, granule.SubblockGain2, granule.Region0Count, granule.Region1Count,
                                granule.Preflag ? 1 : 0, granule.ScaleFactorScale ? 1 : 0, granule.Count1Table]);
                            blocks[granule.BlockType]++;
                        }
                        int offset = Mp3FrameHeader.HeaderBytes + (header.HasCrc ? sizeof(ushort) : 0) + header.SideInfoBytes;
                        ReadOnlySpan<byte> data = reservoir.Assemble(packet.Data.Span[offset..], info.MainDataBegin, info.MainDataBits);
                        Append(main, [info.MainDataBits]);
                        main.AppendData(data);
                        int start = 0;
                        values[0].AsSpan().Clear();
                        values[1].AsSpan().Clear();
                        for (int index = 0; index < info.Granules.Length; index++)
                        {
                            Mp3GranuleInfo granule = info.Granules[index];
                            Mp3BitReader bits = new(data, start, granule.PartBits);
                            int channel = index % header.ChannelCount;
                            int reuse = index < header.ChannelCount ? 0
                                : (channel == 0 ? info.LeftScaleFactorReuse : info.RightScaleFactorReuse);
                            bool intensity = header.ChannelMode == Mp3ChannelMode.JointStereo
                                && (header.ModeExtension & 1) != 0 && channel == 1;
                            switch (header.Version)
                            {
                                case MpegAudioVersion.Mpeg1:
                                    Mp3ScaleFactors.ReadMpeg1(ref bits, granule, reuse, values[channel]);
                                    limits.AsSpan().Fill(byte.MaxValue);
                                    break;
                                case MpegAudioVersion.Mpeg2:
                                case MpegAudioVersion.Mpeg25:
                                    Mp3ScaleFactors.ReadLowRate(ref bits, granule, intensity, values[channel], limits);
                                    break;
                                default:
                                    throw new InvalidOperationException();
                            }
                            int consumed = bits.Position - start;
                            Append(scales, [consumed]);
                            scales.AppendData(values[channel]);
                            scales.AppendData(limits);
                            scaleBits += consumed;
                            reusedGroups += BitOperations.PopCount((uint)reuse);
                            bits.Skip(bits.Remaining);
                            start = bits.Position;
                        }
                        if (start != info.MainDataBits)
                        {
                            throw new InvalidDataException("The MP3 granule bit boundaries differ.");
                        }
                        totalBits += info.MainDataBits;
                        maximumBack = Math.Max(maximumBack, info.MainDataBegin);
                        history += info.MainDataBegin != 0 ? 1 : 0;
                        frames++;
                    }
                    if (frames != item.GetProperty("frames").GetInt32()
                        || history != item.GetProperty("frames_with_history").GetInt32()
                        || maximumBack != item.GetProperty("maximum_back_bytes").GetInt32()
                        || totalBits != item.GetProperty("main_data_bits").GetInt32()
                        || scaleBits != item.GetProperty("scalefactor_bits").GetInt32()
                        || reusedGroups != item.GetProperty("reused_scalefactor_groups").GetInt32()
                        || Convert.ToHexStringLower(scales.GetHashAndReset()) != item.GetProperty("scalefactors_sha256").GetString()
                        || Convert.ToHexStringLower(fields.GetHashAndReset()) != item.GetProperty("side_fields_sha256").GetString()
                        || Convert.ToHexStringLower(main.GetHashAndReset()) != item.GetProperty("main_data_sha256").GetString())
                    {
                        throw new InvalidDataException($"The complete MP3 side fields or assembled main data differ for {name}.");
                    }
                    for (int index = 0; index < blocks.Length; index++)
                    {
                        if (blocks[index] != item.GetProperty("block_counts")[index].GetInt32())
                        {
                            throw new InvalidDataException("The MP3 window-type counts differ.");
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0 || !input.CanRead)
                {
                    throw new InvalidOperationException("MP3 syntax buffers or source ownership were not released correctly.");
                }
            }
        }

        private static void Append(IncrementalHash hash, ReadOnlySpan<int> values)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            foreach (int value in values)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
                hash.AppendData(bytes);
            }
        }
    }
}
