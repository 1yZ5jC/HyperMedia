using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HyperMedia
{
    public enum LyricFormat { Unknown, Lrc, Krc, Qrc }

    public class WordSeg
    {
        public double StartMs;
        public double EndMs;
        public string Ch;
    }

    public class LyricLineModel
    {
        public double TimeMs;
        public string Text;
        public List<WordSeg> Words;
    }

    public static class LyricParsers
    {
        private static readonly byte[] KrcKey = new byte[]
        {
            64, 71, 70, 76, 77, 66, 75, 69, 50, 48, 52, 49, 53, 59, 57, 55,
            56, 54, 58, 51, 47, 45, 44, 43, 42, 41, 40, 39, 38, 37, 36, 35,
            34, 33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19,
            18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1
        };

        private static readonly Regex RgxKrcLine = new Regex(@"\[\s*\d+\s*,\s*\d+\s*\]");
        private static readonly Regex RgxWordTag = new Regex(@"<\s*\d+:\d+(?:\.\d+)?\s*[,:]\s*\d+:\d+(?:\.\d+)?\s*>");
        private static readonly Regex RgxSingleWordTag = new Regex(@"<\s*\d+:\d+(?:\.\d+)?\s*>");

        public static LyricFormat DetectFormat(string text, byte[] raw = null)
        {
            if (raw != null && raw.Length >= 3 && raw[0] == (byte)'k' && raw[1] == (byte)'r' && raw[2] == (byte)'c')
                return LyricFormat.Krc;
            if (string.IsNullOrEmpty(text)) return LyricFormat.Unknown;
            string t = text.TrimStart('\uFEFF');
            // Check for word-level tags FIRST — any format (LRC, QRC, mixed) that
            // contains <start,end> or <start> word tags should use QRC parsing.
            if (RgxWordTag.IsMatch(t) || RgxSingleWordTag.IsMatch(t))
                return LyricFormat.Qrc;
            if (RgxKrcLine.IsMatch(t)) return LyricFormat.Krc;
            return LyricFormat.Lrc;
        }

        public static List<LyricLineModel> Parse(string text, LyricFormat format)
        {
            switch (format)
            {
                case LyricFormat.Krc: return ParseKrc(text);
                case LyricFormat.Qrc: return ParseQrc(text);
                default: return ParseLrc(text);
            }
        }

        public static bool TryDecryptKrc(byte[] data, out string text)
        {
            text = null;
            try
            {
                if (data == null || data.Length < 8) return false;
                if (data[0] != (byte)'k' || data[1] != (byte)'r' || data[2] != (byte)'c') return false;

                var buf = new byte[data.Length];
                Array.Copy(data, buf, data.Length);
                for (int i = 4; i < buf.Length; i++)
                    buf[i] ^= KrcKey[(i - 4) % KrcKey.Length];

                int offset = 0;
                if (buf.Length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) offset = 3;
                text = Encoding.UTF8.GetString(buf, offset, buf.Length - offset);
                return text.IndexOf('[') >= 0;
            }
            catch { return false; }
        }

        public static List<LyricLineModel> ParseKrc(string text)
        {
            var result = new List<LyricLineModel>();
            if (string.IsNullOrEmpty(text)) return result;

            double offset = 0;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] != '[') continue;

                if (line.StartsWith("[offset:"))
                {
                    int e = line.IndexOf(']', 8);
                    if (e > 8)
                    {
                        double v;
                        if (double.TryParse(line.Substring(8, e - 8), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            offset = v;
                    }
                    continue;
                }
                if (line.StartsWith("[ti:") || line.StartsWith("[ar:") || line.StartsWith("[al:") ||
                    line.StartsWith("[by:") || line.StartsWith("[hash:") || line.StartsWith("[total:") ||
                    line.StartsWith("[language:") || line.StartsWith("[kst:") || line.StartsWith("[ksc:") ||
                    line.StartsWith("[kt:") || line.StartsWith("[ve:") || line.StartsWith("[sign:"))
                    continue;

                int close = line.IndexOf(']');
                if (close <= 1) continue;
                string head = line.Substring(1, close - 1);
                string content = line.Substring(close + 1);
                string[] parts = head.Split(',');
                double startMs, durMs;
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out startMs) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out durMs))
                    continue;

                var words = new List<WordSeg>();
                var sb = new StringBuilder();
                double t = startMs;
                int i = 0;
                while (i < content.Length)
                {
                    char c = content[i];
                    if (c == '<')
                    {
                        int gt = content.IndexOf('>', i + 1);
                        if (gt < 0) break;
                        double wDur;
                        if (double.TryParse(content.Substring(i + 1, gt - i - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out wDur) &&
                            wDur > 0 && sb.Length > 0)
                        {
                            words.Add(new WordSeg { StartMs = t, EndMs = t + wDur, Ch = sb.ToString() });
                            sb.Clear();
                            t += wDur;
                        }
                        i = gt + 1;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                }

                if (sb.Length > 0)
                {
                    if (words.Count == 0)
                    {
                        words.Add(new WordSeg { StartMs = startMs, EndMs = startMs + durMs, Ch = sb.ToString() });
                    }
                    else
                    {
                        double lastEnd = words[words.Count - 1].EndMs;
                        double remaining = startMs + durMs - lastEnd;
                        if (remaining > 0)
                            words.Add(new WordSeg { StartMs = lastEnd, EndMs = lastEnd + remaining, Ch = sb.ToString() });
                    }
                }

                var model = new LyricLineModel
                {
                    TimeMs = startMs + offset,
                    Text = BuildPlainText(words, content),
                    Words = words.Count > 0 ? words : null
                };
                result.Add(model);
            }

            result.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return result;
        }

        private static string BuildPlainText(List<WordSeg> words, string contentFallback)
        {
            if (words != null && words.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var w in words) sb.Append(w.Ch);
                return sb.ToString();
            }
            return contentFallback;
        }

        public static List<LyricLineModel> ParseQrc(string text)
        {
            var result = new List<LyricLineModel>();
            if (string.IsNullOrEmpty(text)) return result;

            double offset = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[offset:"))
                {
                    int e = line.IndexOf(']', 8);
                    if (e > 8)
                    {
                        double v;
                        if (double.TryParse(line.Substring(8, e - 8), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            offset = v;
                    }
                    continue;
                }

                int open = line.IndexOf('[');
                int closeBracket = line.IndexOf(']', open + 1);
                if (open < 0 || closeBracket < 0) continue;
                double lineMs = ParseLrcTimestamp(line.Substring(open + 1, closeBracket - open - 1));
                if (lineMs < 0) continue;
                string content = line.Substring(closeBracket + 1);

                var words = new List<WordSeg>();
                var sb = new StringBuilder();
                int p = 0;
                double lineStart = lineMs;
                bool firstWordTag = true;
                while (p < content.Length)
                {
                    int lt = content.IndexOf('<', p);
                    if (lt < 0)
                    {
                        sb.Append(content.Substring(p));
                        break;
                    }
                    sb.Append(content.Substring(p, lt - p));
                    int gt = content.IndexOf('>', lt + 1);
                    if (gt < 0)
                    {
                        sb.Append(content.Substring(lt));
                        break;
                    }
                    string tag = content.Substring(lt + 1, gt - lt - 1);
                    double wStart, wEnd;
                    if (TryParseWordTag(tag, out wStart, out wEnd))
                    {
                        if (firstWordTag)
                        {
                            // First <start> tag marks the word-level start for this line.
                            // Text before it is line-level leading text — discard, not a word.
                            firstWordTag = false;
                            sb.Clear();
                            lineStart = wStart + offset;
                        }
                        else if (sb.Length > 0)
                        {
                            // Subsequent tag: text before it is a complete word.
                            words.Add(new WordSeg
                            {
                                StartMs = wStart + offset,
                                EndMs = wEnd > 0 ? wEnd + offset : -1,
                                Ch = sb.ToString()
                            });
                            sb.Clear();
                        }
                    }
                    else
                    {
                        sb.Append(content.Substring(lt, gt - lt + 1));
                    }
                    p = gt + 1;
                }

                // Second pass: fill in missing EndMs from next word's StartMs
                for (int i = 0; i < words.Count; i++)
                {
                    if (words[i].EndMs < 0)
                    {
                        words[i].EndMs = (i + 1 < words.Count)
                            ? words[i + 1].StartMs
                            : lineMs + 3000;
                    }
                }

                string textPart = sb.ToString();
                string plain = textPart;
                if (words.Count > 0)
                {
                    var acc = new StringBuilder();
                    foreach (var w in words) acc.Append(w.Ch);
                    acc.Append(textPart);
                    plain = acc.ToString();
                }

                result.Add(new LyricLineModel
                {
                    TimeMs = lineStart,
                    Text = plain.Trim(),
                    Words = words.Count > 0 ? words : null
                });
            }

            result.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return result;
        }

        private static bool TryParseWordTag(string tag, out double start, out double end)
        {
            start = -1;
            end = -1;
            int sep = tag.IndexOf(',');
            if (sep < 0)
            {
                int dotPos = tag.IndexOf('.');
                if (dotPos >= 0) sep = tag.IndexOf(':', dotPos);
            }
            if (sep >= 0)
            {
                // Dual-timestamp: <start,end> or <start:end>
                double s = ParseLrcTimestamp(tag.Substring(0, sep).Trim());
                double e = ParseLrcTimestamp(tag.Substring(sep + 1).Trim());
                if (s < 0 || e < 0 || e < s) return false;
                start = s;
                end = e;
                return true;
            }
            // Single-timestamp: <start>
            double st = ParseLrcTimestamp(tag.Trim());
            if (st < 0) return false;
            start = st;
            end = -1;
            return true;
        }

        public static List<LyricLineModel> ParseLrc(string text)
        {
            var result = new List<LyricLineModel>();
            if (string.IsNullOrEmpty(text)) return result;

            int pos = 0;
            while (pos < text.Length)
            {
                int openBracket = text.IndexOf('[', pos);
                if (openBracket < 0) break;

                int closeBracket = text.IndexOf(']', openBracket + 1);
                if (closeBracket < 0) break;

                string segment = text.Substring(openBracket + 1, closeBracket - openBracket - 1);
                double ms = ParseLrcTimestamp(segment);

                int textStart = closeBracket + 1;
                int textEnd = text.Length;
                int nextOpen = text.IndexOf('[', textStart);
                if (nextOpen >= 0) textEnd = nextOpen;

                string lineText = text.Substring(textStart, textEnd - textStart).Trim();
                if (lineText.EndsWith("/")) lineText = lineText.Substring(0, lineText.Length - 1).Trim();
                if (lineText.EndsWith("\\")) lineText = lineText.Substring(0, lineText.Length - 1).Trim();

                if (ms >= 0 && !string.IsNullOrEmpty(lineText))
                    result.Add(new LyricLineModel { TimeMs = ms, Text = lineText });

                pos = closeBracket + 1;
            }

            result.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return result;
        }

        private static double ParseLrcTimestamp(string s)
        {
            var parts = s.Split(':');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return -1;

            int min;
            if (!int.TryParse(parts[0], out min)) return -1;

            double sec;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                return -1;

            return min * 60000 + sec * 1000;
        }
    }
}