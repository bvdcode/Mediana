// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;

namespace Mediana.Tests
{
    public class TgsTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Tgs");

        [Fact]
        public async Task SupportedReferenceFramesMatchEveryPixel()
        {
            await TgsReferenceTestInput.VerifyAllAsync(Root);
        }

        [Theory]
        [InlineData("bounce-path-expression.tgs")]
        [InlineData("controller-expressions.tgs")]
        [InlineData("elastic-expressions.tgs")]
        [InlineData("missing-color-controller.tgs")]
        public async Task ExpressionsAreRejectedWithoutAnExecutionEngine(string name)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(Root, name));
            Assert.Throws<NotSupportedException>(() => TgsReader.Read(bytes));
        }

        [Theory]
        [InlineData("orbit")]
        [InlineData("effects")]
        [InlineData("geometry")]
        public async Task GzipAndJsonProduceTheSameFrames(string name)
        {
            TgsDocument json = await TgsReader.ReadFileAsync(Path.Combine(Root, name + ".json"));
            TgsDocument gzip = await TgsReader.ReadFileAsync(Path.Combine(Root, name + ".tgs"));
            DrawingRenderOptions options = new() { Width = 64, Height = 64, Supersampling = 1 };
            foreach (double position in new[] { 0, .25, .5, .75 })
            {
                double frame = json.InPoint + (json.OutPoint - json.InPoint) * position;
                PixelBuffer first = TgsRenderer.RenderFrame(json, frame, options);
                PixelBuffer second = TgsRenderer.RenderFrame(gzip, frame, options);
                Assert.Equal(first.Pixels.ToArray(), second.Pixels.ToArray());
            }
        }

        [Fact]
        public async Task LazyAnimationPreservesTimingAndPartialLastFrame()
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, "orbit.tgs"));
            TgsAnimationOptions animation = new() { StartTime = TimeSpan.FromSeconds(.2), Duration = TimeSpan.FromSeconds(.35), FrameRate = 10 };
            TgsFrame[] frames = TgsRenderer.RenderFrames(document, animation, new() { Width = 16, Height = 16, Supersampling = 1 }).ToArray();
            Assert.Equal(4, frames.Length);
            Assert.Equal(TimeSpan.FromSeconds(.2), frames[0].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(.5), frames[3].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(.05), frames[3].Duration);
            Assert.Equal(TimeSpan.FromSeconds(.35), TimeSpan.FromTicks(frames.Sum(frame => frame.Duration.Ticks)));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrames(document, animation with { MaximumFrames = 2 }));
            using CancellationTokenSource cancellation = new();
            using IEnumerator<TgsFrame> iterator = TgsRenderer.RenderFrames(document, animation,
                new() { Width = 16, Height = 16 }, cancellation.Token).GetEnumerator();
            Assert.True(iterator.MoveNext());
            await cancellation.CancelAsync();
            Assert.Throws<OperationCanceledException>(() => iterator.MoveNext());
        }

        [Fact]
        public async Task RenderingTheSameCompositionIsIndependentAcrossConcurrentFrames()
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, "effects.tgs"));
            DrawingRenderOptions options = new() { Width = 64, Height = 64, Supersampling = 1 };
            double[] positions = [0, .25, .5, .75];
            byte[][] expected = positions.Select(position => TgsRenderer.RenderFrame(document,
                document.InPoint + ((document.OutPoint - document.InPoint) * position), options).Pixels.ToArray()).ToArray();
            await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            {
                int selected = index % positions.Length;
                PixelBuffer image = TgsRenderer.RenderFrame(document,
                    document.InPoint + ((document.OutPoint - document.InPoint) * positions[selected]), options);
                Assert.Equal(expected[selected], image.Pixels.ToArray());
            })));
        }

        [Fact]
        public async Task EffectMemoryBudgetAppliesToDirectScenesAndResetsForTheNextRender()
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, "effects.tgs"));
            DrawingRenderOptions options = new() { Width = 64, Height = 64, Supersampling = 1 };
            Assert.Throws<InvalidOperationException>(() => TgsRenderer.RenderFrame(document, document.InPoint,
                options with { MaximumEffectBytes = 1 }));
            Assert.Equal(64 * 64 * 4, TgsRenderer.RenderFrame(document, document.InPoint, options).Pixels.Length);
        }

        [Fact]
        public void UnsupportedBlendRequiresExplicitPartialRenderingAndRemainsDiagnosed()
        {
            byte[] json = System.Text.Encoding.UTF8.GetBytes("""
                {"v":"5.7","w":32,"h":32,"fr":30,"ip":0,"op":60,
                 "layers":[{"ty":1,"ip":0,"op":60,"sw":32,"sh":32,"sc":"#ff0000","bm":99}]}
                """);
            TgsDocument document = TgsReader.Read(json);
            Assert.Throws<NotSupportedException>(() => TgsRenderer.RenderFrame(document, 0));
            TgsFrame frame = TgsRenderer.RenderFrameDetailed(document, 0, new() { AllowPartialRendering = true, Supersampling = 1 });
            Assert.Contains("Lottie blend modes", frame.Warnings);
            Assert.Equal(new Rgba32(255, 0, 0, 255), frame.Image[16, 16]);
        }

        [Fact]
        public void RepeatedGeometryCountsTowardTheExpandedPathBudget()
        {
            byte[] json = System.Text.Encoding.UTF8.GetBytes("""
                {"v":"5.7","w":32,"h":32,"fr":30,"ip":0,"op":60,"layers":[
                  {"ty":4,"ip":0,"op":60,"shapes":[
                    {"ty":"rc","p":{"k":[16,16]},"s":{"k":[8,8]},"r":{"k":0}},
                    {"ty":"fl","c":{"k":[1,0,0,1]},"o":{"k":100}},
                    {"ty":"rp","c":{"k":20},"o":{"k":0},"tr":{"p":{"k":[0,0]},"s":{"k":[100,100]},"so":{"k":100},"eo":{"k":100}}}]}]}
                """);
            TgsDocument limited = TgsReader.Read(json, new() { MaximumPathCommands = 100 });
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(limited, 0));
            TgsDocument allowed = TgsReader.Read(json, new() { MaximumPathCommands = 1000 });
            Assert.Equal(32, TgsRenderer.RenderFrame(allowed, 0, new() { Supersampling = 1 }).Width);
        }

        [Fact]
        public async Task InputLimitsAndUnsupportedFramePositionsAreRejected()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "orbit.tgs"));
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(data, new() { MaximumInputBytes = data.Length - 1 }));
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(data, new() { MaximumJsonBytes = 16 }));
            Assert.Throws<OperationCanceledException>(() => TgsReader.Read(data, cancellationToken: new CancellationToken(true)));
            TgsDocument document = TgsReader.Read(data);
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrame(document, document.OutPoint));
        }
    }
}
