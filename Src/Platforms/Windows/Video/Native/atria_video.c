/* Narrow ABI adapter only. Demuxing, timing, audio and gameplay are C# owned. */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavutil/error.h>
#include <libswscale/swscale.h>

#define API __declspec(dllexport)
typedef struct AtriaVideo {
    AVCodecContext *codec;
    AVPacket *packet;
    AVFrame *frame;
    struct SwsContext *conversion;
    int width, height;
} AtriaVideo;

API void atria_video_close(AtriaVideo *v) {
    if (!v) return;
    sws_freeContext(v->conversion);
    av_frame_free(&v->frame);
    av_packet_free(&v->packet);
    avcodec_free_context(&v->codec);
    free(v);
}

API AtriaVideo *atria_video_open(int width, int height) {
    if (width <= 0 || width > 4096 || height <= 0 || height > 4096) return NULL;
    const AVCodec *codec = avcodec_find_decoder_by_name("indeo3");
    if (!codec) return NULL;
    AtriaVideo *v = calloc(1, sizeof(*v));
    if (!v) return NULL;
    v->width = width; v->height = height;
    v->codec = avcodec_alloc_context3(codec);
    v->packet = av_packet_alloc(); v->frame = av_frame_alloc();
    if (!v->codec || !v->packet || !v->frame) goto fail;
    v->codec->width = width; v->codec->height = height;
    v->codec->thread_count = 1;
    if (avcodec_open2(v->codec, codec, NULL) < 0) goto fail;
    return v;
fail:
    atria_video_close(v); return NULL;
}

/* One IV32 packet = one picture. AVI empty chunks repeat the previous picture in C#. */
API int atria_video_decode(AtriaVideo *v, const uint8_t *packet, int size,
                          uint8_t *bgra, int output_size) {
    if (!v || !packet || size <= 0 || size > 16777216 || !bgra ||
        output_size != v->width * v->height * 4) return AVERROR(EINVAL);
    av_packet_unref(v->packet);
    int result = av_new_packet(v->packet, size); /* includes required zero padding */
    if (result < 0) return result;
    memcpy(v->packet->data, packet, size);
    result = avcodec_send_packet(v->codec, v->packet);
    av_packet_unref(v->packet);
    if (result < 0) return result;
    result = avcodec_receive_frame(v->codec, v->frame);
    if (result < 0) return result;
    if (v->frame->width != v->width || v->frame->height != v->height) {
        av_frame_unref(v->frame); return AVERROR_INVALIDDATA;
    }
    if (!v->conversion)
        v->conversion = sws_getContext(v->width, v->height, v->frame->format,
            v->width, v->height, AV_PIX_FMT_BGRA, SWS_BICUBIC, NULL, NULL, NULL);
    if (!v->conversion) { av_frame_unref(v->frame); return AVERROR(ENOMEM); }
    uint8_t *planes[4] = { bgra, NULL, NULL, NULL };
    int strides[4] = { v->width * 4, 0, 0, 0 };
    result = sws_scale(v->conversion, (const uint8_t *const *)v->frame->data,
        v->frame->linesize, 0, v->height, planes, strides);
    av_frame_unref(v->frame);
    return result == v->height ? 0 : (result < 0 ? result : AVERROR_INVALIDDATA);
}

API int atria_video_error(int code, char *buffer, int size) {
    return size > 0 ? av_strerror(code, buffer, (size_t)size) : AVERROR(EINVAL);
}
