// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacDecoderWorkspace : IDisposable
    {
        private const int OutputOffset = 2048 * sizeof(double);
        private const int FourierOffset = OutputOffset + 1024 * sizeof(double);
        private readonly WritableMediaBuffer storage;
        private readonly int[] elementTags;

        public AacDecoderWorkspace(MediaBufferAllocator allocator, AacChannelLayout layout)
        {
            Layout = layout;
            Channels = new AacChannelState[layout.ChannelCount];
            elementTags = new int[layout.Elements.Length];
            Array.Fill(elementTags, -1);
            int allocated = 0;
            bool succeeded = false;
            try
            {
                for (; allocated < Channels.Length; allocated++)
                {
                    Channels[allocated] = new AacChannelState(allocator);
                }
                storage = allocator.Allocate(FourierOffset + 512 * 2 * sizeof(double));
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    for (int index = 0; index < allocated; index++)
                    {
                        Channels[index].Dispose();
                    }
                }
            }
        }

        public AacChannelLayout Layout { get; }
        public AacChannelState[] Channels { get; }
        public AacSbrElement?[] SbrElements { get; private set; } = [];
        public AacSbrChannelState?[] SbrChannels { get; private set; } = [];
        public Span<double> Time => MemoryMarshal.Cast<byte, double>(storage.Memory.Span[..OutputOffset]);
        public Span<double> Output => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(OutputOffset, 1024 * sizeof(double)));
        public Span<Complex> Fourier => MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span[FourierOffset..]);

        public void BeginSbrFrame()
        {
            foreach (AacSbrElement? element in SbrElements)
            {
                if (element is not null)
                {
                    element.Present = false;
                }
            }
        }

        public AacSbrElement GetSbrElement(int index, int sampleRate)
        {
            if (SbrElements.Length == 0)
            {
                SbrElements = new AacSbrElement?[Layout.Elements.Length];
                SbrChannels = new AacSbrChannelState?[Channels.Length];
            }
            return SbrElements[index] ??= new AacSbrElement(Layout.Elements[index] == 1 ? 2 : 1, sampleRate);
        }

        public void BindElement(int index, int type, int tag)
        {
            if (index >= Layout.Elements.Length || type != Layout.Elements[index])
            {
                throw new InvalidDataException("The AAC channel elements do not match the configured speaker layout.");
            }
            if (elementTags[index] >= 0 && elementTags[index] != tag)
            {
                throw new InvalidDataException("The AAC element tag changed within the configured stream.");
            }
            for (int previous = 0; previous < index; previous++)
            {
                if (Layout.Elements[previous] == type && elementTags[previous] == tag)
                {
                    throw new InvalidDataException("AAC elements of the same type must have unique instance tags.");
                }
            }
            elementTags[index] = tag;
        }

        public void Dispose()
        {
            foreach (AacChannelState channel in Channels)
            {
                channel.Dispose();
            }
            foreach (AacSbrChannelState? channel in SbrChannels)
            {
                channel?.Dispose();
            }
            storage.Dispose();
        }
    }
}
