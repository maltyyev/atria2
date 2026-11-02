#include <stdint.h>
int32_t atria_start(const char *bundle, const char *storage);
void atria_suspend(void);
void atria_resume(void);
int32_t atria_stop(void);
int32_t atria_wait_stopped(void);
void atria_surface(int32_t width, int32_t height, int32_t focused);
void atria_pad(int32_t slot, int32_t connected, uint16_t buttons, uint8_t leftTrigger, uint8_t rightTrigger,
               int16_t lx, int16_t ly, int16_t rx, int16_t ry);
void atria_touch_pad(uint16_t buttons, uint8_t leftTrigger, uint8_t rightTrigger, int16_t x, int16_t y);
void atria_touch_activity(int32_t identity);
void atria_key(uint8_t scan, int32_t down);
void atria_pointer(int32_t x, int32_t y, int32_t down);
void atria_text(const char *text);
int32_t atria_frame(uint32_t *pixels, int32_t capacity, int32_t *width, int32_t *height);
int32_t atria_error(char *buffer, int32_t capacity);
