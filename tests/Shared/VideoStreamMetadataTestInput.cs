// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class VideoStreamMetadataTestInput
    {
        public static MediaEngine CreateEngine() => MediaProbeTestInput.CreateBuilder()
            .AddStreamMetadataReader(new H264StreamMetadataReader()).Build();

        public static async Task VerifyAsync(string root)
        {
            H264MetadataTestInput.VerifyMultipleConfigurations();
            MediaEngine engine = CreateEngine();
            if (engine.AudioDecoderImplementations.Count != 0 || engine.VideoDecoderImplementations.Count != 0)
            {
                throw new InvalidOperationException("Reading SPS metadata must not require a decoder.");
            }
            string directory = Path.Combine(root, "Metadata");
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "video-headers.json")));
            foreach (JsonElement entry in references.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("input").GetString()!;
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(directory, name));
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("An independent video header input changed.");
                }
                MediaInfo info;
                using (MemoryStream memory = new(input))
                {
                    await using RandomAccessStreamByteSource source = new(memory, leaveOpen: true);
                    await source.ReadAsync(new byte[3]);
                    info = await engine.ProbeAsync(source, new MediaOpenOptions { BufferBudgetBytes = 32 * 1024 });
                    if (memory.Position != 3 || !memory.CanRead)
                    {
                        throw new InvalidDataException("Video header inspection changed source ownership or position.");
                    }
                }
                JsonElement stream = entry.GetProperty("reference").GetProperty("streams").EnumerateArray()
                    .Single(item => item.GetProperty("codec_type").GetString() == "video");
                int index = stream.GetProperty("index").GetInt32();
                if (info.StreamMetadata.Count != 1 || info.StreamMetadata[index] is not VideoStreamMetadata video
                    || video.Configurations.Count != 1 || video.Configurations[0] is not H264SequenceMetadata sequence)
                {
                    throw new InvalidDataException($"Video header metadata is missing for {name}.");
                }
                byte[] profile = Convert.FromHexString(entry.GetProperty("profileBytes").GetString()!);
                (VideoChromaSampling sampling, int depth) = Text(stream, "pix_fmt") switch
                {
                    "yuv420p" => (VideoChromaSampling.Sampling420, 8),
                    "yuv420p10le" => (VideoChromaSampling.Sampling420, 10),
                    "yuv422p" => (VideoChromaSampling.Sampling422, 8),
                    "yuv444p" => (VideoChromaSampling.Sampling444, 8),
                    _ => throw new InvalidDataException("Unexpected reference pixel format.")
                };
                bool progressive = Text(stream, "field_order") == "progressive";
                if (sequence.Width != stream.GetProperty("width").GetInt32() || sequence.Height != stream.GetProperty("height").GetInt32()
                    || sequence.ChromaSampling != sampling || sequence.LumaBitDepth != depth || sequence.ChromaBitDepth != depth
                    || sequence.ProfileIdc != profile[0] || sequence.ConstraintFlags != profile[1] || sequence.LevelIdc != profile[2]
                    || sequence.LevelIdc != stream.GetProperty("level").GetInt32() || sequence.FrameMbsOnly != progressive
                    || (progressive && sequence.IsProgressive != true) || (!progressive && sequence.IsProgressive is not null))
                {
                    throw new InvalidDataException($"SPS image properties differ from the independent reference for {name}.");
                }
                string[] ratio = Text(stream, "sample_aspect_ratio")!.Split(':');
                SampleAspectRatio aspect = sequence.SampleAspectRatio ?? throw new InvalidDataException("The SPS sample aspect ratio is missing.");
                if ((long)aspect.Numerator * int.Parse(ratio[1], CultureInfo.InvariantCulture)
                    != (long)aspect.Denominator * int.Parse(ratio[0], CultureInfo.InvariantCulture))
                {
                    throw new InvalidDataException("The SPS sample aspect ratio differs.");
                }
                ColorPrimaries primaries = Text(stream, "color_primaries") switch
                {
                    null => ColorPrimaries.Unspecified,
                    "bt709" => ColorPrimaries.Bt709,
                    "bt2020" => ColorPrimaries.Bt2020,
                    _ => throw new InvalidDataException("Unexpected reference color primaries.")
                };
                TransferFunction transfer = Text(stream, "color_transfer") switch
                {
                    null => TransferFunction.Unspecified,
                    "bt709" => TransferFunction.Bt709,
                    "smpte2084" => TransferFunction.Pq,
                    _ => throw new InvalidDataException("Unexpected reference transfer function.")
                };
                MatrixCoefficients matrix = Text(stream, "color_space") switch
                {
                    null => MatrixCoefficients.Unspecified,
                    "bt709" => MatrixCoefficients.Bt709,
                    "bt2020nc" => MatrixCoefficients.Bt2020NonConstant,
                    _ => throw new InvalidDataException("Unexpected reference color matrix.")
                };
                ColorRange range = Text(stream, "color_range") switch
                {
                    null or "tv" => ColorRange.Limited,
                    "pc" => ColorRange.Full,
                    _ => throw new InvalidDataException("Unexpected reference color range.")
                };
                if (sequence.Color.Primaries != primaries || sequence.Color.Transfer != transfer || sequence.Color.Matrix != matrix || sequence.Color.Range != range)
                {
                    throw new InvalidDataException($"SPS color properties differ for {name}.");
                }
                if (sampling == VideoChromaSampling.Sampling420 && progressive)
                {
                    ChromaLocation location = Text(stream, "chroma_location") switch
                    {
                        "left" => ChromaLocation.Left,
                        "topleft" => ChromaLocation.TopLeft,
                        _ => throw new InvalidDataException("Unexpected reference chroma location.")
                    };
                    if (sequence.Color.ChromaLocation != location)
                    {
                        throw new InvalidDataException("The progressive 4:2:0 chroma location differs.");
                    }
                }
                else if (sequence.Color.ChromaLocation != ChromaLocation.Unspecified)
                {
                    throw new InvalidDataException("A single progressive 4:2:0 location was assigned outside its scope.");
                }
            }
        }

        private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) ? item.GetString() : null;
    }
}
