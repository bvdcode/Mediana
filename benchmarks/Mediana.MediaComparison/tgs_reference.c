/* SPDX-License-Identifier: MIT
 * Copyright (c) 2026 Vadim Belov <https://belov.us>
 */

#include <rlottie_capi.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <zlib.h>

int main(int argc, char **argv) {
    if (argc != 3) { fprintf(stderr, "Expected input TGS and output RGBA.\n"); return 2; }
    gzFile input = gzopen(argv[1], "rb");
    if (!input) { fprintf(stderr, "Cannot open animation.\n"); return 1; }
    const size_t limit = 32 * 1024 * 1024;
    char *json = malloc(limit + 1);
    if (!json) { fprintf(stderr, "Cannot allocate JSON input.\n"); return 1; }
    size_t length = 0;
    int count;
    while ((count = gzread(input, json + length, (unsigned)(limit - length))) > 0) {
        length += (size_t)count;
        if (length == limit) { fprintf(stderr, "JSON exceeds input limit.\n"); return 1; }
    }
    if (count < 0 || gzclose(input) != Z_OK) { fprintf(stderr, "Invalid gzip input.\n"); return 1; }
    json[length] = '\0';
    Lottie_Animation *animation = lottie_animation_from_data(json, "comparison", "");
    free(json);
    if (!animation) { fprintf(stderr, "Invalid Lottie document.\n"); return 1; }
    size_t width = 0, height = 0;
    lottie_animation_get_size(animation, &width, &height);
    size_t frames = lottie_animation_get_totalframe(animation);
    if (!width || !height || width > 4096 || height > 4096 || !frames) { fprintf(stderr, "Invalid animation dimensions.\n"); return 1; }
    size_t pixels = width * height;
    uint32_t *surface = calloc(pixels, sizeof(uint32_t));
    uint8_t *rgba = malloc(pixels * 4);
    FILE *output = fopen(argv[2], "wbx");
    if (!surface || !rgba || !output) { fprintf(stderr, "Cannot create output.\n"); return 1; }
    for (size_t frame = 0; frame < frames; frame++) {
        memset(surface, 0, pixels * 4);
        lottie_animation_render(animation, frame, surface, width, height, width * 4);
        for (size_t pixel = 0; pixel < pixels; pixel++) {
            uint32_t argb = surface[pixel];
            unsigned alpha = argb >> 24;
            rgba[pixel * 4 + 3] = (uint8_t)alpha;
            for (unsigned channel = 0; channel < 3; channel++) {
                unsigned component = (argb >> (16 - 8 * channel)) & 255;
                rgba[pixel * 4 + channel] = alpha ? (uint8_t)((component * 255 + alpha / 2) / alpha) : 0;
            }
        }
        if (fwrite(rgba, 4, pixels, output) != pixels) { fprintf(stderr, "Incomplete frame write.\n"); return 1; }
    }
    if (fclose(output) != 0) { perror("output"); return 1; }
    fprintf(stderr, "frames=%zu; dimensions=%zux%zu\n", frames, width, height);
    free(rgba);
    free(surface);
    lottie_animation_destroy(animation);
    return 0;
}
