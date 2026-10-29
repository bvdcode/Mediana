// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    public readonly struct CodecOutput<T> where T : class, IDisposable
    {
        private readonly T? value;

        private CodecOutput(CodecOutputStatus status, T? value)
        {
            Status = status;
            this.value = value;
        }

        public CodecOutputStatus Status { get; }
        public T Value => value ?? throw new InvalidOperationException("No output is available.");
        public static CodecOutput<T> InputRequired => default;
        public static CodecOutput<T> EndOfStream => new(CodecOutputStatus.EndOfStream, null);

        public static CodecOutput<T> Available(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new CodecOutput<T>(CodecOutputStatus.Available, value);
        }
    }
}
