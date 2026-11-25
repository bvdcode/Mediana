# PAL DV fixture

`pal.avi` contains four PAL DV25 frames, generated with FFmpeg using
`-f lavfi -i testsrc2=size=720x576:rate=50 -vf tinterlace=interleave_bottom,setsar=1 -frames:v 4 -an -c:v dvvideo -pix_fmt yuv420p -threads 1 -flags +ildct`.

`first-and-fourth.yuv.gz` contains the decoded YUV 4:2:0 frames at PTS 0 and 3
in the 1/25 clock, selected with `select='eq(n,0)+eq(n,3)'`.
