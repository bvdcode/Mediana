// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Svg;
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
            SvgRenderOptions options = new() { Width = 64, Height = 64, Supersampling = 1 };
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
