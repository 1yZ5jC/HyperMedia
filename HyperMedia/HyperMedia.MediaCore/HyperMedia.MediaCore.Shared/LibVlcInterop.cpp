#include "pch.h"
#include "LibVlcInterop.h"
#include <mutex>
#include <vector>
#include <d3d11.h>

using namespace Platform;
using namespace Windows::ApplicationModel;

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

// --- Hardware decode capability probe (D3D11 video decoder profiles) ---
// DXVA2 / D3D11 decoder profile GUIDs (identical constants across d3d11.h and dxva2api.h).
namespace
{
    const GUID kGpuH264NoFgt   = { 0x1b81be64, 0xa0c7, 0x11d3, { 0xb9, 0x84, 0x00, 0xc0, 0x4f, 0x2e, 0x73, 0xc5 } };
    const GUID kGpuH264Fgt     = { 0x1b81be65, 0xa0c7, 0x11d3, { 0xb9, 0x84, 0x00, 0xc0, 0x4f, 0x2e, 0x73, 0xc5 } };
    const GUID kGpuHevcMain    = { 0x5f11f7e4, 0x4d0a, 0x4f73, { 0x8b, 0x0f, 0x8c, 0x0f, 0x5b, 0x8f, 0x41, 0x1f } };
    const GUID kGpuHevcMain10  = { 0x7374e49d, 0xc60e, 0x4b40, { 0x9b, 0x6e, 0x18, 0x44, 0x4d, 0x59, 0x14, 0x1c } };
}

static int s_hardwareDecodeGrade = -1;

int LibVlcManager::GetHardwareDecodeGrade()
{
    if (s_hardwareDecodeGrade >= 0)
        return s_hardwareDecodeGrade;

    s_hardwareDecodeGrade = 0;

#if HYPERMEDIA_HAS_LIBVLC
    ID3D11Device* device = nullptr;
    HRESULT hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION,
        &device, nullptr, nullptr);
    if (FAILED(hr) || !device)
        return s_hardwareDecodeGrade;

    ID3D11VideoDevice* videoDevice = nullptr;
    if (SUCCEEDED(device->QueryInterface(__uuidof(ID3D11VideoDevice), (void**)&videoDevice)) && videoDevice)
    {
        bool hasH264 = false;
        bool hasHevc8 = false;
        bool hasHevc10 = false;

        UINT count = videoDevice->GetVideoDecoderProfileCount();
        for (UINT i = 0; i < count; i++)
        {
            GUID profile;
            if (SUCCEEDED(videoDevice->GetVideoDecoderProfile(i, &profile)))
            {
                if (profile == kGpuH264NoFgt || profile == kGpuH264Fgt)
                    hasH264 = true;
                else if (profile == kGpuHevcMain10)
                    hasHevc10 = true;
                else if (profile == kGpuHevcMain)
                    hasHevc8 = true;
            }
        }

        if (hasH264) s_hardwareDecodeGrade = 1;
        if (hasHevc8) s_hardwareDecodeGrade = 2;
        if (hasHevc10) s_hardwareDecodeGrade = 3;

        videoDevice->Release();
    }
    device->Release();
#endif

    return s_hardwareDecodeGrade;
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

    // Build libVLC args dynamically so we can point at the app's plugin folder
    // (without --plugin-path, libvlc_new fails to load modules and OpenFile fails).
    // Use dummy aout/vout: this component has no XAML host, and the winstore
    // output modules require one. Audio is captured via libvlc_audio_set_callbacks.
    std::vector<std::string> argStrings = {
        "-I", "dummy",
        "--no-plugins-cache",
        "--no-osd",
        "--no-stats",
        "--no-loop",
        "--no-video-title-show",
        "--aout=adummy",
        "--vout=vdummy"
    };

    // Resolve the plugin directory from the install location, if available
    Platform::String^ pluginDir = nullptr;
    try
    {
        auto pkg = Windows::ApplicationModel::Package::Current;
        if (pkg != nullptr && pkg->InstalledLocation != nullptr)
            pluginDir = pkg->InstalledLocation->Path + "\\plugins";
    }
    catch (...) { pluginDir = nullptr; }

    if (pluginDir != nullptr && !pluginDir->IsEmpty())
    {
        int pbLen = WideCharToMultiByte(CP_UTF8, 0, pluginDir->Data(), -1, nullptr, 0, nullptr, nullptr);
        if (pbLen > 0)
        {
            std::string pluginPathArg = "--plugin-path=";
            std::string dirUtf8(pbLen, '\0');
            WideCharToMultiByte(CP_UTF8, 0, pluginDir->Data(), -1, &dirUtf8[0], pbLen, nullptr, nullptr);
            if (dirUtf8.size() > 0 && dirUtf8.back() == '\0') dirUtf8.pop_back();
            pluginPathArg += dirUtf8;
            argStrings.push_back(pluginPathArg);
        }
    }

    std::vector<const char*> vlcArgs;
    vlcArgs.reserve(argStrings.size() + 1);
    for (const auto& s : argStrings)
        vlcArgs.push_back(s.c_str());
    vlcArgs.push_back(nullptr);

    _ctx->vlcInstance = libvlc_new((int)vlcArgs.size() - 1, vlcArgs.data());
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

    // NOTE: audio callbacks must be installed BEFORE play() starts the output
    // pipeline; installing them afterwards means the default aout grabs the
    // audio and our play callback is never invoked (ring stays empty).
    _hasAudio = true;
    _audioSampleRate = 44100;
    _audioChannels = 2;
    _ctx->audioSampleRate = _audioSampleRate;   // used by the audio play callback
    _ctx->audioChannels = _audioChannels;       // used by the audio play callback

    // Set up audio output
    {
        _ctx->audioMutex = new std::mutex();
        // ~11.6s of 44.1k stereo 16-bit; must comfortably exceed any
        // CollectAudioPcm target so the target duration is not truncated.
        _ctx->audioRingSize = 2 * 1024 * 1024;
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

    // Now start playback and wait for tracks to be detected
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

Platform::Array<int16_t>^ LibVlcDecoder::CollectAudioPcm(double seconds)
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_ctx->vlcPlayer || !_ctx->audioRingBuffer) return nullptr;

    std::mutex* mtx = (std::mutex*)_ctx->audioMutex;

    // Resume playback so the audio callback keeps producing samples
    libvlc_media_player_play((libvlc_media_player_t*)_ctx->vlcPlayer);

    int bytesPerSecond = _audioSampleRate * _audioChannels * (int)sizeof(int16_t);
    // Keep the requested duration; the ring is sized (2MB) well beyond this
    // so no truncation occurs. Read side drains continuously, so the ring
    // never overflows.
    int targetBytes = (int)(bytesPerSecond * seconds);

    auto collected = ref new Platform::Collections::Vector<int16_t>();
    libvlc_time_t startTick = libvlc_clock();
    int64_t lastTick = 0;

    while (collected->Size < (unsigned)(targetBytes / (int)sizeof(int16_t)))
    {
        {
            std::lock_guard<std::mutex> lock(*mtx);
            int available = _ctx->audioRingCount;
            if (available >= 4096)
            {
                int readBytes = (available / 4096) * 4096;
                if (readBytes > 65536) readBytes = 65536;
                for (int i = 0; i < readBytes; i += 2)
                {
                    int16_t sample = (int16_t)((uint8_t)_ctx->audioRingBuffer[_ctx->audioRingRead] |
                        ((uint8_t)_ctx->audioRingBuffer[(_ctx->audioRingRead + 1) % _ctx->audioRingSize] << 8));
                    collected->Append(sample);
                    _ctx->audioRingRead = (_ctx->audioRingRead + 2) % _ctx->audioRingSize;
                }
                _ctx->audioRingCount -= readBytes;
            }
        }

        // Stop when the media finishes or a timeout far beyond the target elapses
        libvlc_time_t now = libvlc_clock();
        if (libvlc_media_player_get_state((libvlc_media_player_t*)_ctx->vlcPlayer) == libvlc_Ended)
            break;
        if (now - startTick > (libvlc_time_t)((seconds + 8.0) * 1000000))
            break;

        // Sleep a little to let the callback fill the ring
        if (libvlc_clock() - lastTick > 20000)
        {
            lastTick = libvlc_clock();
            ::Sleep(10);
        }
    }

    libvlc_media_player_pause((libvlc_media_player_t*)_ctx->vlcPlayer);

    if (collected->Size == 0) return nullptr;

    auto result = ref new Platform::Array<int16_t>(collected->Size);
    for (unsigned i = 0; i < collected->Size; i++)
        result[i] = collected->GetAt(i);
    return result;
#else
    return nullptr;
#endif
}

void LibVlcDecoder::SetPlayPause(bool playing)
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_ctx->vlcPlayer) return;
    if (playing)
        libvlc_media_player_play((libvlc_media_player_t*)_ctx->vlcPlayer);
    else
        libvlc_media_player_pause((libvlc_media_player_t*)_ctx->vlcPlayer);
#else
    (void)playing;
#endif
}

Platform::Array<float>^ LibVlcDecoder::ScanWaveform()
{
#if HYPERMEDIA_HAS_LIBVLC
    if (!_ctx || !_ctx->vlcPlayer || !_ctx->audioRingBuffer || !_hasAudio) return nullptr;

    std::mutex* mtx = (std::mutex*)_ctx->audioMutex;

    // 2nd-order Butterworth low-pass filters @44.1kHz:
    //   band_low  = lp(250Hz)
    //   band_mid  = lp(4500Hz) - lp(250Hz)
    //   band_high = raw - lp(4500Hz)
    const double fs = 44100.0;
    const double pi = 3.14159265358979323846;

    struct LpCoef { double b0, b1, b2, a1, a2; };
    auto makeLowPass = [&](double fc) -> LpCoef
    {
        double w = 2.0 * pi * fc / fs;
        double c = cos(w), s = sin(w), alpha = s * 0.7071067811865476;
        double a0 = 1.0 + alpha;
        LpCoef k;
        k.b0 = ((1.0 - c) / 2.0) / a0;
        k.b1 = ((1.0 - c)) / a0;
        k.b2 = ((1.0 - c) / 2.0) / a0;
        k.a1 = (-2.0 * c) / a0;
        k.a2 = (1.0 - alpha) / a0;
        return k;
    };
    LpCoef lpLow = makeLowPass(250.0);
    LpCoef lpHigh = makeLowPass(4500.0);

    // Per-channel filter states: [x1, x2, y1, y2] for each of the two low-passes.
    const int maxCh = 2;
    double stLow[maxCh][4] = {};
    double stHigh[maxCh][4] = {};

    const int windowSamples = (int)(fs * 0.05);          // 50 ms
    const int bytesPerFrame = _audioChannels * (int)sizeof(int16_t);

    std::vector<double> lowAcc, midAcc, highAcc, envAcc, peakAcc;

    double lowSum = 0, midSum = 0, highSum = 0, envSum = 0, peakMax = 0;
    int windowCount = 0;

    libvlc_media_player_set_rate((libvlc_media_player_t*)_ctx->vlcPlayer, 16.0f);
    libvlc_media_player_play((libvlc_media_player_t*)_ctx->vlcPlayer);

    libvlc_time_t startTick = libvlc_clock();
    libvlc_time_t lastDataTick = libvlc_clock();
    bool finished = false;

    while (!finished)
    {
        {
            std::lock_guard<std::mutex> lock(*mtx);
            while (_ctx->audioRingCount >= bytesPerFrame)
            {
                for (int ch = 0; ch < maxCh && ch < _audioChannels; ch++)
                {
                    int16_t sample = (int16_t)((uint8_t)_ctx->audioRingBuffer[_ctx->audioRingRead] |
                        ((uint8_t)_ctx->audioRingBuffer[(_ctx->audioRingRead + 1) % _ctx->audioRingSize] << 8));
                    _ctx->audioRingRead = (_ctx->audioRingRead + 2) % _ctx->audioRingSize;
                    _ctx->audioRingCount -= 2;

                    double x = (double)sample / 32768.0;

                    // low-pass 250
                    double& lx1 = stLow[ch][0]; double& lx2 = stLow[ch][1];
                    double& ly1 = stLow[ch][2]; double& ly2 = stLow[ch][3];
                    double yL = lpLow.b0 * x + lpLow.b1 * lx1 + lpLow.b2 * lx2
                              - lpLow.a1 * ly1 - lpLow.a2 * ly2;
                    lx2 = lx1; lx1 = x; ly2 = ly1; ly1 = yL;

                    // low-pass 4500
                    double& hx1 = stHigh[ch][0]; double& hx2 = stHigh[ch][1];
                    double& hy1 = stHigh[ch][2]; double& hy2 = stHigh[ch][3];
                    double yH = lpHigh.b0 * x + lpHigh.b1 * hx1 + lpHigh.b2 * hx2
                              - lpHigh.a1 * hy1 - lpHigh.a2 * hy2;
                    hx2 = hx1; hx1 = x; hy2 = hy1; hy1 = yH;

                    double low = yL;
                    double mid = yH - yL;
                    double high = x - yH;

                    lowSum += low * low;
                    midSum += mid * mid;
                    highSum += high * high;
                    envSum += x * x;
                    if (fabs(x) > peakMax) peakMax = fabs(x);

                    windowCount++;
                    if (windowCount >= windowSamples)
                    {
                        double n = (double)windowSamples;
                        lowAcc.push_back(sqrt(lowSum / n));
                        midAcc.push_back(sqrt(midSum / n));
                        highAcc.push_back(sqrt(highSum / n));
                        envAcc.push_back(sqrt(envSum / n));
                        peakAcc.push_back(peakMax);
                        lowSum = midSum = highSum = envSum = 0;
                        peakMax = 0;
                        windowCount = 0;
                    }
                }
                lastDataTick = libvlc_clock();
            }
        }

        if (libvlc_media_player_get_state((libvlc_media_player_t*)_ctx->vlcPlayer) == libvlc_Ended)
        {
            // Drain whatever is left in the ring (stops when < one frame remains)
            while (true)
            {
                std::lock_guard<std::mutex> lock(*mtx);
                if (_ctx->audioRingCount < bytesPerFrame) break;
                for (int ch = 0; ch < maxCh && ch < _audioChannels; ch++)
                {
                    int16_t sample = (int16_t)((uint8_t)_ctx->audioRingBuffer[_ctx->audioRingRead] |
                        ((uint8_t)_ctx->audioRingBuffer[(_ctx->audioRingRead + 1) % _ctx->audioRingSize] << 8));
                    _ctx->audioRingRead = (_ctx->audioRingRead + 2) % _ctx->audioRingSize;
                    _ctx->audioRingCount -= 2;

                    double x = (double)sample / 32768.0;
                    double& lx1 = stLow[ch][0]; double& lx2 = stLow[ch][1];
                    double& ly1 = stLow[ch][2]; double& ly2 = stLow[ch][3];
                    double yL = lpLow.b0 * x + lpLow.b1 * lx1 + lpLow.b2 * lx2
                              - lpLow.a1 * ly1 - lpLow.a2 * ly2;
                    lx2 = lx1; lx1 = x; ly2 = ly1; ly1 = yL;

                    double& hx1 = stHigh[ch][0]; double& hx2 = stHigh[ch][1];
                    double& hy1 = stHigh[ch][2]; double& hy2 = stHigh[ch][3];
                    double yH = lpHigh.b0 * x + lpHigh.b1 * hx1 + lpHigh.b2 * hx2
                              - lpHigh.a1 * hy1 - lpHigh.a2 * hy2;
                    hx2 = hx1; hx1 = x; hy2 = hy1; hy1 = yH;

                    lowSum += yL * yL;
                    midSum += (yH - yL) * (yH - yL);
                    highSum += (x - yH) * (x - yH);
                    envSum += x * x;
                    if (fabs(x) > peakMax) peakMax = fabs(x);

                    windowCount++;
                    if (windowCount >= windowSamples)
                    {
                        double n = (double)windowSamples;
                        lowAcc.push_back(sqrt(lowSum / n));
                        midAcc.push_back(sqrt(midSum / n));
                        highAcc.push_back(sqrt(highSum / n));
                        envAcc.push_back(sqrt(envSum / n));
                        peakAcc.push_back(peakMax);
                        lowSum = midSum = highSum = envSum = 0;
                        peakMax = 0;
                        windowCount = 0;
                    }
                }
            }
            if (windowCount > 0)
            {
                double n = (double)windowCount;
                lowAcc.push_back(sqrt(lowSum / n));
                midAcc.push_back(sqrt(midSum / n));
                highAcc.push_back(sqrt(highSum / n));
                envAcc.push_back(sqrt(envSum / n));
                peakAcc.push_back(peakMax);
            }
            finished = true;
            break;
        }

        libvlc_time_t now = libvlc_clock();
        // Give up on silence (ring stopped growing for 3s) or timeout
        if (now - lastDataTick > 3000000)
            finished = true;
        if (now - startTick > (libvlc_time_t)((_duration + 20.0) * 1000000))
            finished = true;

        ::Sleep(10);
    }

    libvlc_media_player_set_rate((libvlc_media_player_t*)_ctx->vlcPlayer, 1.0f);
    libvlc_media_player_pause((libvlc_media_player_t*)_ctx->vlcPlayer);

    if (envAcc.empty()) return nullptr;

    // Normalize every band against the peak envelope so bars stay in 0..1
    double envMax = 0;
    for (size_t i = 0; i < envAcc.size(); i++)
        if (envAcc[i] > envMax) envMax = envAcc[i];
    if (envMax <= 0.0) return nullptr;

    auto result = ref new Platform::Array<float>((int)envAcc.size() * 4);
    for (size_t i = 0; i < envAcc.size(); i++)
    {
        auto clamp01 = [](double v) { return v < 0.0 ? 0.0f : (v > 1.0 ? 1.0f : (float)v); };
        result[(int)i * 4 + 0] = clamp01(lowAcc[i] / envMax);
        result[(int)i * 4 + 1] = clamp01(midAcc[i] / envMax);
        result[(int)i * 4 + 2] = clamp01(highAcc[i] / envMax);
        result[(int)i * 4 + 3] = clamp01(envAcc[i] / envMax);
    }
    return result;
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
