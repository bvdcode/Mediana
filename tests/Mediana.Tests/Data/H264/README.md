# H.264 arithmetic references

`cabac-intra-contexts.bin` contains packed initial CABAC states for progressive
I-slice contexts. It follows ITU-T H.264 V15 (08/2024), tables 9-12 and 9-16
through 9-21 and equation 9-5.

The outer order is SliceQPY from 0 through 51. Within each quantizer, context
indices ascend through 3–10, 60–275 and 399–401. Each byte stores
`2 * pStateIdx + valMPS`. There are 227 contexts per quantizer, 11,804 bytes.
The reference was calculated separately with integer floor division, clipping
the intermediate state to 1–126 before packing the probability state and symbol.

SHA-256: `f1eacad024365024b15462922c89aa1ebff91f9071dfb8b67047aece8a93bb77`.

`cabac-predictive-contexts.bin` contains the corresponding progressive P-slice
states under tables 9-13 and 9-15 through 9-21. The outer order is
`cabac_init_idc` 0, 1, 2, then SliceQPY 0 through 51, then context indices
11–23, 40–275 and 399–401. There are 252 contexts per selection and quantizer,
39,312 bytes total. Packing and separate integer calculation follow the same
definition as the I-slice reference.

SHA-256: `6e2bfd1bb2cdd4117e36f6b699fa85eb930a200c087dc092dc74c4bd042bb9fa`.

Source: [ITU-T H.264 V15 (08/2024)](https://www.itu.int/rec/T-REC-H.264-202408-S/en).
These are arithmetic initialization references; they do not prove complete
CABAC picture reconstruction.
