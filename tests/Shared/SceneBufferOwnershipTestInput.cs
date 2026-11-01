// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class SceneBufferOwnershipTestInput
    {
        internal static void VerifyReuse()
        {
            using SceneRenderContext context = new(new() { MaximumEffectBytes = 16 }, default);
            PixelBuffer first;
            using (SceneBufferScope scope = context.BeginBuffers())
            {
                first = context.CreateBuffer(2, 2);
                first.Clear(new(1, 2, 3, 4));
            }
            using (SceneBufferScope scope = context.BeginBuffers())
            {
                PixelBuffer reused = context.CreateBuffer(2, 2);
                Require(ReferenceEquals(first, reused) && !reused.Pixels.Span.ContainsAnyExcept((byte)0));
                Expect<InvalidOperationException>(() => context.CreateBuffer(1, 1));
            }
            context.Dispose();
            Expect<ObjectDisposedException>(() => _ = first.Pixels);
        }

        internal static void VerifyTrim()
        {
            using SceneRenderContext context = new(new() { MaximumEffectBytes = 16 }, default);
            using SceneBufferScope scope = context.BeginBuffers();
            PixelBuffer active = context.CreateBuffer(1, 1);
            active[0, 0] = new(10, 20, 30, 40);
            PixelBuffer idle = context.CreateBuffer(1, 2);
            context.Release(idle);
            using (IDisposable scratch = context.Reserve(1, 2, 1))
            {
                Expect<ObjectDisposedException>(() => _ = idle.Pixels);
                Require(active[0, 0] == new Rgba32(10, 20, 30, 40));
                Expect<InvalidOperationException>(() => context.Reserve(1, 2, 1));
            }
            PixelBuffer replacement = context.CreateBuffer(1, 2);
            Require(!ReferenceEquals(idle, replacement) && !replacement.Pixels.Span.ContainsAnyExcept((byte)0));
        }

        internal static void VerifyDisposal()
        {
            SceneRenderContext context = new(new(), default);
            PixelBuffer active = context.Buffers.Rent(1, 1);
            PixelBuffer idle = context.Buffers.Rent(1, 2);
            context.Buffers.Return(idle);
            context.Dispose();
            context.Dispose();
            Expect<ObjectDisposedException>(() => _ = active.Pixels);
            Expect<ObjectDisposedException>(() => _ = idle.Pixels);
        }

        internal static void VerifyFailedConstruction()
        {
            using SceneRenderContext context = new(new() { MaximumEffectBytes = (long)int.MaxValue * 4 }, default);
            Expect<ArgumentOutOfRangeException>(() => context.Buffers.Rent(int.MaxValue, 1));
            using IDisposable wholeBudget = context.Reserve(int.MaxValue, 1, 1);
        }

        internal static void VerifyReservationDisposal()
        {
            using SceneRenderContext context = new(new() { MaximumEffectBytes = 16 }, default);
            IDisposable reservation = context.Reserve(1, 1, 1);
            reservation.Dispose();
            reservation.Dispose();
            Expect<InvalidOperationException>(() => context.Reserve(5, 1, 1));
            using IDisposable wholeBudget = context.Reserve(4, 1, 1);
            Expect<InvalidOperationException>(() => context.Reserve(1, 1, 1));
        }

        private static void Expect<TException>(Action action) where TException : Exception
        {
            Exception? caught = null;
            try
            {
                action();
            }
            catch (TException exception)
            {
                caught = exception;
            }
            Require(caught is not null);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Drawing surface ownership or effect budget differs.");
            }
        }
    }
}
