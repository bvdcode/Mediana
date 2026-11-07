// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

#include <libheif/heif.h>
#include <stdio.h>
#include <string.h>

static int check(struct heif_error error)
{
    if (error.code == heif_error_Ok) {
        return 1;
    }
    fprintf(stderr, "%s\n", error.message);
    return 0;
}

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 4) {
        fprintf(stderr, "Usage: heif-reference input.heic output.rgba [nearest|bilinear]\n");
        return 1;
    }
    const char* filter = argc == 4 ? argv[3] : "bilinear";
    enum heif_chroma_upsampling_algorithm upsampling;
    if (strcmp(filter, "nearest") == 0) {
        upsampling = heif_chroma_upsampling_nearest_neighbor;
    } else if (strcmp(filter, "bilinear") == 0) {
        upsampling = heif_chroma_upsampling_bilinear;
    } else {
        fprintf(stderr, "Supported chroma filters: nearest, bilinear.\n");
        return 1;
    }
    int result = 1;
    struct heif_context* context = heif_context_alloc();
    struct heif_image_handle* handle = NULL;
    struct heif_image* image = NULL;
    struct heif_decoding_options* options = heif_decoding_options_alloc();
    FILE* output = NULL;
    if (context == NULL || options == NULL) {
        fprintf(stderr, "Cannot allocate libheif context/options\n");
        goto cleanup;
    }
    heif_context_set_max_decoding_threads(context, 2);
    options->decoder_id = "libde265";
    options->strict_decoding = 1;
    options->num_codec_threads = 2;
    options->output_image_nclx_profile_passthrough = 1;
    options->color_conversion_options.preferred_chroma_upsampling_algorithm =
        upsampling;
    options->color_conversion_options.only_use_preferred_chroma_algorithm = 1;
    if (!check(heif_context_read_from_file(context, argv[1], NULL)) ||
        !check(heif_context_get_primary_image_handle(context, &handle)) ||
        !check(heif_decode_image(handle, &image, heif_colorspace_RGB,
                                heif_chroma_interleaved_RGBA, options))) {
        goto cleanup;
    }
    int width = heif_image_get_width(image, heif_channel_interleaved);
    int height = heif_image_get_height(image, heif_channel_interleaved);
    int stride = 0;
    const unsigned char* pixels = heif_image_get_plane_readonly(
        image, heif_channel_interleaved, &stride);
    output = fopen(argv[2], "wbx");
    if (output == NULL || pixels == NULL || width <= 0 || height <= 0) {
        fprintf(stderr, "Cannot access decoded pixels or create output\n");
        goto cleanup;
    }
    for (int row = 0; row < height; row++) {
        if (fwrite(pixels + (size_t)row * stride, 4, (size_t)width, output) !=
            (size_t)width) {
            fprintf(stderr, "Cannot write decoded pixels\n");
            goto cleanup;
        }
    }
    if (fclose(output) != 0) {
        output = NULL;
        fprintf(stderr, "Cannot finalize decoded pixels\n");
        goto cleanup;
    }
    output = NULL;
    printf("%dx%d; source-color RGBA8; chroma %s; libheif %s\n", width, height,
           filter, heif_get_version());
    result = 0;

cleanup:
    if (output != NULL) { fclose(output); }
    if (image != NULL) { heif_image_release(image); }
    if (handle != NULL) { heif_image_handle_release(handle); }
    if (options != NULL) { heif_decoding_options_free(options); }
    if (context != NULL) { heif_context_free(context); }
    return result;
}
