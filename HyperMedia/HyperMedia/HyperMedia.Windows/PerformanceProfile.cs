using System;
using System.Diagnostics;

namespace HyperMedia
{
    public enum PerformanceLevel
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public static class PerformanceProfile
    {
        private static PerformanceLevel? _cachedLevel;

        public static PerformanceLevel Level
        {
            get
            {
                if (_cachedLevel == null)
                    _cachedLevel = Measure();
                return _cachedLevel.Value;
            }
        }

        private static PerformanceLevel Measure()
        {
            try
            {
                long cpuMs = RunCpuBenchmark();
                int cores = Environment.ProcessorCount;

                int lowCount = 0;
                if (cpuMs > 350) lowCount++;
                if (cores <= 2) lowCount++;

                Debug.WriteLine("[HyperMedia] Perf: cpu={0}ms cores={1} -> {2}",
                    cpuMs, cores,
                    lowCount >= 2 ? "Low" : lowCount == 1 ? "Medium" : "High");

                if (lowCount >= 2) return PerformanceLevel.Low;
                if (lowCount == 1) return PerformanceLevel.Medium;
                return PerformanceLevel.High;
            }
            catch
            {
                return PerformanceLevel.Medium;
            }
        }

        private static long RunCpuBenchmark()
        {
            var sw = Stopwatch.StartNew();
            double acc = 0;
            for (int i = 1; i < 6000000; i++)
                acc += Math.Sqrt(i) * 1.0000001;
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
    }
}
