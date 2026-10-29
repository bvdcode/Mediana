# JBIG2 reference images

The 98 original synthetic fixtures originate from Render.NET commit `03cb502`,
under the MIT license, copyright 2026 Vadim Belov. Encoded files, authored packed
pixel references and manifest hashes are preserved.

The originals were checked independently with PDFBox JBIG2 0.1.16.2: all 98
pixel arrays matched. Poppler 24.02.0 matched 93; the five differences involve
typical refinement and retained bitmap contexts. This is differential regression
coverage, not complete T.88 conformance. Comparison tools are not dependencies.

Pixels are packed MSB-first, one means black, zero means white, with zero tail
bits. Cases include arithmetic and MMR generic regions, text/symbol dictionaries,
refinement, halftones, standard/custom Huffman tables and retained contexts.
