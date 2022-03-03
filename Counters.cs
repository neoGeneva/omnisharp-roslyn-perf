using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace omnisharp_roslyn_perf
{
    public static class Counters
    {
        public static Process? Start(int pid)
        {
            return Process.Start(new ProcessStartInfo()
            {
                FileName = "dotnet-counters",
                Arguments = "collect  "
                    + $"--process-id {pid} "
                    + "--output counters.csv "
                // + "--refresh-interval 3 "
                // + "--counters System.Runtime "
            });
        }
    }
}