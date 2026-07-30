#pragma once

#include "pch.h"
#include "FFmpegInterop.h"

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
            void Close();

            DecodedVideoFrame^ ReadNextVideoFrame();
            DecodedAudioFrame^ ReadNextAudioFrame();

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
