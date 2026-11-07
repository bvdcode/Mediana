# Ogg reference streams

The stereo streams contain synthetic tones encoded with FFmpeg's libopus and
libvorbis encoders. Both are 1.37 seconds long; Opus uses 48 kHz, Vorbis 44.1 kHz.
The title tag includes Latin and Cyrillic text. No recorded audio is included.

The `.packets` files contain every audio packet independently demuxed by ffprobe,
preceded by a four-byte little-endian payload length. Header packets are excluded.
The `.timing.json` files retain ffprobe's packet PTS and duration in sample units.
Container tests compare complete compressed payloads, not only signatures.
