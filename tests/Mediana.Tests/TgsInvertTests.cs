// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    public class TgsInvertTests
    {
        [Fact]
        public void InvertBlendsRgbAndPreservesAlpha() => TgsEffectTestInput.VerifyInvert();

        [Fact]
        public void UnsupportedInvertChannelIsDiagnosed()
        {
            TgsDocument document = TgsReader.Read(Encoding.UTF8.GetBytes("""
                {"v":"5.7","w":8,"h":8,"fr":30,"ip":0,"op":30,"layers":[
                  {"ty":1,"ind":1,"sw":8,"sh":8,"sc":"#804020","ef":[
                    {"ty":5,"mn":"ADBE Invert","ef":[{"ty":7,"v":{"k":99}}]}]}]}
                """));
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => TgsRenderer.RenderFrame(document, 0));
            Assert.Contains("Invert channel", error.Message);
        }
    }
}
