// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacAdtsCrcTests
    {
        [Fact]
        public void ChannelProtectionStopsAfter192BitsRatherThanCoveringTheWholePayload()
        {
            byte[] frame = Convert.FromHexString(
                "FFF04C400FA28036D3011A93983AB8B6DA11B7069A381FEBF5D55E9AAF97068E11592D6C40D740B9F5C0226B804489D6" +
                "8D6F1882CBEC1F50CD11A0FBBEBBDDED4A5058B563C494C7534093517608B8AEDF58D8AEE074B5A48C5ABDE5E87D8DFB" +
                "361BF7ECB176CB175A93068229663CE5F2E8E92905C4F056FDFBC0D1C0");
            Assert.Equal(125, frame.Length);
            VerifyFirstChannel(frame);
            frame[9 + 25] ^= 0x80;
            VerifyFirstChannel(frame);
            frame[9 + 24] ^= 0x20;
            Assert.Throws<InvalidDataException>(() => VerifyFirstChannel(frame));
        }

        private static void VerifyFirstChannel(byte[] frame)
        {
            AacAdtsCrc crc = new(frame.AsSpan(9), frame.AsSpan(0, 9), 3, 1);
            crc.AddChannel(3, 919);
            crc.Verify();
        }
    }
}
