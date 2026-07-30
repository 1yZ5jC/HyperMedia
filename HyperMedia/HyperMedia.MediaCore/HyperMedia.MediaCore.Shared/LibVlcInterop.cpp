#include "pch.h"
#include "LibVlcInterop.h"
#include <mutex>

using namespace Platform;

namespace HyperMedia { namespace MediaCore {

#if HYPERMEDIA_HAS_LIBVLC

// --- libVLC Audio Callbacks ---
static void libvlcAudioPlay_cb(void* data, const void* samples, unsigned count, int64_t pts)
{
    LibVlcContextData* ctx = (LibVlcContextData*)data;
    if (!ctx || !samples || count == 0) return;

    std::mutex* mtx = (std::mutex*)ctx->audioMutex;
    int frameBytes = count * ctx->audioChannels * sizeof(int16_t);

    std::lock_guard<std::mutex> lock(*mtx);

    // Simple ring buffer write
    for (int i = 0; i < frameBytes; i++)
    {
        ctx->audioRingBuffer[ctx->audioRingWrite] = ((const uint8_t*)samples)[i];
        ctx->audioRingWrite = (ctx->audioRingWrite + 1) % ctx->audioRingSize;
    }
    ctx->audioRingCount += frameBytes;
    if (ctx->audioRingCount > ctx->audioRingSize)
        ctx->audioRingCount = ctx->audioRingSize;
}

static void libvlcAudioPause_cb(void* data, int64_t pts)
{
}

static void libvlcAudioResume_cb(void* data, int64_t pts)
{
}

static void libvlcAudioFlush_cb(void* data, int64_t pts)
{
    LibVlcContextData* ctx = (LibVlcContextData*)data;
    if (!ctx) return;

    std::mutex* mtx = (std::mutex*)ctx->audioMutex;
    std::lock_guard<std::mutex> lock(*mtx);

    ctx->audioRingWrite = 0;
    ctx->audioRingRead = 0;
    ctx->audioRingCount = 0;
}

static void libvlcAudioDrain_cb(void* data)
{
}

// --- libVLC Video Callbacks ---
static void* libvlcVideoLock_cb(void* data, void** planes)
{
    LibVlcContextData* ctx = (LibVlcContextData*)data;
    if (!ctx || !ctx->videoFrameBuffer) return nullptr;

    std::mutex* mtx = (std::mutex*)ctx->videoMutex;
    mtx->lock();

    planes[0] = ctx->videoFrameBuffer;
    return nullptr;
}

static void libvlcVideoUnlock_cb(void* data, void* picture, void* const* planes)
{
    LibVlcContextData* ctx = (LibVlcContextData*)data;
    if (!ctx) return;

    ctx->videoFrameReady = true;

    std::mutex* mtx = (std::mutex*)ctx->videoMutex;
    mtx->unlock();
}

static void libvlcVideoDisplay_cb(void* data, void* picture)
{
    LibVlcContextData* ctx = (LibVlcContextData*)data;
    if (!ctx) return;

    ctx->videoFramePts = (int64_t)(libvlc_media_player_get_time((libvlc_media_player_t*)ctx->vlcPlayer) * 10000);
}

// --- Media event callbacks ---
static void libvlcMediaParsedChanged_cb(const libvlc_event_t* event, void* data)
{
}

#endif // HYPERMEDIA_HAS_LIBVLC

// --- LibVlcManager ---
bool LibVlcManager::_initialized = false;

LibVlcManager::LibVlcManager() {}
LibVlcManager::~LibVlcManager() {}

void LibVlcManager::Initialize()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_initialized)
    {
        _initialized = true;
    }
#endif
}

void LibVlcManager::Shutdown()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (_initialized)
    {
        _initialized = false;
    }
#endif
}

// --- LibVlcDecoder ---
LibVlcDecoder::LibVlcDecoder()
    : _hasVideo(false)
    , _hasAudio(false)
    , _videoWidth(0)
    , _videoHeight(0)
    , _audioSampleRate(0)
    , _audioChannels(0)
    , _duration(0.0)
    , _ctx(nullptr)
{
    LibVlcManager::Initialize();
    _ctx = new LibVlcContextData();
    memset(_ctx, 0, sizeof(LibVlcContextData));
}

LibVlcDecoder::~LibVlcDecoder()
{
    Close();
    if (_ctx)
    {
        delete _ctx;
        _ctx = nullptr;
    }
}

bool LibVlcDecoder::OpenFile(String^ filePath)
{
#if HYPERMEDIA_HAS_LIBVLC
    Close();

    if (!filePath || filePath->IsEmpty()) return false;

    // Convert to UTF-8
    int bufLen = WideCharToMultiByte(CP_UTF8, 0, filePath->Data(), -1, nullptr, 0, nullptr, nullptr);
    if (bufLen <= 0) return false;
    char* pathUtf8 = new char[bufLen];
    WideCharToMultiByte(CP_UTF8, 0, filePath->Data(), -1, pathUtf8, bufLen, nullptr, nullptr);

    const char* vlcArgs[] = {
        "-I", "dummy",
        "--no-osx",
        "--no-stats",
        "--no-video-title-show",
        "--aout=winstore",
        "--vout=winstore",
        nullptr
    };

    _ctx->vlcInstance = libvlc_new(sizeof(vlcArgs) / sizeof(vlcArgs[0]) - 1, vlcArgs);
    if (!_ctx->vlcInstance)
    {
        delete[] pathUtf8;
        return false;
    }

    _ctx->vlcMedia = libvlc_media_new_path((libvlc_instance_t*)_ctx->vlcInstance, pathUtf8);
    delete[] pathUtf8;

    if (!_ctx->vlcMedia)
    {
        libvlc_release((libvlc_instance_t*)_ctx->vlcInstance);
        _ctx->vlcInstance = nullptr;
        return false;
    }

    _ctx->vlcPlayer = libvlc_media_player_new_from_media((libvlc_media_t*)_ctx->vlcMedia);
    if (!_ctx->vlcPlayer)
    {
        libvlc_media_release((libvlc_media_t*)_ctx->vlcMedia);
        _ctx->vlcMedia = nullptr;
        libvlc_release((libvlc_instance_t*)_ctx->vlcInstance);
        _ctx->vlcInstance = nullptr;
        return false;
    }

    // Parse media info
    libvlc_media_parse((libvlc_media_t*)_ctx->vlcMedia);

    // Get duration
    libvlc_time_t durationMs = libvlc_media_get_duration((libvlc_media_t*)_ctx->vlcMedia);
    _duration = (durationMs > 0) ? (double)durationMs / 1000.0 : 0.0;

    // Wait for tracks to be detected
    libvlc_media_player_play((libvlc_media_player_t*)_ctx->vlcPlayer);

    // Give libVLC time to detect tracks
    libvlc_time_t waitStart = libvlc_clock();
    while (libvlc_clock() - waitStart < 3000)
    {
        _videoWidth = libvlc_video_get_width((libvlc_media_player_t*)_ctx->vlcPlayer);
        _videoHeight = libvlc_video_get_height((libvlc_media_player_t*)_ctx->vlcPlayer);
        if (_videoWidth > 0 && _videoHeight > 0)
            break;
    }

    _hasVideo = (_videoWidth > 0 && _videoHeight > 0);
    _hasAudio = true;
    _audioSampleRate = 44100;
    _audioChannels = 2;

    // Set up audio output
    {
        _ctx->audioMutex = new std::mutex();
        _ctx->audioRingSize = 256 * 1024;
        _ctx->audioRingBuffer = new uint8_t[_ctx->audioRingSize];
        _ctx->audioRingWrite = 0;
        _ctx->audioRingRead = 0;
        _ctx->audioRingCount = 0;

        libvlc_audio_set_callbacks((libvlc_media_player_t*)_ctx->vlcPlayer,
            libvlcAudioPlay_cb,
            libvlcAudioPause_cb,
            libvlcAudioResume_cb,
            libvlcAudioFlush_cb,
            libvlcAudioDrain_cb,
            _ctx);

        libvlc_audio_set_format((libvlc_media_player_t*)_ctx->vlcPlayer,
            "S16N", _audioSampleRate, _audioChannels);
    }

    // Set up video output
    if (_hasVideo)
    {
        _ctx->videoMutex = new std::mutex();
        int frameSize = _videoWidth * _videoHeight * 4;
        _ctx->videoFrameBuffer = new uint8_t[frameSize];
        _ctx->videoFrameWidth = _videoWidth;
        _ctx->videoFrameHeight = _videoHeight;
        _ctx->videoFrameReady = false;
        _ctx->videoFramePts = 0;

        libvlc_video_set_callbacks((libvlc_media_player_t*)_ctx->vlcPlayer,
            libvlcVideoLock_cb,
            libvlcVideoUnlock_cb,
            libvlcVideoDisplay_cb,
            _ctx);

        libvlc_video_set_format((libvlc_media_player_t*)_ctx->vlcPlayer,
            "RV32", _videoWidth, _videoHeight, _videoWidth * 4);
    }

    libvlc_media_player_pause((libvlc_media_player_t*)_ctx->vlcPlayer);

    return (_hasVideo || _hasAudio);
#else
    return false;
#endif
}

void LibVlcDecoder::Close()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (_ctx->vlcPlayer)
    {
        libvlc_media_player_stop((libvlc_media_player_t*)_ctx->vlcPlayer);
        libvlc_media_player_release((libvlc_media_player_t*)_ctx->vlcPlayer);
        _ctx->vlcPlayer = nullptr;
    }
    if (_ctx->vlcMedia)
    {
        libvlc_media_release((libvlc_media_t*)_ctx->vlcMedia);
        _ctx->vlcMedia = nullptr;
    }
    if (_ctx->vlcInstance)
    {
        libvlc_release((libvlc_instance_t*)_ctx->vlcInstance);
        _ctx->vlcInstance = nullptr;
    }

    if (_ctx->audioMutex)
    {
        delete (std::mutex*)_ctx->audioMutex;
        _ctx->audioMutex = nullptr;
    }
    if (_ctx->audioRingBuffer)
    {
        delete[] _ctx->audioRingBuffer;
        _ctx->audioRingBuffer = nullptr;
    }
    if (_ctx->videoMutex)
    {
        delete (std::mutex*)_ctx->videoMutex;
        _ctx->videoMutex = nullptr;
    }
    if (_ctx->videoFrameBuffer)
    {
        delete[] _ctx->videoFrameBuffer;
        _ctx->videoFrameBuffer = nullptr;
    }

    _hasVideo = false;
    _hasAudio = false;
    _videoWidth = 0;
    _videoHeight = 0;
    _audioSampleRate = 0;
    _audioChannels = 0;
    _duration = 0.0;
#endif
}

DecodedVideoFrame^ LibVlcDecoder::ReadNextVideoFrame()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_hasVideo || !_ctx->videoFrameBuffer)
        return nullptr;

    std::mutex* mtx = (std::mutex*)_ctx->videoMutex;
    std::lock_guard<std::mutex> lock(*mtx);

    if (!_ctx->videoFrameReady)
        return nullptr;

    int bgraSize = _videoWidth * _videoHeight * 4;
    auto frame = ref new DecodedVideoFrame();
    frame->_width = _videoWidth;
    frame->_height = _videoHeight;
    frame->_timestamp = _ctx->videoFramePts;
    frame->_data = ref new Platform::Array<uint8_t>(_ctx->videoFrameBuffer, bgraSize);

    _ctx->videoFrameReady = false;
    return frame;
#else
    return nullptr;
#endif
}

DecodedAudioFrame^ LibVlcDecoder::ReadNextAudioFrame()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_hasAudio || !_ctx->audioRingBuffer)
        return nullptr;

    std::mutex* mtx = (std::mutex*)_ctx->audioMutex;
    std::lock_guard<std::mutex> lock(*mtx);

    int available = _ctx->audioRingCount;
    if (available < 4096)
        return nullptr;

    int frameBytes = (available / 4096) * 4096;
    if (frameBytes > 65536)
        frameBytes = 65536;

    auto audioData = ref new Platform::Array<uint8_t>(frameBytes);
    for (int i = 0; i < frameBytes; i++)
    {
        audioData[i] = _ctx->audioRingBuffer[_ctx->audioRingRead];
        _ctx->audioRingRead = (_ctx->audioRingRead + 1) % _ctx->audioRingSize;
    }
    _ctx->audioRingCount -= frameBytes;

    int sampleCount = frameBytes / (_audioChannels * sizeof(int16_t));

    auto frame = ref new DecodedAudioFrame();
    frame->_data = audioData;
    frame->_sampleCount = sampleCount;
    frame->_sampleRate = _audioSampleRate;
    frame->_channels = _audioChannels;
    frame->_timestamp = 0;

    return frame;
#else
    return nullptr;
#endif
}

void LibVlcDecoder::SeekTo(double seconds)
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_ctx->vlcPlayer) return;

    _ctx->seeking = true;

    libvlc_media_player_set_time((libvlc_media_player_t*)_ctx->vlcPlayer,
        (libvlc_time_t)(seconds * 1000));

    // Flush audio buffer
    if (_ctx->audioRingBuffer)
    {
        std::lock_guard<std::mutex> lock(*(std::mutex*)_ctx->audioMutex);
        _ctx->audioRingWrite = 0;
        _ctx->audioRingRead = 0;
        _ctx->audioRingCount = 0;
    }

    _ctx->videoFrameReady = false;
    _ctx->seeking = false;
#endif
}

}}
