// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class TranslatedImageTests
    {
        [Fact]
        public void IntegerTranslationsMatchShaderSampling() => TranslatedImageTestInput.Verify();

        [Fact]
        public void TranslatedFramesHonorPlaneOffsetAndStride() => TranslatedImageTestInput.VerifyStridedFrame();
    }
}
