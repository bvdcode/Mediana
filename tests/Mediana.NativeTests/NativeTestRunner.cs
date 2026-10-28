using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTestRunner
    {
        public static async Task RunAsync()
        {
            H264CabacTestInput.VerifyMixedBins();
            Console.WriteLine("PASS H.264 CABAC arithmetic: mixed decision, bypass and termination bins match exact intervals.");
            (string Name, string Extension, int Packet)[] cases =
                [("avc-tail", ".mov", 0), ("avc-tail", ".mov", 1), ("avc-tail", ".mov", 2),
                    ("avc-tail", ".mov", 3), ("avc-tail", ".mov", 4), ("avc-tail", ".mov", 5), ("avc-cropped", ".mp4", 0)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            await H264CabacTestInput.VerifyIntraContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
            await H264CabacTestInput.VerifyRealIntraPrefixAsync(root);
            Console.WriteLine("PASS H.264 CABAC intra initialization: all 227 contexts at 52 quantizers and the real IDR prefix match.");
            await H264CabacTestInput.VerifyPredictiveContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
            await H264CabacTestInput.VerifyRealPredictivePrefixAsync(root);
            Console.WriteLine("PASS H.264 CABAC predictive initialization: 252 contexts at 52 quantizers, three initialization selections and the real P prefix match.");
            for (int initialization = 0; initialization < 3; initialization++)
            {
                await H264CabacPredictionTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), root, initialization);
            }
            Console.WriteLine("PASS H.264 CABAC P syntax: skip, macroblock/subblock types, reference indices and signed motion differences match exact intervals.");
            await H264CabacTestInput.VerifyIntraPicturesAsync(root);
            Console.WriteLine("PASS H.264 CABAC intra pictures: seven independent filtered YUV references and storage release match.");
            using (H264ReferenceBuffer references = new())
            {
                foreach ((string name, string extension, int packet) in cases)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + extension), packet);
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{packet}.yuv"));
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, expected);
                    Console.WriteLine($"PASS {name}, packet {packet}: all YUV samples match.");
                }
                byte[] weighted = await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.sequence.yuv"));
                const int frameBytes = 94 * 62 * 3 / 2;
                if (weighted.Length != frameBytes * 12)
                {
                    throw new InvalidDataException("The weighted H.264 reference sequence has an unexpected length.");
                }
                for (int index = 0; index < 12; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-weighted.mp4"), index);
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, weighted.AsSpan(index * frameBytes, frameBytes));
                    Console.WriteLine($"PASS avc-weighted, packet {index}: all YUV samples match.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("H.264 reference picture storage was not released.");
            }
            await NativePublicVideoTests.RunAsync(root);
            await NativeSrgbTests.RunAsync(root);
            await RotatedPreviewTestInput.VerifyAsync(root);
            Console.WriteLine("PASS rotated MP4 preview: display matrix, square-pixel 49x100 PNG, CRCs and all reference pixels match.");
            await CabacPreviewTestInput.VerifyAsync(root);
            Console.WriteLine("PASS CABAC MP4 preview: 450 ms selects 600 ms, square-pixel 100x49 PNG and all independent sRGB samples match.");
        }
    }
}
