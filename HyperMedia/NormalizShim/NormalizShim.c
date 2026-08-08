// Minimal Normaliz.dll shim for Windows Phone / Store apps.
// Windows Phone 8.1 does not ship Normaliz.dll, yet libvlccore.dll statically
// imports IdnToAscii from it. Pure-C implementation, includes ONLY <windows.h>
// so no CRT/uCRT headers are needed. Build:
//   cl /TC /O2 /MT /LD NormalizShim.c NormalizShim.def /link /out:Normaliz.dll

#include <windows.h>

#define ENCODE_INITIAL_N 128u
#define ENCODE_INITIAL_BIAS 72u
#define ENCODE_DAMP 700u
#define ENCODE_SKEW 38u
#define ENCODE_TMIN 1u
#define ENCODE_TMAX 26u
#define BASE36 36u

static unsigned long adapt(unsigned long delta, unsigned long numpoints, int firsttime)
{
    unsigned long k;
    delta = firsttime ? delta / ENCODE_DAMP : delta / 2;
    delta += delta / numpoints;
    k = 0;
    while (delta > ((BASE36 - ENCODE_TMIN) * ENCODE_TMAX) / 2)
    {
        delta /= BASE36 - ENCODE_TMIN;
        k += BASE36;
    }
    return k + (((BASE36 - ENCODE_TMIN + 1) * delta) / (delta + ENCODE_SKEW));
}

static char enc_digit(unsigned long d)
{
    return (char)(d + 22 + (75 * (d < 26 ? 1 : 0)));
}

static unsigned long dec_digit(char c)
{
    if (c >= 'a' && c <= 'z') return (unsigned long)(c - 'a');
    if (c >= 'A' && c <= 'Z') return (unsigned long)(c - 'A');
    if (c >= '0' && c <= '9') return (unsigned long)(c - '0') + 26;
    return 0xFFFFFFFFul;
}

static size_t wstrlen(const wchar_t* s)
{
    size_t n = 0;
    while (s[n]) n++;
    return n;
}

static size_t lstrlen_a(const char* s)
{
    size_t n = 0;
    while (s[n]) n++;
    return n;
}

/* punycode encode: returns number of chars written to out (may be 0), 0xFFFF on error */
static unsigned long punc_encode(const wchar_t* in, unsigned long n_in, char* out, unsigned long out_cap)
{
    unsigned long h = 0, b = 0, n, delta, bias, i;
    unsigned long written = 0;

    for (i = 0; i < n_in; i++)
        if (in[i] < 0x80)
        {
            if (written >= out_cap) return 0xFFFF;
            out[written++] = (char)in[i];
            b++;
        }
    h = b;
    if (b > 0 && b < n_in)
    {
        if (written >= out_cap) return 0xFFFF;
        out[written++] = '-';
    }

    n = ENCODE_INITIAL_N;
    delta = 0;
    bias = ENCODE_INITIAL_BIAS;

    while (h < n_in)
    {
        unsigned long m = 0x7FFFFFFF;
        unsigned long t;
        for (i = 0; i < n_in; i++)
            if ((unsigned long)in[i] >= n && (unsigned long)in[i] < m)
                m = (unsigned long)in[i];

        delta += (m - n) * (h + 1);
        n = m;

        for (i = 0; i < n_in; i++)
        {
            if ((unsigned long)in[i] < n) delta++;
            if ((unsigned long)in[i] == n)
            {
                unsigned long q = delta, k = BASE36;
                for (;;)
                {
                    if (k <= bias) t = ENCODE_TMIN;
                    else if (k >= bias + ENCODE_TMAX) t = ENCODE_TMAX;
                    else t = k - bias;
                    if (q < t) break;
                    if (written >= out_cap) return 0xFFFF;
                    out[written++] = enc_digit(t + (q - t) % (BASE36 - t));
                    q = (q - t) / (BASE36 - t);
                    k += BASE36;
                }
                if (written >= out_cap) return 0xFFFF;
                out[written++] = enc_digit(q);
                bias = adapt(delta, h + 1, h == b);
                delta = 0;
                h++;
            }
        }
        delta++;
        n++;
    }
    return (int)written;
}

/* punycode_decode: 1 on success writing into `out` (wchar), 0 on error */
static int punc_decode(const char* in, unsigned long n_in, wchar_t* out, unsigned long out_cap)
{
    unsigned long n, i, bias, pos = 0;
    char* dash;
    int have_body = 0;
    const char* body = in;
    unsigned long body_len = n_in;

    for (dash = in + n_in - 1; dash >= in; dash--)
        if (*dash == '-') break;
    if (dash >= in)
    {
        /* split at last '-' */
        unsigned long ascii_len = (unsigned long)(dash - in);
        unsigned long j;
        for (j = 0; j < ascii_len; j++)
        {
            if (in[j] >= 0x80) return 0;
            if (j >= out_cap) return 0;
            out[j] = (wchar_t)(unsigned char)in[j];
        }
        if (j >= out_cap || out[j] == 0) return 0;
        out[j] = 0;
        body = dash + 1;
        body_len = n_in - ascii_len - 1;
        have_body = 1;
    }
    else
    {
        unsigned long j;
        for (j = 0; j < n_in; j++)
        {
            if (in[j] >= 0x80) return 0;
            if (j >= out_cap) return 0;
            out[j] = (wchar_t)(unsigned char)in[j];
        }
        out[j] = 0;
        have_body = 0;
    }

    if (!have_body || body_len == 0) return 1;

    n = ENCODE_INITIAL_N;
    i = 0;
    bias = ENCODE_INITIAL_BIAS;

    for (;;)
    {
        unsigned long oldi = i;
        unsigned long w = 1;
        unsigned long outlen;
        for (unsigned long k = BASE36;; k += BASE36)
        {
            unsigned long digit, t;
            if (pos >= body_len) return 0;
            digit = dec_digit(body[pos++]);
            if (digit == 0xFFFFFFFF) return 0;
            if (digit > ((0xFFFFFFFF - i) / w)) return 0;
            i += digit * w;
            t = (k <= bias) ? ENCODE_TMIN : (k >= bias + ENCODE_TMAX ? ENCODE_TMAX : k - bias);
            if (digit < t) break;
            if (w > 0xFFFFFFFF / (BASE36 - t)) return 0;
            w *= BASE36 - t;
        }
        outlen = 0;
        while (out[outlen]) outlen++;
        if (i < oldi || outlen > 8192) return 0;
        {
            unsigned long numpoints = outlen + 1;
            unsigned long delta = i - oldi;
            unsigned long insert_at;
            if (delta / (n + 1) < numpoints) n += delta / numpoints;
            else return 0;
            if (n < 0x80 || n > 0x10FFFF) return 0;
            bias = adapt(delta, numpoints, oldi == 0);
            insert_at = outlen - (delta % numpoints);
            if (insert_at >= out_cap) return 0;
            {
                unsigned long mv;
                for (mv = outlen; mv > insert_at; mv--) out[mv] = out[mv - 1];
                out[insert_at] = (wchar_t)n;
            }
            i++;
        }
        if (pos >= body_len) break;
    }
    return 1;
}

/* exports, signatures match winnls.h */

__declspec(dllexport) int WINAPI IdnToAscii(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPWSTR lpASCIICharStr, int cchASCIIChar)
{
    int len;
    int need = 1;
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len > 0 && lpUnicodeWideStr[len - 1] == 0) len--;
    if (len <= 0)
    {
        if (lpASCIICharStr && cchASCIIChar >= 1) lpASCIICharStr[0] = 0;
        return 1;
    }

    /* pass 1: compute output length into a local buffer sized by worst case (4x + "xn--" per label) */
    {
        char buf[1024];
        unsigned long bp = 0;
        size_t segStart = 0;
        int i;
        int ok = 1;
        for (i = 0; i <= len && ok; i++)
        {
            if (i == len || lpUnicodeWideStr[i] == L'.')
            {
                if (i > (int)segStart)
                {
                    const wchar_t* seg = lpUnicodeWideStr + segStart;
                    int segLen = i - (int)segStart;
                    int j;
                    int asciiOnly = 1;
                    for (j = 0; j < segLen; j++)
                        if (seg[j] >= 0x80) { asciiOnly = 0; break; }
                    if (asciiOnly)
                    {
                        for (j = 0; j < segLen; j++)
                        {
                            if (bp >= sizeof(buf)) { ok = 0; break; }
                            buf[bp++] = (char)seg[j];
                        }
                    }
                    else
                    {
                        char enc[512];
                        int nenc;
                        if (bp + 4 >= sizeof(buf)) { ok = 0; break; }
                        buf[bp++] = 'x'; buf[bp++] = 'n'; buf[bp++] = '-'; buf[bp++] = '-';
                        nenc = punc_encode(seg, (unsigned long)segLen, enc, sizeof(enc));
                        if (nenc == 0xFFFF) { ok = 0; break; }
                        if (bp + nenc >= sizeof(buf)) { ok = 0; break; }
                        for (j = 0; j < nenc; j++) buf[bp++] = enc[j];
                    }
                }
                if (i < len)
                {
                    if (bp >= sizeof(buf)) { ok = 0; break; }
                    buf[bp++] = '.';
                }
                segStart = (size_t)i + 1;
            }
        }
        if (!ok) { SetLastError(ERROR_INVALID_DATA); return 0; }
        need = (int)bp + 1;
        if (cchASCIIChar >= need && lpASCIICharStr)
        {
            size_t j;
            for (j = 0; j < bp; j++) lpASCIICharStr[j] = buf[j];
            lpASCIICharStr[bp] = 0;
        }
    }
    return need;
}

__declspec(dllexport) int WINAPI IdnToUnicode(DWORD dwFlags, LPCSTR lpASCIICharStr, int cchASCIIChar, LPWSTR lpUnicodeWideStr, int cchUnicodeWideStr)
{
    int len;
    int need = 1;
    if (!lpASCIICharStr || cchASCIIChar < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    len = (cchASCIIChar == 0) ? (int)lstrlenA(lpASCIICharStr) : cchASCIIChar;
    if (len > 0 && lpASCIICharStr[len - 1] == 0) len--;
    if (len <= 0)
    {
        if (lpUnicodeWideStr && cchUnicodeWideStr >= 1) lpUnicodeWideStr[0] = 0;
        return 1;
    }
    {
        wchar_t buf[8192];
        unsigned long bp = 0;
        size_t segStart = 0;
        int i;
        int ok = 1;
        for (i = 0; i <= len && ok; i++)
        {
            if (i == len || lpASCIICharStr[i] == '.')
            {
                if (i > (int)segStart)
                {
                    const char* seg = lpASCIICharStr + segStart;
                    int segLen = i - (int)segStart;
                    if (segLen > 4 && seg[0] == 'x' && seg[1] == 'n' && seg[2] == '-' && seg[3] == '-')
                    {
                        wchar_t dec[512];
                        int ndec;
                        if (!punc_decode(seg + 4, (unsigned long)segLen - 4, dec, 512)) { ok = 0; break; }
                        ndec = (int)wcslen(dec);
                        if (bp + (unsigned long)ndec >= sizeof(buf) / sizeof(wchar_t)) { ok = 0; break; }
                        for (int j = 0; j < ndec; j++) buf[bp++] = dec[j];
                    }
                    else
                    {
                        int j;
                        if (bp + (unsigned long)segLen > sizeof(buf) / sizeof(wchar_t)) { ok = 0; break; }
                        for (j = 0; j < segLen; j++) buf[bp++] = (wchar_t)(unsigned char)seg[j];
                    }
                }
                if (i < len) buf[bp++] = L'.';
                segStart = (size_t)i + 1;
            }
        }
        if (!ok) { SetLastError(ERROR_INVALID_DATA); return 0; }
        need = (int)bp + 1;
        if (cchUnicodeWideStr >= need && lpUnicodeWideStr)
        {
            unsigned long j;
            for (j = 0; j < bp; j++) lpUnicodeWideStr[j] = buf[j];
            lpUnicodeWideStr[bp] = 0;
        }
    }
    return need;
}

__declspec(dllexport) int WINAPI IdnToNameprepUnicode(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPWSTR lpNameprepWideStr, int cchNameprepWideStr)
{
    int len;
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len > 0 && lpUnicodeWideStr[len - 1] == 0) len--;
    if (cchNameprepWideStr >= len + 1 && lpNameprepWideStr)
    {
        int i;
        for (i = 0; i < len; i++) lpNameprepWideStr[i] = lpUnicodeWideStr[i];
        lpNameprepWideStr[len] = 0;
    }
    return len + 1;
}

__declspec(dllexport) int WINAPI IdnToNameprepAscii(DWORD dwFlags, LPCWSTR lpUnicodeWideStr, int cchUnicodeWideStr, LPSTR lpNameprepAsciiStr, int cchNameprepAsciiStr)
{
    int len;
    char* ascii;
    if (!lpUnicodeWideStr || cchUnicodeWideStr < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    len = (cchUnicodeWideStr == 0) ? (int)wcslen(lpUnicodeWideStr) : cchUnicodeWideStr;
    if (len > 0 && lpUnicodeWideStr[len - 1] == 0) len--;
    {
        int i;
        for (i = 0; i < len; i++)
            if (lpUnicodeWideStr[i] > 0x7F) { SetLastError(ERROR_INVALID_DATA); return 0; }
        if (cchNameprepAsciiStr >= len + 1 && lpNameprepAsciiStr)
        {
            for (i = 0; i < len; i++) lpNameprepAsciiStr[i] = (char)lpUnicodeWideStr[i];
            lpNameprepAsciiStr[len] = 0;
        }
    }
    return len + 1;
}

__declspec(dllexport) int WINAPI NormalizeString(NORM_FORM NormForm, LPCWSTR lpSrcString, int cwSrcLength, LPWSTR lpDstString, int cwDstLength)
{
    int len;
    if (!lpSrcString || cwSrcLength < 0) { SetLastError(ERROR_INVALID_PARAMETER); return 0; }
    len = (cwSrcLength == 0) ? (int)wcslen(lpSrcString) : cwSrcLength;
    if (len > 0 && lpSrcString[len - 1] == 0) len--;
    if (cwDstLength >= len + 1 && lpDstString)
    {
        int i;
        for (i = 0; i < len; i++) lpDstString[i] = lpSrcString[i];
        lpDstString[len] = 0;
    }
    return len + 1;
}

__declspec(dllexport) BOOL WINAPI IsNormalizedString(NORM_FORM NormForm, LPCWSTR lpString, int cwLength)
{
    if (!lpString || cwLength < 0) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE hDll, DWORD reason, LPVOID reserved) { return TRUE; }