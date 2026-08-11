using System;
using System.Collections.Generic;

namespace HyperMedia
{
    public class LyricCandidate
    {
        public string Source;
        public string SongId;
        public string Title;
        public string Artist;
        public string Album;
        public List<string> Aliases = new List<string>();
        public bool StrictMatch;
        public double Score;
        public int Order;
        public LyricFormat Format;
        public string LyricText;
    }

    public static class LyricMatcher
    {
        public static string NormalizeTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            bool inParen = false;
            foreach (char c in s.ToLowerInvariant())
            {
                if (c == '(' || c == '（' || c == '[' || c == '【') { inParen = true; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { inParen = false; continue; }
                if (!inParen && !char.IsWhiteSpace(c))
                    sb.Append(c);
            }

            string norm = sb.ToString();
            int i = 0;
            while (i < norm.Length && char.IsDigit(norm[i])) i++;
            if (i > 0 && i < norm.Length)
            {
                char sep = norm[i];
                if (sep == '.' || sep == '-' || sep == '_' || sep == '：' || sep == ':')
                    return norm.Substring(i + 1);
            }
            return norm;
        }

        public static bool IsLatinOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (!char.IsLetter(c)) continue;
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
            }
            return true;
        }

        public static int EditDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    int ins = curr[j - 1] + 1;
                    int del = prev[j] + 1;
                    int sub = prev[j - 1] + cost;
                    curr[j] = Math.Min(ins, Math.Min(del, sub));
                }
                var tmp = prev;
                prev = curr;
                curr = tmp;
            }
            return prev[b.Length];
        }

        public static double ScoreName(string queryTitle, string candidateTitle)
        {
            string q = NormalizeTitle(queryTitle);
            string c = NormalizeTitle(candidateTitle);
            if (q.Length == 0 || c.Length == 0) return 0.2;
            if (q == c) return 1.0;
            if (q.Contains(c) || c.Contains(q)) return 0.8;
            int d = EditDistance(q, c);
            int maxLen = Math.Max(q.Length, c.Length);
            if (maxLen > 0 && d <= Math.Max(2, maxLen / 3)) return 0.6;
            return 0.3;
        }

        public static double ScoreTrack(string queryTitle, string queryArtist, string queryAlbum,
            string candidateTitle, string candidateArtist, string candidateAlbum, List<string> aliases)
        {
            double t = ScoreName(queryTitle, candidateTitle);
            if (aliases != null)
            {
                double aliasBest = 0;
                foreach (var a in aliases)
                    aliasBest = Math.Max(aliasBest, ScoreName(queryTitle, a));
                t = Math.Max(t, aliasBest * 0.92);
            }

            double artistScore;
            string qn = NormalizeTitle(queryArtist);
            string cn = NormalizeTitle(candidateArtist);
            if (qn.Length == 0) artistScore = 0.8;
            else if (cn.Length == 0) artistScore = 0.7;
            else if (qn == cn) artistScore = 1.0;
            else if (qn.Contains(cn) || cn.Contains(qn)) artistScore = 0.9;
            else artistScore = 0.5;

            double score = t * 0.7 + artistScore * 0.3;

            string qal = NormalizeTitle(queryAlbum);
            string cal = NormalizeTitle(candidateAlbum);
            if (qal.Length > 0 && cal.Length > 0 && (qal == cal || qal.Contains(cal) || cal.Contains(qal)))
                score = Math.Min(1.0, score + 0.1);

            return score;
        }

        public static string CleanSearchTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            string t = title;
            foreach (string cut in new[] { "(feat", "(ft", "(with", " ft.", " - live", " - remix", "(live", "(instrumental", "(伴奏" })
            {
                int i = t.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    t = t.Substring(0, i).Trim();
                    break;
                }
            }

            var sb = new System.Text.StringBuilder();
            bool inParen = false;
            foreach (char c in t)
            {
                if (c == '(' || c == '（' || c == '[' || c == '【') { inParen = true; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { inParen = false; continue; }
                if (!inParen) sb.Append(c);
            }
            string r = sb.ToString().Trim();
            return r.Length > 0 ? r : title;
        }

        public static List<string> BuildSearchQueries(string artist, string title, string album)
        {
            var list = new List<string>();
            string t = CleanSearchTitle(title);
            string a = CleanSearchTitle(artist ?? "");
            string al = CleanSearchTitle(album ?? "");

            string q1 = string.IsNullOrEmpty(a) ? t : a + " " + t;
            if (!string.IsNullOrEmpty(q1)) list.Add(q1);

            if (!string.IsNullOrEmpty(al) &&
                !string.Equals(al, t, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(al, q1, StringComparison.OrdinalIgnoreCase))
            {
                string q2 = (string.IsNullOrEmpty(a) ? "" : a + " ") + t + " " + al;
                if (!list.Contains(q2)) list.Add(q2);
            }

            if (!string.Equals(t, title, StringComparison.OrdinalIgnoreCase))
                list.Add(t);

            return list;
        }
    }
}