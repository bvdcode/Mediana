# AAC-LC multichannel references

These AAC-LC inputs and complete Float32 PCM references exercise the predefined
channel configurations 3–7 at 48000 Hz. FFmpeg/ffprobe 6.1.1 prepared independent
references for the five multichannel inputs. FAAD2 2.11.1 supplies the complete
stereo window-transition reference. Tests read the stored media and PCM directly.

| Input | Configuration | Speakers in output PCM order | Coded frames |
| --- | ---: | --- | ---: |
| `layout-3.m4a` | 3 | FL, FR, FC | 15 |
| `layout-4.m4a` | 4 | FL, FR, FC, BC | 15 |
| `layout-5.m4a` | 5 | FL, FR, FC, BL, BR | 15 |
| `layout-6.m4a` | 6 | FL, FR, FC, LFE, BL, BR | 15 |
| `layout-7.m4a` | 7 | FL, FR, FC, LFE, BL, BR, FLC, FRC | 9 |

Configurations 3–6 encode generated interleaved Float32 source signals, each with
13920 samples per channel. At sample `n`, channel `c` has amplitude
`0.045 + 0.009*c` and frequency `263 + 173*c` Hz. Configuration 6 uses 73 Hz for
LFE, channel 3. Samples 5000–5015 add `0.2*sin((n-5000)*pi/16)`. Encoding disables
PNS and enables TNS, automatic M/S and intensity coding. Each speaker therefore
has a distinct signal, including the LFE and back-center channels.

Configuration 7 uses authored ADTS syntax with SCE, three CPEs and LFE, tagged
0, 0, 1, 2 and 0 respectively. The first pair represents FLC/FRC, the second
FL/FR and the third BL/BR. Each of nine access units has one active long-window
scale-factor band, spectral codebook 1, zero scale-factor delta and the 11-bit
spectral codeword 2036. Coded channel `c` in frame `f` uses global gain
`160 + 4*c + f%3`. Pair mask modes are 2, 1 with a zero mask, and 0; the middle
pair has separate windows in odd frames. Every speaker has a nonzero signal
well above the sample tolerance, and each pair of output channels differs by
more than `0.0001`. This checks both speaker mapping and independent pair state.

The configuration-7 reference uses `-strict 1` on input to preserve the normative
7.1 wide speaker mapping. `-ignore_editlist 1` retains all coded frames, including
priming and the final frame. `references.json` records source/input/PCM SHA256,
commands, stream declarations and every packet timestamp. Each complete reference
sample is checked within 0.000002, together with PCM labels, timing, EOF, borrowed
source position, retained-frame lifetime and released decoder storage.

Format references: [ISO/IEC 14496-3:2001, Table 1.11 and §4.5.2.1](https://www.ossrs.net/lts/zh-cn/assets/files/ISO_IEC_14496-3-AAC-2001-7f4d0b3622b322cb72c78f85d91c449f.pdf),
[Fraunhofer channel configurations](https://www2.iis.fraunhofer.de/AAC/multichannel.html).

## Window-transition reference

`window-transitions.m4a` contains 73 authored stereo AAC-LC access units at
48000 Hz. Each speaker covers every one of the 16 sequence transitions and all
four previous/current sine/KBD shape combinations. ISO/IEC 14496-3:2001,
section 4.6.11.3.1 explicitly permits the additional transitions as well as the
eight meaningful ones. Complete PCM follows the overlap-add definition in
section 4.6.11.3.3; these additional transitions are accepted.

Start with one common-window only-long/sine frame. For each of four passes, the
left sequence follows `0,0,1,0,2,0,3,1,1,2,1,3,2,2,3,3,0`; the right sequence
follows its reverse. Shapes for the four passes are respectively all sine, all
KBD, alternating sine/KBD, and alternating KBD/sine. These 68 frames have
independent windows. Four final frames have `(left sequence, left shape, right
sequence, right shape, common_window)` equal to `(2,0,3,1,false)`,
`(2,1,2,1,true)`, `(0,1,1,0,false)`, `(0,0,0,0,true)`. They exercise sharing the
current window after the channels have acquired different histories.

Each channel has one active scale-factor band, book 1, zero scale-factor delta,
and no pulse, TNS or gain-control data. Its global gain is
`160 + 4*channel + frame%3`, with zero-based channel and frame indices. Long
frames use the 11-bit spectral codeword 2036 once; short frames use one group
of eight windows and repeat the codeword eight times. Common windows use M/S
mask mode zero. ADTS declares AAC-LC, rate index 3, configuration 2 and one
raw data block. FFmpeg only stream-copies the authored AAC into M4A; it does
not encode or supply the PCM reference.

FAAD2 decodes the original ADTS as raw Float32 PCM using
`faad -q -f 2 -b 4 -o window-transitions.f32le window-transitions.aac`.
The complete 149504-sample reference occupies 598016 bytes. The manifest records
the source, input and PCM hashes, authored windows and packet timestamps.
Managed and Native AOT tests compare every sample within `2e-6`, including EOF,
channel order, timing and retained-frame ownership. The measured maximum
difference is `1.6763806343078613e-8`. The reference data is stored in the test
suite; FAAD2 is an external verification tool and is not a product dependency.
