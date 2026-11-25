The AVI samples contain 12 generated frames from FFmpeg's `testsrc2=size=54x38:rate=12`,
encoded with `-threads 1 -g 5 -q:v 5`. `mp43.avi` uses `-c:v msmpeg4 -vtag MP43`
and `wmv2.avi` uses `-c:v wmv2 -vtag WMV2`.
`wmv2-loop.avi` adds `-flags +loop`.
The YUV files are display-order FFmpeg decodes with `-pix_fmt yuv420p -f rawvideo`.

`wmv2-tools.avi` starts with a generated 16×16 WMV2 frame. Its two predictive packets
encode an 8×4 split with modified subpixel motion and a 4×8 split with ordinary motion,
respectively. Both use coded coefficients in each half of the split block.
