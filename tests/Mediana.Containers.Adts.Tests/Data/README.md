The ADTS fixtures contain the original compressed access units from the
`lc-mono-48.m4a` and `lc-stereo-44.m4a` AAC fixtures, remuxed with FFmpeg 6.1.1:

```sh
ffmpeg -nostdin -v error -xerror -i input.m4a -map 0:a:0 -c:a copy -f adts output.aac
```

The tests compare every decoded sample with the existing independent float32
references in `Mediana.Tests/Data/Aac`. No container edit or gapless trimming is
applied; the references include every complete coded AAC frame. The mono input
uses 48 kHz, and the stereo input uses 44.1 kHz.
