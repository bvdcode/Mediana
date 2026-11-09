# H.264 encoder deblocking references

Eight complete 80-picture I/P sequences exercise CAVLC, CABAC Fast and CABAC rate-distortion analysis with in-loop deblocking. Inputs are deterministic YUV patterns from `H264TemporalEncodingTestInput`: cropped moving gradients, chroma-only changes, a scene transition with variable frame durations, and a constant picture. Cases cover QP0, QP18, QP26, QP38 and QP51, zero filter offsets and both offset limits.

The gzip files contain complete visible planar YUV420P8 output independently decoded by FFmpeg 6.1.1. `references.json` records transport and decoded SHA-256 values. Every sequence matched the independent decoder byte for byte before compression. The shared sequence checks also verify filter syntax, picture count, presentation and decode timestamps, durations, IDR boundaries, HLS segmentation and release of media buffers.

Filtering follows ITU-T H.264 (06/2026), clauses 7.4.3 and 8.7. The encoder filters the reconstructed picture before that picture becomes a prediction reference.
