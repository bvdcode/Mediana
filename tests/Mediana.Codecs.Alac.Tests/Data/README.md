# ALAC reference audio

The six M4A files contain 9107 or 9120 procedurally generated samples per channel.
FFmpeg 6.1.1 encodes ALAC and independently decodes the signed32 little-endian PCM
references. Sources contain distinct tones per channel, silence or deterministic
noise. The 16-bit cases use s16p and the 24-bit cases s32p encoder input.

surround24 has six channels and no channel-layout record. Its reference is reordered
from FFmpeg's speaker order to the encoded C/L/R/Ls/Rs/LFE order with:

    -af 'pan=6c|c0=c2|c1=c0|c2=c1|c3=c4|c4=c5|c5=c3'

The decoder publishes discrete channel labels for that file, preserving the absence
of a declared speaker layout. The runtime library has no FFmpeg dependency.
