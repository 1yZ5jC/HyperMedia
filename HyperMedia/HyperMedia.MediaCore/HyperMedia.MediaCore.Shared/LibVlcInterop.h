#pragma once

#include "pch.h"

// Decoded frame types that were previously provided by the (absent) FFmpegInterop.h.
namespace HyperMedia
{
    namespace MediaCore
    {
        public ref class DecodedVideoFrame sealed
        {
        internal:
            Platform::Array<uint8_t>^ _data;
            int _width;
            int _height;
            int64_t _timestamp;
        public:
            property Platform::Array<uint8_t>^ Data { Platform::Array<uint8_t>^ get() { return _data; } }
            property int Width { int get() { return _width; } }
            property int Height { int get() { return _height; } }
            property int64_t Timestamp { int64_t get() { return _timestamp; } }
        };

        public ref class DecodedAudioFrame sealed
        {
        internal:
            Platform::Array<uint8_t>^ _data;
            int _sampleCount;
            int _sampleRate;
            int _channels;
            int64_t _timestamp;
        public:
            property Platform::Array<uint8_t>^ Data { Platform::Array<uint8_t>^ get() { return _data; } }
            property int SampleCount { int get() { return _sampleCount; } }
            property int SampleRate { int get() { return _sampleRate; } }
            property int Channels { int get() { return _channels; } }
            property int64_t Timestamp { int64_t get() { return _timestamp; } }
        };
    }
}

struct LibVlcContextData
{
    void* vlcInstance;
    void* vlcPlayer;
    void* vlcMedia;

    int videoWidth;
    int videoHeight;
    int audioSampleRate;
    int audioChannels;
    double duration;

    void* audioMutex;
    uint8_t* audioRingBuffer;
    int audioRingSize;
    int audioRingWrite;
    int audioRingRead;
    int audioRingCount;

    void* videoMutex;
    uint8_t* videoFrameBuffer;
    int videoFrameWidth;
    int videoFrameHeight;
    int64_t videoFramePts;
    bool videoFrameReady;

    bool seeking;
};

namespace HyperMedia
{
    namespace MediaCore
    {
        public ref class LibVlcManager sealed
        {
        public:
            LibVlcManager();
            virtual ~LibVlcManager();

            static void Initialize();
            static void Shutdown();

            // Hardware video decode capability via D3D11/DXGI profile enumeration.
            // Returns: 0 = none, 1 = H.264, 2 = H.264 + H.265 8-bit, 3 = + H.265 10-bit.
            static int GetHardwareDecodeGrade();

            property bool IsInitialized { bool get() { return _initialized; } }

        private:
            static bool _initialized;
        };

        public ref class LibVlcDecoder sealed
        {
        public:
            LibVlcDecoder();
            virtual ~LibVlcDecoder();

            bool OpenFile(Platform::String^ filePath);
            [Windows::Foundation::Metadata::DefaultOverload]
            void Close();

            DecodedVideoFrame^ ReadNextVideoFrame();
            DecodedAudioFrame^ ReadNextAudioFrame();

            // Play the file for the given duration (or to the end, whichever first)
            // and return the captured audio as interleaved S16N PCM.
            Platform::Array<int16_t>^ CollectAudioPcm(double seconds);

            // Pause (playing=false) or resume (true) the underlying libVLC player.
            void SetPlayPause(bool playing);

            // Full-file waveform scan. Returns a float array of 4-tuples per 50ms
            // window: [low, mid, high, envelope] each normalized 0..1 (0 = silence).
            // Returns nullptr when the file has no audio or the scan fails.
            Platform::Array<float>^ ScanWaveform();

            void SeekTo(double seconds);

            property bool HasVideo { bool get() { return _hasVideo; } }
            property bool HasAudio { bool get() { return _hasAudio; } }
            property int VideoWidth { int get() { return _videoWidth; } }
            property int VideoHeight { int get() { return _videoHeight; } }
            property int AudioSampleRate { int get() { return _audioSampleRate; } }
            property int AudioChannels { int get() { return _audioChannels; } }
            property double Duration { double get() { return _duration; } }

        private:
            bool _hasVideo;
            bool _hasAudio;
            int _videoWidth;
            int _videoHeight;
            int _audioSampleRate;
            int _audioChannels;
            double _duration;
            LibVlcContextData* _ctx;
        };
    }
}
