# MJPEG video references

Twelve deterministic 64×48 pictures at 8 fps, independently encoded with FFmpeg's
MJPEG encoder at quantizer 3. The three Matroska files carry full-range 4:2:0,
4:2:2 and 4:4:4 JPEG pictures. Native container labels specify BT.709 primaries
and transfer, a BT.601 matrix and square pixels.

```sh
ffmpeg -f lavfi -i testsrc2=size=64x48:rate=8 -frames:v 12 -an \
  -c:v mjpeg -q:v 3 -pix_fmt yuvj420p -color_primaries bt709 \
  -color_trc bt709 -colorspace smpte170m -color_range pc -threads 1 mjpeg-420.mkv
ffmpeg -threads 1 -i mjpeg-420.mkv -an -pix_fmt yuvj420p \
  -f rawvideo -threads 1 mjpeg-420.yuv
python3 reference.py
```

Repeat with `yuvj422p` and `yuvj444p`. The YUV files are FFmpeg's independently
decoded component samples, before any scaling or RGB conversion. `reference.py`
reconstructs centered chroma with bilinear interpolation and floating-point
full-range BT.601 equations; unsubsampled axes have zero phase. This avoids
differences between optimized RGB conversion filters when no dimensions change.

Raw RGBA references contain all twelve pictures. Maximum observed RGB errors
are 3, 3 and 2 codes; RGB mean squared errors are 0.252, 0.235 and 0.0335.
Tests require at most four codes and MSE below one across every picture; alpha,
dimensions, timestamps and picture counts are exact assertions.
