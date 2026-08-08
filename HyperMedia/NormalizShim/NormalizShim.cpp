// Minimal Normaliz.dll shim for Windows Phone / Store apps.
// Windows Phone 8.1 does not ship Normaliz.dll, yet libvlccore.dll statically
// imports IdnToAscii from it. This module provides a RFC3492-compatible
// implementation so the import table resolves.
// Build: cl /O2 /MT /LD NormalizShim.cpp NormalizShim.def /link /out:Normaliz.dll

#include <windows.h>
#include <string>

static const unsigned long BASE = 36;
static const unsigned long TMIN = 1;
static const unsigned long TMAX = 26;
static const unsigned long SKEW = 38;
static const unsigned long DAMP = 700;
static const unsigned long INITIAL_BIAS = 72;
static const unsigned long INITIAL_N = 128;

static unsigned long adapt(unsigned long delta, unsigned long numpoints, bool firsttime)
{
    delta = firsttime ? delta / DAMP : delta / 2;
    delta += delta / numpoints;
    unsigned long k = 0;
    while (delta > ((BASE - TMIN) * TMAX) / 2)
    {
        delta /= BASE - TMIN;
        k += BASE;
    }
    return k + (((BASE - TMIN + 1) * delta) / (delta + SKEW));
}

static char encode_digit(unsigned long d)
{
    return (char)(d + 22 + 75 * (d < 26 ? 1 : 0));
}

static unsigned long decode_digit(char c)
{
    if (c >= 'a' && c <= 'z') return c - 'a';
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= '0' && c <= '9') return c - '0' + 26;
    return ULONG_MAX;
}

static bool punycode_encode(const wchar_t* in, unsigned long n_in, std::string& out)
{
    out.clear();
    out.reserve((size_t)n_in * 2);

    unsigned long h = 0, b = 0;
    for (unsigned long i = 0; i < n_in; ++i)
        if (in[i] < 0x80)
        {
            out.push_back((char)in[i]);
            b++;
        }
    h = b;
    if (b > 0 && b < n_in) out.push_back('-');

    unsigned long n = INITIAL_N, delta = 0, bias = INITIAL_BIAS;
    while (h < n_in)
    {
        unsigned long m = 0x7FFFFFFF;
        for (unsigned long i = 0; i < n_in; ++i)
            if ((unsigned long)in[i] >= n && (unsigned long)in[i] < m)
                m = (unsigned long)in[i];

        delta += (m - n) * (h + 1);
        n = m;

        for (unsigned long i = 0; i < n_in; ++i)
        {
            if ((unsigned long)in[i] < n) delta++;
            if ((unsigned long)in[i] == n)
            {
                unsigned long q = delta, k = BASE;
                for (;;)
                {
                    unsigned long t = k <= bias ? TMIN : (k >= bias + TMAX ? TMAX : k - bias);
                    if (q < t) break;
                    out.push_back(encode_digit(t + (q - t) % (BASE - t)));
                    q = (q - t) / (BASE - t);
                    k += BASE;
                }
                out.push_back(encode_digit(q));
                bias = adapt(delta, h + 1, h == b);
                delta = 0;
                h++;
            }
        }
        delta++;
        n++;
    }
    return true;
}

static bool punycode_decode(const char* in, size_t n_in, std::wstring& out)
{
    std::wstring ascii;
    std::string body(in, n_in);
    if (!body.empty() && body.front() == '-') return false;

    size_t dash = body.rfind('-');
    if (dash != std::string::npos)
    {
        ascii.assign(body.begin(), body.begin() + dash);
        body = body.substr(dash + 1);
    }
    else
    {
        ascii = body;
        body.clear();
    }
    for (size_t i = 0; i < ascii.size(); i++)
    {
        unsigned char c = (unsigned char)ascii[i];
        if (c >= 0x80) return false;
        if (c == '-') return false;
        out.push_back((wchar_t)c);
    }
    if (body.empty()) return true;

    unsigned long n = INITIAL_N, i = 0, bias = INITIAL_BIAS;
    size_t pos = 0;
    while (pos < body.size())
    {
        unsigned long oldi = i, w = 1;
        for (unsigned long k = BASE;; k += BASE)
        {
            if (pos >= body.size()) return false;
            unsigned long digit = decode_digit(body[pos++]);
            if (digit == ULONG_MAX) return false;
            if (digit > ((ULONG_MAX - i) / w)) return false;
            i += digit * w;
            unsigned long t = k <= bias ? TMIN : (k >= bias + TMAX ? TMAX : k - bias);
            if (digit < t) break;
            if (w > ULONG_MAX / (BASE - t)) return false;
            w *= BASE - t;
        }
        size_t outlen = out.size();
        if (i < oldi || outlen > 8192) return false;
        unsigned long numpoints = (unsigned long)(outlen + 1);
        bias = adapt(i - oldi, numpoints, oldi == 0);
        unsigned long delta = i - oldi;
        if (delta / (n + 1) < numpoints) n += delta / numpoints;
        else return false;
        if (n < 0x80 || n > 0x10FFFF) return false;
        out.insert(outlen - (delta % numpoints), 1, (wchar_t)n);
        i++;
    }
    return true;
}

// Exact signatures match winnls.h; the .def file exports them by name.

int WINAPI IdnToAscii(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPWSTR lpASCIICharStr, int cchASCIIChar)
{
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    int len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len <= 0) { if (lpASCIICharStr && cchASCIIChar >= 1) lpASCIICharStr[0] = 0; return 1; }
    if (lpUnicodeWideStr[len - 1] == 0) len--;

    std::string result;
    size_t segStart = 0;
    for (int i = 0; i <= len; ++i)
    {
        if (i == len || lpUnicodeWideStr[i] == L'.')
        {
            if (i > (int)segStart)
            {
                const wchar_t* seg = lpUnicodeWideStr + segStart;
                int segLen = i - (int)segStart;
                bool asciiOnly = true;
                for (int j = 0; j < segLen; ++j)
                    if (seg[j] >= 0x80) { asciiOnly = false; break; }
                if (asciiOnly)
                {
                    for (int j = 0; j < segLen; ++j) result.push_back((char)seg[j]);
                }
                else
                {
                    std::string enc;
                    if (!punycode_encode(seg, (unsigned long)segLen, enc)) { SetLastError(ERROR_INVALID_DATA); return 0; }
                    result += "xn--";
                    result += enc;
                }
            }
            if (i < len) result.push_back('.');
            segStart = (size_t)i + 1;
        }
    }
    int need = (int)result.size() + 1;
    if (cchASCIIChar >= need && lpASCIICharStr)
    {
        for (size_t j = 0; j < result.size(); ++j) lpASCIICharStr[j] = result[j];
        lpASCIICharStr[result.size()] = 0;
        return need;
    }
    return need;
}

int WINAPI IdnToUnicode(DWORD dwFlags, LPCSTR lpASCIICharStr, int cchASCIIChar, LPWSTR lpUnicodeWideStr, int cchUnicodeWideStr)
{
    if (!lpASCIICharStr || cchASCIIChar < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    int len = (cchASCIIChar == 0) ? (int)strlen(lpASCIICharStr) : cchASCIIChar;
    if (len <= 0) { if (lpUnicodeWideStr && cchUnicodeWideStr >= 1) lpUnicodeWideStr[0] = 0; return 1; }
    if (lpASCIICharStr[len - 1] == 0) len--;

    std::wstring result;
    size_t segStart = 0;
    for (int i = 0; i <= len; ++i)
    {
        if (i == len || lpASCIICharStr[i] == '.')
        {
            if (i > (int)segStart)
            {
                const char* seg = lpASCIICharStr + segStart;
                int segLen = i - (int)segStart;
                if (segLen > 4 && seg[0] == 'x' && seg[1] == 'n' && seg[2] == '-' && seg[3] == '-')
                {
                    std::wstring dec;
                    if (!punycode_decode(seg + 4, (size_t)segLen - 4, dec)) { SetLastError(ERROR_INVALID_DATA); return 0; }
                    result += dec;
                }
                else
                {
                    for (int j = 0; j < segLen; ++j) result.push_back((wchar_t)(unsigned char)seg[j]);
                }
            }
            if (i < len) result.push_back(L'.');
            segStart = (size_t)i + 1;
        }
    }
    int need = (int)result.size() + 1;
    if (cchUnicodeWideStr >= need && lpUnicodeWideStr)
    {
        for (size_t j = 0; j < result.size(); ++j) lpUnicodeWideStr[j] = result[j];
        lpUnicodeWideStr[result.size()] = 0;
        return need;
    }
    return need;
}

int WINAPI IdnToNameprepUnicode(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPWSTR lpNameprepWideStr, int cchNameprepWideStr)
{
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    int len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len > 0 && lpUnicodeWideStr[len - 1] == 0) len--;
    if (cchNameprepWideStr >= len + 1 && lpNameprepWideStr)
    {
        for (int i = 0; i < len; ++i) lpNameprepWideStr[i] = lpUnicodeWideStr[i];
        lpNameprepWideStr[len] = 0;
    }
    return len + 1;
}

int WINAPI IdnToNameprepAscii(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPSTR lpNameprepAsciiStr, int cchNameprepAsciiStr)
{
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    int len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len > 0 && lpUnicodeWideStr[len - 1] == 0) len--;
    std::string ascii;
    for (int i = 0; i < len; ++i)
    {
        if (lpUnicodeWideStr[i] > 0x7F) { SetLastError(ERROR_INVALID_DATA); return 0; }
        ascii.push_back((char)lpUnicodeWideStr[i]);
    }
    if (cchNameprepAsciiStr >= (int)ascii.size() + 1 && lpNameprepAsciiStr)
    {
        for (size_t j = 0; j < ascii.size(); ++j) lpNameprepAsciiStr[j] = ascii[j];
        lpNameprepAsciiStr[ascii.size()] = 0;
    }
    return (int)ascii.size() + 1;
}

// Normalization: accept "already normalized" input and copy through; full NFKC
// decomposition is not needed by VLC's text-rendering paths on this platform.
int WINAPI NormalizeString(NORM_FORM NormForm, LPCWSTR lpSrcString, int cwSrcLength, LPWSTR lpDstString, int cwDstLength)
{
    if (!lpSrcString || cwSrcLength < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    int len = (cwSrcLength == 0) ? (int)wcslen(lpSrcString) : cwSrcLength;
    if (len > 0 && lpSrcString[len - 1] == 0) len--;
    if (cwDstLength >= len + 1 && lpDstString)
    {
        for (int i = 0; i < len; ++i) lpDstString[i] = lpSrcString[i];
        lpDstString[len] = 0;
    }
    return len + 1;
}

BOOL WINAPI IsNormalizedString(NORM_FORM NormForm, LPCWSTR lpString, int cwLength)
{
    if (!lpString || cwLength < 0) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }