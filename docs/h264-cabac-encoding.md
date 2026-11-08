The arithmetic encoder follows the decision, bypass, carry and termination
processes in [ITU-T H.264, clause 9.3.4](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
It shares the existing probability states and normative range tables with
decoding. Outstanding bits are emitted in bounded words; no source picture,
coefficient array or output copy is retained by the arithmetic engine.

The caller owns the NAL writer. A terminating bin supplies the arithmetic flush,
the stop bit and byte alignment. `H264BitWriter.TryFinishCompletedRbsp` then
publishes that completed RBSP without adding another stop bit. The same engine
termination can precede I_PCM samples and a fresh arithmetic engine, retaining
the probability contexts.

## Arithmetic verification

All 64 probability states, both most-probable symbols and four decision patterns
are checked in 512 mixed sequences. Each includes decisions, zero to 32 bypass
bits, false termination bins and final termination. The resulting code point
must lie inside an independently subdivided arbitrary-precision interval. A
separate streaming decode verifies every bin and final context state. Checks
also cover the initial flush, invalid widths, alignment, terminal-state access
and released packet storage.

This component supplies arithmetic encoding. Public picture encoding still uses
CAVLC; macroblock binarization, residual integration and complete-operation
CPU, size and quality comparisons remain separate work.
