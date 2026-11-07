# Opus decoding

`OpusDecoderFactory` registers managed Opus audio decoding in `MediaEngineBuilder`.
It accepts `StreamFormats.Opus`: an Opus identification header and complete
Opus packets. Output is interleaved native-endian Float32 PCM at 48 kHz. No
external decoder or native multimedia dependency is used.

The decoder reconstructs SILK, CELT and hybrid frames. It supports all standard
frame durations, VBR/CBR packet framing, multiple frames per packet, changing
coded channel counts, bandwidth changes, transition redundancy and hybrid
high-band fade-out. Uncoded DTX frames and mode transitions without redundant
audio reconstruct the required missing interval. Mono decoding disables stereo
phase inversion. Channel mapping families 0, 1 and 255 support mono/stereo,
standard one-through-eight-speaker layouts and discrete channels respectively.
Multistream packets share prepared transform and pulse tables. Duplicated and
silent output channels retain their declared positions. Other mapping families
are rejected during factory capability selection.

Header pre-skip is consumed across packets at the original stream beginning.
Header gain scales Float32 PCM without forced clipping. A known packet Duration
specifies the number of presentation samples available from its beginning and
may remove the coded tail. Trimming returns a leased view of the decoded buffer
without copying its PCM. Unknown duration preserves the coded tail. Retained
frames survive decoder reset and disposal.

`IPacketPrerollDecoder` distinguishes original-stream priming from a midstream
restart. With `OggReader`, indexed seeks decode and suppress at least 80 ms of
preceding packets, clamped at the original beginning. Seek output begins at the
selected packet's presentation position; callers can apply an exact sample
window afterwards. Decoders restarted with a finite history can differ from
continuous decoding; tests compare equally primed independent decoders.

SILK and CELT reuse allocator-backed workspaces. Bandwidth changes reuse SILK
storage while preserving the required gain, frequency and stereo histories.
Steady frame reconstruction and bandwidth changes allocate no managed storage.
The public packet API allocates an owned output lease per accepted packet;
working tables are released at completion, reset, failure or disposal. Calls
on one decoder must not overlap. Failure or cancellation requires reset or
disposal before reuse.

Independent libopus 1.6.1 corpora cover 2,448 CELT frames, 3,072 SILK packets,
576 hybrid frames, 768 transition frames and 144 multistream packets. Sample
counts and entropy ranges match exactly where recorded. SILK samples match
exactly; floating transform PCM has relative L2 tolerance 0.00001 and peak
absolute tolerance 0.000003 times the larger of one and the reference peak.
Whole Ogg decoding and indexed restarts additionally verify pre-skip, final
granules, timestamps and ownership through the public audio API. Native AOT
verification covers frame reconstruction and the public multistream factory.

Recovery corpora additionally cover 13,312 public packets with DTX, short and
long loss bursts, resumed speech and transitions without redundant audio.
SILK recovery matches fixed-point reference PCM exactly. CELT loss reconstruction
is non-normative and depends on rounding of retained histories and LPC analysis;
complete-stream comparisons require at least 40 dB signal-to-error ratio and
peak error below 0.02 times the larger of one and the reference peak. Scalar and
SIMD libopus builds also produce different recovered samples. Ordinary-frame
comparisons retain the stricter float tolerances above. Repeated mode changes,
DTX and recovery allocate no managed working storage.

The packet API accepts uncoded Opus frames; an empty media packet does not
represent a lost interval. A public missing-interval API and forward-error-
correction recovery remain open. Ordinary SILK decoding reads and skips its
redundant data correctly.

Framing, synthesis and transitions follow
[RFC 6716](https://www.rfc-editor.org/rfc/rfc6716.html), and Ogg presentation,
channel mapping and gain follow
[RFC 7845](https://www.rfc-editor.org/rfc/rfc7845.html).
