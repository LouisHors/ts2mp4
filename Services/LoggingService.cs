using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TStoMP4Converter.Services
{
    /// <summary>
    /// 日志服务，用于记录应用程序日志
    /// </summary>
    public class LoggingService
    {
        private readonly string _logDirectory;
        private readonly string _conversionLogDirectory;
        private readonly object _lockObj = new object();

        public LoggingService()
        {
            // 在应用程序目录下创建log文件夹
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
            _conversionLogDirectory = Path.Combine(_logDirectory, "conversions");
            
            // 确保日志目录存在
            EnsureDirectoryExists(_logDirectory);
            EnsureDirectoryExists(_conversionLogDirectory);
        }

        /// <summary>
        /// 记录一般信息日志
        /// </summary>
        public async Task LogInfoAsync(string message)
        {
            await LogMessageAsync("INFO", message);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public async Task LogErrorAsync(string message, Exception exception = null)
        {
            string errorMessage = message;
            if (exception != null)
            {
                errorMessage += $"\r\n异常信息: {exception.Message}";
                errorMessage += $"\r\n堆栈跟踪: {exception.StackTrace}";
                
                if (exception.InnerException != null)
                {
                    errorMessage += $"\r\n内部异常: {exception.InnerException.Message}";
                }
            }
            
            await LogMessageAsync("ERROR", errorMessage);
        }

        /// <summary>
        /// 记录转换开始日志
        /// </summary>
        public async Task LogConversionStartedAsync(string filePath, bool useHardwareAcceleration)
        {
            string fileName = Path.GetFileName(filePath);
            string message = $"开始转换文件: {fileName}\r\n";
            message += $"文件路径: {filePath}\r\n";
            message += $"硬件加速: {(useHardwareAcceleration ? "启用" : "禁用")}";
            
            await LogConversionEventAsync(filePath, "开始转换", message);
        }

        /// <summary>
        /// 记录转换完成日志
        /// </summary>
        public async Task LogConversionCompletedAsync(string filePath, TimeSpan duration)
        {
            string fileName = Path.GetFileName(filePath);
            string message = $"文件转换完成: {fileName}\r\n";
            message += $"文件路径: {filePath}\r\n";
            message += $"转换耗时: {duration.TotalSeconds:F2} 秒";
            
            await LogConversionEventAsync(filePath, "转换完成", message);
        }

        /// <summary>
        /// 记录转换失败日志
        /// </summary>
        public async Task LogConversionFailedAsync(string filePath, string errorMessage, Exception exception = null)
        {
            string fileName = Path.GetFileName(filePath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"文件转换失败: {fileName}");
            sb.AppendLine($"文件路径: {filePath}");
            sb.AppendLine($"错误信息: {errorMessage}");
            
            if (exception != null)
            {
                sb.AppendLine($"异常类型: {exception.GetType().Name}");
                sb.AppendLine($"异常信息: {exception.Message}");
                sb.AppendLine($"堆栈跟踪: {exception.StackTrace}");
                
                if (exception.InnerException != null)
                {
                    sb.AppendLine($"内部异常: {exception.InnerException.Message}");
                }
            }
            
            await LogConversionEventAsync(filePath, "转换失败", sb.ToString());
        }

        /// <summary>
        /// 记录转换取消日志
        /// </summary>
        public async Task LogConversionCancelledAsync(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string message = $"文件转换已取消: {fileName}\r\n";
            message += $"文件路径: {filePath}";
            
            await LogConversionEventAsync(filePath, "转换取消", message);
        }

        /// <summary>
        /// 记录FFmpeg输出日志
        /// </summary>
        public async Task LogFFmpegOutputAsync(string filePath, string output)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string logFilePath = Path.Combine(_conversionLogDirectory, $"{fileName}_ffmpeg_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            await File.WriteAllTextAsync(logFilePath, output);
        }

        /// <summary>
        /// 记录转换事件日志
        /// </summary>
        private async Task LogConversionEventAsync(string filePath, string eventType, string message)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string logFilePath = Path.Combine(_conversionLogDirectory, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{eventType}]\r\n{message}\r\n";
            
            await File.WriteAllTextAsync(logFilePath, logEntry);
            await LogMessageAsync(eventType, $"文件: {Path.GetFileName(filePath)} - {message.Split('\r')[0]}");
        }

        /// <summary>
        /// 记录一般日志消息
        /// </summary>
        private async Task LogMessageAsync(string level, string message)
        {
            string logFilePath = Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\r\n";
            
            try
            {
                // 使用锁确保多线程写入安全
                lock (_lockObj)
                {
                    using (StreamWriter writer = File.AppendText(logFilePath))
                    {
                        writer.Write(logEntry);
                    }
                }
                
                await Task.CompletedTask; // 为了保持异步签名
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入日志时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}