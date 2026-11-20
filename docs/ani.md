# Animated cursors

`Mediana.Codecs.Bmp.AniImageDecoder` reads RIFF ACON animations with icon-form
frames. ICO/CUR pixel data is decoded through the caller's common image context,
including embedded PNG images. The CLI and default image registry include ANI.

`ImageDecodeRequest.PageIndex` selects a zero-based presentation step, including
sequence repetitions. `Time` selects a position within one cycle. Selecting both
a nonzero step and nonzero time is rejected. `IImageSequenceDecoder` enumerates
from the selected step to the end of that cycle. Each frame has its own buffer
lease and the declared timestamp/duration, expressed in exact 1/60-second units.

The first ICO/CUR directory entry supplies each stored frame's image. The reader
supports implicit order, explicit `seq ` order and per-step `rate` durations.
Zero delays remain zero; positive sub-jiffy timestamps skip zero-duration steps.
Pixels are decoded only for selected steps. Inspection reads frame declarations
without decompressing pixel data and distinguishes stored frames from steps.

Raw-bitmap ANI without the icon-form flag is not implemented. Unknown flag bits,
duplicate required chunks, missing sequences, invalid references and mismatched
frame/rate counts are rejected. Shared RIFF parsing checks declared bounds and
odd-chunk padding without imposing WebP's format-specific zero-padding rule.

Tests cover exact time boundaries, sequence repetitions, retained frames,
early enumeration disposal, zero delays, fractional-jiffy selection, a one-frame
pixel budget, truncation and malformed sequence/rate tables.

References: [Microsoft RIFF description](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-)
and [Wine's ANI loader](https://github.com/wine-mirror/wine/blob/master/dlls/user32/cursoricon.c).
