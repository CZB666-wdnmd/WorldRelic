#include <stdio.h>
#include <string.h>
#include <ctype.h>
#include <sys/system_properties.h>
#include <android/log.h>
#include <dlfcn.h>

static int contains(const char *s, const char *word)
{
    if (!s) return 0;
    size_t n = strlen(word);
    for (; *s; ++s) {
        size_t i = 0;
        while (i < n && s[i] && tolower((unsigned char)s[i]) == word[i]) ++i;
        if (i == n) return 1;
    }
    return 0;
}

// Same known-headset policy as AndroidXRDeviceCapability, evaluated before
// Unity creates the graphics device. Explicit native preferences take priority.
int WorldRelicShouldPreInit(const char *maker, const char *brand, const char *model, const char *mode)
{
    if (mode && strcmp(mode, "flat") == 0) return 0;
    if (mode && strcmp(mode, "xr") == 0) return 1;
    const char *ids[] = { maker, brand, model };
    for (size_t i = 0; i < 3; ++i)
        if (contains(ids[i], "pico") || contains(ids[i], "oculus") || contains(ids[i], "quest")) return 1;
    return 0;
}

static void read_mode(char mode[8])
{
    strcpy(mode, "auto");
    // The shared preferences belong to this app and are read in its own UID.
    // Never rewrite the file or log its contents. Default is safely phone-flat.
    FILE *f = fopen("/data/data/local.worldrelic.player/shared_prefs/worldrelic_settings.xml", "rb");
    if (!f) return;
    char xml[8192]; size_t n = fread(xml, 1, sizeof(xml) - 1, f); fclose(f); xml[n] = '\0';
    char *key = strstr(xml, "name=\"xr_mode\"");
    if (!key) return;
    char *value = strchr(key, '>');
    if (!value) return;
    ++value;
    if (strncmp(value, "flat</", 6) == 0) strcpy(mode, "flat");
    else if (strncmp(value, "xr</", 4) == 0) strcpy(mode, "xr");
}

__attribute__((visibility("default"))) void XRSDKPreInit(void *unityInterfaces)
{
    char maker[PROP_VALUE_MAX] = {0}, brand[PROP_VALUE_MAX] = {0}, model[PROP_VALUE_MAX] = {0}, mode[8];
    __system_property_get("ro.product.manufacturer", maker);
    __system_property_get("ro.product.brand", brand);
    __system_property_get("ro.product.model", model);
    read_mode(mode);
    if (!WorldRelicShouldPreInit(maker, brand, model, mode)) {
        __android_log_print(ANDROID_LOG_INFO, "WorldRelicXRBoot", "PreInit skipped: ordinary device or force-flat; mode=%s", mode);
        return;
    }
    // Forward the unchanged Unity interface pointer to Unity's own provider.
    // It registers its EGL flags before graphics creation. Keep its handle for
    // process lifetime; registered provider callbacks must never be unloaded.
    static void *provider;
    if (!provider) provider = dlopen("libUnityOpenXR.so", RTLD_NOW | RTLD_GLOBAL);
    if (!provider) {
        __android_log_print(ANDROID_LOG_ERROR, "WorldRelicXRBoot", "Cannot load OpenXR preinit provider: %s", dlerror());
        return;
    }
    void (*preinit)(void *) = (void (*)(void *))dlsym(provider, "XRSDKPreInit");
    if (!preinit) {
        __android_log_print(ANDROID_LOG_ERROR, "WorldRelicXRBoot", "OpenXR provider has no XRSDKPreInit export");
        return;
    }
    __android_log_print(ANDROID_LOG_INFO, "WorldRelicXRBoot", "Forwarding Unity OpenXR PreInit: manufacturer=%s model=%s mode=%s", maker, model, mode);
    preinit(unityInterfaces);
}

#ifdef WORLDRELIC_PREINIT_TEST_MAIN
int main(void)
{
    struct { const char *m, *b, *d, *mode; int expected; } cases[] = {
        {"Pico", "Pico", "Pico Neo 3", "auto", 1},
        {"PICO", "pico", "PICO 4", "auto", 1},
        {"Oculus", "Meta", "Quest 3", "auto", 1},
        {"Google", "google", "Pixel 5", "auto", 0},
        {"Samsung", "samsung", "SM-S928B", "auto", 0},
        {"Xiaomi", "Redmi", "23049RAD8C", "auto", 0},
        {"Pico", "Pico", "Pico Neo 3", "flat", 0},
        {"Google", "google", "Pixel 5", "xr", 1},
        {NULL, NULL, NULL, "auto", 0}, {"", "", "", NULL, 0},
        {"P", "Q", "O", "auto", 0},
    };
    int failed = 0;
    for (size_t i = 0; i < sizeof(cases) / sizeof(cases[0]); i++)
        if (WorldRelicShouldPreInit(cases[i].m, cases[i].b, cases[i].d, cases[i].mode) != cases[i].expected) { printf("FAIL %zu\n", i); failed++; }
    char mode[8]; read_mode(mode);
    printf("PreInit policy: %zu cases, failures=%d; current native preference=%s\n", sizeof(cases)/sizeof(cases[0]), failed, mode);
    return failed ? 1 : 0;
}
#endif
