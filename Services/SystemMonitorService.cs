using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace TStoMP4Converter.Services
{
    public class SystemMonitorService
    {
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _diskCounter;
        private readonly Random _random; // 用于模拟GPU使用率

        public SystemMonitorService()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                _random = new Random();
                
                // 初始化计数器
                _cpuCounter.NextValue();
                _diskCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化系统监控服务时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取CPU使用率
        /// </summary>
        public float GetCpuUsage()
        {
            try
            {
                if (_cpuCounter == null)
                    return 0;
                
                return _cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取CPU使用率时出错: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 获取GPU使用率（模拟实现）
        /// </summary>
        public float GetGpuUsage()
        {
            try
            {
                // 注意：这是一个模拟实现
                // 实际应用中应该使用适当的API获取GPU使用率
                // 例如NVIDIA的NVAPI或AMD的ADL
                return _random.Next(10, 50);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取GPU使用率时出错: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 获取磁盘使用率
        /// </summary>
        public float GetDiskUsage()
        {
            try
            {
                if (_diskCounter == null)
                    return 0;
                
                return Math.Min(_diskCounter.NextValue(), 100); // 限制最大值为100
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取磁盘使用率时出错: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 判断系统资源是否足够
        /// </summary>
        public bool AreResourcesSufficient()
        {
            float cpuUsage = GetCpuUsage();
            float diskUsage = GetDiskUsage();
            
            // 如果CPU或磁盘使用率超过90%，认为资源不足
            return cpuUsage < 90 && diskUsage < 90;
        }

        /// <summary>
        /// 获取建议的线程数
        /// </summary>
        public int GetRecommendedThreadCount(int currentThreadCount, int maxThreadCount)
        {
            float cpuUsage = GetCpuUsage();
            float diskUsage = GetDiskUsage();
            
            // 如果资源使用率过高，减少线程数
            if (cpuUsage > 90 || diskUsage > 90)
            {
                return Math.Max(1, currentThreadCount - 1);
            }
            
            // 如果资源使用率较低，可以增加线程数
            if (cpuUsage < 50 && diskUsage < 50 && currentThreadCount < maxThreadCount)
            {
                return Math.Min(maxThreadCount, currentThreadCount + 1);
            }
            
            // 保持当前线程数
            return currentThreadCount;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _diskCounter?.Dispose();
        }
    }
}