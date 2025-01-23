using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThreadingHelpers
{
    /// <summary>
    /// Provides system metrics monitoring capabilities
    /// </summary>
    public class SystemMetrics
    {
        private readonly Process _currentProcess;
        private DateTime _lastCpuTime = DateTime.UtcNow;
        private TimeSpan _lastTotalProcessorTime;

        public SystemMetrics()
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
        }

        /// <summary>
        /// Gets the current CPU usage as a percentage
        /// </summary>
        public double GetCpuUsage()
        {
            var now = DateTime.UtcNow;
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

            var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
            var totalMsPassed = (now - _lastCpuTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            _lastCpuTime = now;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            return cpuUsageTotal * 100;
        }

        /// <summary>
        /// Gets the current memory usage in percentage
        /// </summary>
        public double GetMemoryUsage()
        {
            var usedMemory = _currentProcess.WorkingSet64;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return (usedMemory / (double)_currentProcess.MaxWorkingSet.ToInt64()) * 100;
            }
            
            // For non-Windows platforms, compare against total system memory
            // This is a simplified approach
            return (usedMemory / (double)Environment.SystemPageSize) * 100;
        }
    }
}