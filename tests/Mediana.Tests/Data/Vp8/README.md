VP8 reconstruction corpus

Eight synthetic VP8 streams exercise versions 0–3, odd dimensions, motion,
noise, repeated keyframes and four hidden alternate reference pictures.
The raw YUV420 references contain visible pictures only, with no timestamp
resampling. Every visible row is compared exactly after initial decoding
and after reset; reference storage and failure cleanup are also checked.

Sources are FFmpeg lavfi testsrc/testsrc2 and generated noise, encoded by
libvpx. The motion source is 64x48 at 12 fps for two seconds, keyframe
interval 13, 120 kbit/s. The odd source is testsrc at 65x49, 10 fps for
1.5 seconds. The split source is testsrc2 at 160x96, 20 fps for two
seconds, 50 kbit/s and keyframe interval 25. Version sources use testsrc2
at 80x64, 10 fps for one second and the corresponding libvpx profile.

The alternate reference source uses testsrc2 at 128x80, 24 fps for three
seconds, two-pass libvpx, 150 kbit/s, keyframe interval 48,
auto-alt-ref 1, lag-in-frames 25 and arnr-maxframes 7. Its 76 coded
pictures produce 72 visible pictures. Export references using:

    ffmpeg -i input.ivf -fps_mode passthrough -pix_fmt yuv420p -f rawvideo output.yuv

Format reference: https://www.rfc-editor.org/rfc/rfc6386
