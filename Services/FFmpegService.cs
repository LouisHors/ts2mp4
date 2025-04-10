using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace TStoMP4Converter.Services
{
    public class FFmpegService
    {
        private readonly string _ffmpegPath;
        private readonly LoggingService _loggingService;

        public FFmpegService(string ffmpegPath, LoggingService loggingService)
        {
            _ffmpegPath = ffmpegPath;
            _loggingService = loggingService;
        }

        /// <summary>
        /// 获取视频文件的总时长
        /// </summary>
        public async Task<TimeSpan> GetVideoDurationAsync(string filePath)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = _ffmpegPath;
                    process.StartInfo.Arguments = $"-i \"{filePath}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardOutput = true;

                    process.Start();

                    string output = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    // 使用正则表达式从输出中提取持续时间
                    var durationRegex = new Regex(@"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                    var match = durationRegex.Match(output);

                    if (match.Success)
                    {
                        int hours = int.Parse(match.Groups[1].Value);
                        int minutes = int.Parse(match.Groups[2].Value);
                        int seconds = int.Parse(match.Groups[3].Value);
                        int milliseconds = int.Parse(match.Groups[4].Value) * 10;

                        return new TimeSpan(0, hours, minutes, seconds, milliseconds);
                    }
                }

                // 如果无法获取持续时间，返回默认值
                return TimeSpan.FromMinutes(30);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取视频时长时出错: {ex.Message}");
                return TimeSpan.FromMinutes(30); // 返回默认值
            }
        }

        /// <summary>
        /// 将TS文件转换为MP4文件
        /// </summary>
        /// <returns>转换结果，包含是否成功和错误信息</returns>
        public async Task<(bool Success, string ErrorMessage, string OutputLog)> ConvertTsToMp4Async(
            string inputPath, 
            string outputPath, 
            bool useHardwareAcceleration, 
            IProgress<double> progress, 
            CancellationToken cancellationToken)
        {
            try
            {
                // 获取视频总时长
                TimeSpan totalDuration = await GetVideoDurationAsync(inputPath);

                // 准备FFmpeg命令
                string hwAccelParam = useHardwareAcceleration ? "-hwaccel auto" : "";
                StringBuilder outputLogBuilder = new StringBuilder();

                using Process process = new();
                {
                    process.StartInfo.FileName = _ffmpegPath;
                    process.StartInfo.Arguments = $" {hwAccelParam} -i \"{inputPath}\" -f mp4 -codec copy \"{outputPath}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;

                    // 启动进程
                    process.Start();

                    // 异步读取 stderr
                    var progressTask = Task.Run(async () =>
                    {
                        string line;
                        var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");

                        // 使用 StreamReader 异步读取
                        using (var reader = process.StandardError)
                        {
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                // 记录原始 FFmpeg 输出到日志
                                await _loggingService.LogInfoAsync($"[FFmpeg] {line}");
                                outputLogBuilder.AppendLine(line);

                                // 尝试匹配时间以更新进度
                                var match = timeRegex.Match(line);
                                if (match.Success)
                                {
                                    try
                                    {
                                        int hours = int.Parse(match.Groups[1].Value);
                                        int minutes = int.Parse(match.Groups[2].Value);
                                        int seconds = int.Parse(match.Groups[3].Value);
                                        int milliseconds = int.Parse(match.Groups[4].Value) * 10;

                                        var currentTime = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                                        // 防止 totalDuration 为零导致除零错误
                                        if (totalDuration.TotalMilliseconds > 0)
                                        {
                                            double progressValue = (currentTime.TotalMilliseconds / totalDuration.TotalMilliseconds) * 100;
                                            progressValue = Math.Min(progressValue, 99); // 限制最大进度为99%
                                            progress?.Report(progressValue);
                                        }
                                    }
                                    catch
                                    {
                                        // 忽略解析错误
                                    }
                                }

                                if (cancellationToken.IsCancellationRequested)
                                    break;
                            }
                        }
                    }, cancellationToken);
                    
                    // 等待读取任务完成
                    await progressTask; 
                    
                    // 等待进程完成或取消
                    await Task.Run(async () =>
                    {
                        while (!process.HasExited)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                try
                                {
                                    process.Kill();
                                }
                                catch
                                {
                                    // 忽略进程已结束的错误
                                }
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            await Task.Delay(100, cancellationToken);
                        }
                    }, cancellationToken);

                    // 获取最终的退出代码
                    int exitCode = -1; 
                    try { exitCode = process.ExitCode; } catch { /* 可能进程已被kill */ }

                    string finalOutputLog = outputLogBuilder.ToString();
                    
                    // 检查进程退出代码
                    if (exitCode != 0)
                    {
                        string errorMessage = $"FFmpeg转换失败，退出代码: {exitCode}";
                        if (!string.IsNullOrEmpty(finalOutputLog))
                        {
                            // 尝试从输出中提取错误信息
                            var errorRegex = new Regex(@"(Error|错误).+", RegexOptions.IgnoreCase);
                            var match = errorRegex.Match(finalOutputLog);
                            if (match.Success)
                            {
                                errorMessage += $"\n详细错误: {match.Value}";
                            }
                        }
                        return (false, errorMessage, finalOutputLog);
                    }

                    // 转换成功
                    progress?.Report(100);
                    return (true, string.Empty, finalOutputLog);
                }
            }
            catch (OperationCanceledException)
            {
                // 转换被取消
                return (false, "转换操作被取消", string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"转换文件时出错: {ex.Message}");
                return (false, $"转换过程中发生异常: {ex.Message}", string.Empty);
            }
        }

        /// <summary>
        /// 检查FFmpeg是否可用
        /// </summary>
        public bool IsFFmpegAvailable()
        {
            return File.Exists(_ffmpegPath);
        }
    }
}