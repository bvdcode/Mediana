/* SPDX-License-Identifier: MIT
 * Copyright (c) 2026 Vadim Belov <https://belov.us>
 */

#include <stdio.h>
#include <stdlib.h>
#include <webp/demux.h>

int main(int argc, char **argv) {
    if (argc != 3) { fprintf(stderr, "Expected input WebP and output RGBA.\n"); return 2; }
    FILE *input = fopen(argv[1], "rb");
    if (!input || fseek(input, 0, SEEK_END) != 0) { perror("input"); return 1; }
    long length = ftell(input);
    if (length <= 0 || fseek(input, 0, SEEK_SET) != 0) { fprintf(stderr, "Invalid input length.\n"); return 1; }
    uint8_t *encoded = malloc((size_t)length);
    if (!encoded || fread(encoded, 1, (size_t)length, input) != (size_t)length || fclose(input) != 0) {
        fprintf(stderr, "Cannot read complete input.\n"); return 1;
    }
    WebPData data = { encoded, (size_t)length };
    WebPAnimDecoderOptions options;
    if (!WebPAnimDecoderOptionsInit(&options)) { fprintf(stderr, "WebP ABI mismatch.\n"); return 1; }
    options.color_mode = MODE_RGBA;
    options.use_threads = 1;
    WebPAnimDecoder *decoder = WebPAnimDecoderNew(&data, &options);
    WebPAnimInfo info;
    if (!decoder || !WebPAnimDecoderGetInfo(decoder, &info)) { fprintf(stderr, "Invalid animation.\n"); return 1; }
    FILE *output = fopen(argv[2], "wbx");
    if (!output) { perror("output"); return 1; }
    unsigned frames = 0;
    size_t bytes = (size_t)info.canvas_width * info.canvas_height * 4;
    while (WebPAnimDecoderHasMoreFrames(decoder)) {
        uint8_t *rgba = NULL;
        int timestamp = 0;
        if (!WebPAnimDecoderGetNext(decoder, &rgba, &timestamp) || fwrite(rgba, 1, bytes, output) != bytes) {
            fprintf(stderr, "Incomplete animation decode or write.\n"); return 1;
        }
        frames++;
    }
    if (frames != info.frame_count || fclose(output) != 0) { fprintf(stderr, "Incomplete output.\n"); return 1; }
    fprintf(stderr, "frames=%u; libwebp_demux=0x%x\n", frames, WebPGetDemuxVersion());
    WebPAnimDecoderDelete(decoder);
    free(encoded);
    return 0;
}
