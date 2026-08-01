#pragma once

#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#include <collection.h>
#include <ppltasks.h>
#include <wrl.h>
#include <stdint.h>

#if !defined(__cplusplus_winrt)
namespace Windows { namespace Storage { namespace Streams {
    MIDL_INTERFACE("905a0fef-bc53-11df-8c49-001e4fc686da")
    IBufferByteAccess : public ::IUnknown
    {
        virtual HRESULT __stdcall GetBuffer(unsigned char** value) = 0;
    };
}}}
#endif

// Default to enabled; the WindowsPhone project overrides this to 0 via
// HYPERMEDIA_HAS_LIBVLC=0 in its preprocessor definitions (no libVLC there).
#ifndef HYPERMEDIA_HAS_LIBVLC
#define HYPERMEDIA_HAS_LIBVLC 1
#endif

#if HYPERMEDIA_HAS_LIBVLC
#include <vlc/vlc.h>
#endif
