# H.264 4:4:4 reference sequences

Procedural 83×75 planar sources contain moving patches, gradients and periodic
edges in each channel. Ten-bit sources also vary the low two bits. Eighteen
frames at 12 fps are encoded by FFmpeg/libx264 with flat scaling, two slices,
GOP 9 (or intra-only), QP26 (or 45), and chroma QP offset 3.
The RGB sequence uses FFmpeg testsrc and libx264rgb, with full-range sRGB labels.
MBAFF adds one padded row and top-field-first coding. Its optional SEI messages
are omitted with `filter_units=remove_types=6`; decoded sample references are
produced from the final file.

FFmpeg decodes complete sequences to yuv444p, yuv444p10le or gbrp. Gzip files
store those raw reference samples with a zero timestamp. Product decoding has
no FFmpeg or libx264 dependency.

```text
ffmpeg version 6.1.1-3ubuntu5 Copyright (c) 2000-2023 the FFmpeg developers
```

SHA-256:

| File | SHA-256 |
| --- | --- |
| `avc-444-10-cabac-b.mp4` | `8e6ccf0a6f96e97545ee43cafc1550481ebd8aa1acd5a7c78c93123fd1fad335` |
| `avc-444-10-cabac-b.yuv.gz` | `c81ac164d4f319efad5ad8a928e6a874507f7cd586793a65e931249db4213f3f` |
| `avc-444-10-cabac-i.mp4` | `63efa224ce1288a76a4e5a6965a71dd1cf83e879dc6ba2b5ef849be1bdf80b01` |
| `avc-444-10-cabac-i.yuv.gz` | `3604b14cdcd986fddd6da171f6dad46ac62a1310cbdfa10f4a034295bc5b8f19` |
| `avc-444-10-cabac-q45.mp4` | `2921dfbb8e7a8b088b0b100c1ee01a995c995b3f3ff31d0ade2c30c4acfab712` |
| `avc-444-10-cabac-q45.yuv.gz` | `ea3c5337b8f36dbbc497856ccd37783d083b00ea3d729660c76d34faec93c7c3` |
| `avc-444-10-cavlc-b.mp4` | `56cf8ecff3b26013ba51f5c5a37afc54ac773b5690b3aa79743b1cd98f06d851` |
| `avc-444-10-cavlc-b.yuv.gz` | `241fc45dd9bc83da1969424cdc827d63fc1c084082dac8159ebef149b9f87488` |
| `avc-444-10-cavlc-i.mp4` | `d1ed406773994de23a6e729a405f6197e1f8219b22e693f61b4532dae301f4f7` |
| `avc-444-10-cavlc-i.yuv.gz` | `d02b4a1299c810b9f75593d64ee487fe75b7cb23b5acc06b4d0fdbd468fee0e7` |
| `avc-444-10-mbaff.mp4` | `4609bd47bddf04899cb85e47adfd3d072650ea6c445874d0efe7e126395b7ac6` |
| `avc-444-10-mbaff.yuv.gz` | `e1fdca362941c3fbb6241dfc2691befa5ded0ff7e97d6e7f296f894866243cdb` |
| `avc-444-8-cabac-b.mp4` | `e5d701068493d7e7ddff497e379c5d20b37e759f26d916db2024244f57434aa8` |
| `avc-444-8-cabac-b.yuv.gz` | `a20a45ce80fe23517590cca5d9063287fff4fba23be96833efa468add88ab5ec` |
| `avc-444-8-cabac-i.mp4` | `70565a9860c20ada06f35210479ccd60ee7019215eb8c1cb25dddc548fb7a440` |
| `avc-444-8-cabac-i.yuv.gz` | `abf5279d580a84fbdf91b3e56be50d7447f52aa67d65314025c59de2661d882c` |
| `avc-444-8-cabac-q45.mp4` | `b06e5bbb0a0376516e47e415b55b01e39411be789a9e2f8e5a024433afb8d65f` |
| `avc-444-8-cabac-q45.yuv.gz` | `d5952128b2101b3391d89c50bb0d59e67ba38f4b6d8157610600f0b645c031cc` |
| `avc-444-8-cavlc-b.mp4` | `5646d9e89a52c8377848be2b950103354c80bcb2fd687f2c7440c4958efa2782` |
| `avc-444-8-cavlc-b.yuv.gz` | `0f280e963f21ad0d3fa86548c3380ffd7e4ca0b186568840a8c97c7fcdde2d36` |
| `avc-444-8-cavlc-i.mp4` | `418fd92d1a99c3736760da64685dab5377e3dd05340f396e8de92260c859c2bd` |
| `avc-444-8-cavlc-i.yuv.gz` | `01df0dfa894518271add5553170d79af3801254a1cd7aa136f02d2842798d58d` |
| `avc-444-8-mbaff.mp4` | `1236a480fd0dd62eba6e4ee93e5503b0822380b9e96215f0daefa6ea875a193f` |
| `avc-444-8-mbaff.yuv.gz` | `d5cd3af7e8da0fa1f42d53f89b55dbad2d753ab67c78dbf300de8dd8f633fc0c` |
| `avc-444-rgb.mp4` | `ae5d8e3545c3c37a3c6a44fc4546fe8fa7abcb92430dd07378d5ca2eff0651fe` |
| `avc-444-rgb.yuv.gz` | `eb4166ac0a1e0f550e4a0ae8fd8528d0671980df55ab769afbcd22d86eaee12b` |
