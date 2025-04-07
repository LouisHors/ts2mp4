using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TStoMP4Converter.Models;

namespace TStoMP4Converter.Services
{
    public class ConversionTaskService
    {
        private readonly FFmpegService _ffmpegService;
        private readonly SystemMonitorService _systemMonitorService;
        private readonly LoggingService _loggingService;
        private CancellationTokenSource _cancellationTokenSource;
        private SemaphoreSlim _semaphore;
        private int _maxThreadCount;
        private bool _useHardwareAcceleration;
        private bool _isRunning;
        private Dictionary<string, string> _conversionErrors = new Dictionary<string, string>();

        public event EventHandler<FileItemEventArgs> FileConversionStarted;
        public event EventHandler<FileItemEventArgs> FileConversionCompleted;
        public event EventHandler<FileItemEventArgs> FileConversionFailed;
        public event EventHandler<FileItemEventArgs> FileConversionCancelled;
        public event EventHandler<ProgressEventArgs> ConversionProgressChanged;
        public event EventHandler<ThreadCountChangedEventArgs> ThreadCountChanged;
        public event EventHandler ConversionCompleted;

        public ConversionTaskService(FFmpegService ffmpegService, SystemMonitorService systemMonitorService, LoggingService loggingService)
        {
            _ffmpegService = ffmpegService;
            _systemMonitorService = systemMonitorService;
            _loggingService = loggingService;
            _maxThreadCount = 1; // 将默认线程数改为 1
            _useHardwareAcceleration = true; // 默认启用硬件加速
        }

        /// <summary>
        /// 开始转换任务
        /// </summary>
        public async Task StartConversionAsync(List<FileItem> files, int threadCount, bool useHardwareAcceleration)
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _maxThreadCount = threadCount;
            _useHardwareAcceleration = useHardwareAcceleration;
            _cancellationTokenSource = new CancellationTokenSource();
            _semaphore = new SemaphoreSlim(threadCount);

            try
            {
                // 获取待转换的文件
                var filesToConvert = files.Where(f => f.Status == "等待中" || f.Status == "失败").ToList();
                
                // 创建任务列表
                var tasks = new List<Task>();
                
                foreach (var file in filesToConvert)
                {
                    // 检查是否取消
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;
                    
                    // 等待信号量
                    await _semaphore.WaitAsync(_cancellationTokenSource.Token);
                    
                    // 创建转换任务
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await ConvertFileAsync(file, _useHardwareAcceleration, _cancellationTokenSource.Token);
                        }
                        finally
                        {
                            // 释放信号量
                            _semaphore.Release();
                        }
                    }, _cancellationTokenSource.Token);
                    
                    tasks.Add(task);
                }
                
                // 启动资源监控任务
                var monitorTask = MonitorSystemResourcesAsync(_cancellationTokenSource.Token);
                
                // 等待所有任务完成
                await Task.WhenAll(tasks);
                
                // 通知转换完成
                ConversionCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                // 转换被取消
            }
            catch (Exception ex)
            {
                MessageBox.Show($"转换过程中出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isRunning = false;
                _semaphore?.Dispose();
            }
        }

        /// <summary>
        /// 停止转换任务
        /// </summary>
        public void StopConversion()
        {
            _cancellationTokenSource?.Cancel();
        }
        
        /// <summary>
        /// 获取文件转换失败的错误信息
        /// </summary>
        public string GetConversionError(string filePath)
        {
            lock (_conversionErrors)
            {
                if (_conversionErrors.TryGetValue(filePath, out string error))
                {
                    return error;
                }
                return "未知错误";
            }
        }
        
        /// <summary>
        /// 清除文件的错误信息
        /// </summary>
        public void ClearConversionError(string filePath)
        {
            lock (_conversionErrors)
            {
                if (_conversionErrors.ContainsKey(filePath))
                {
                    _conversionErrors.Remove(filePath);
                }
            }
        }

        /// <summary>
        /// 转换单个文件
        /// </summary>
        private async Task ConvertFileAsync(FileItem file, bool useHardwareAcceleration, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;
            string errorMessage = string.Empty;
            
            try
            {
                // 通知文件转换开始
                FileConversionStarted?.Invoke(this, new FileItemEventArgs(file));
                
                // 记录转换开始日志
                await _loggingService.LogConversionStartedAsync(file.FilePath, useHardwareAcceleration);
                
                // 准备输出文件路径
                string outputFilePath = Path.Combine(
                    Path.GetDirectoryName(file.FilePath),
                    Path.GetFileNameWithoutExtension(file.FilePath) + ".mp4");
                
                // 创建进度报告对象
                var progress = new Progress<double>(p =>
                {
                    ConversionProgressChanged?.Invoke(this, new ProgressEventArgs(file, p));
                });
                
                // 执行转换
                var (success, error, outputLog) = await _ffmpegService.ConvertTsToMp4Async(
                    file.FilePath, 
                    outputFilePath, 
                    useHardwareAcceleration, 
                    progress, 
                    cancellationToken);
                
                // 记录FFmpeg输出日志
                if (!string.IsNullOrEmpty(outputLog))
                {
                    await _loggingService.LogFFmpegOutputAsync(file.FilePath, outputLog);
                }
                
                if (cancellationToken.IsCancellationRequested)
                {
                    // 转换被取消
                    await _loggingService.LogConversionCancelledAsync(file.FilePath);
                    FileConversionCancelled?.Invoke(this, new FileItemEventArgs(file));
                    return;
                }
                
                if (success && File.Exists(outputFilePath))
                {
                    // 转换成功，删除原始TS文件
                    File.Delete(file.FilePath);
                    
                    // 记录转换完成日志
                    var duration = DateTime.Now - startTime;
                    await _loggingService.LogConversionCompletedAsync(file.FilePath, duration);
                    
                    FileConversionCompleted?.Invoke(this, new FileItemEventArgs(file));
                }
                else
                {
                    // 转换失败，记录错误信息
                    errorMessage = error;
                    await _loggingService.LogConversionFailedAsync(file.FilePath, errorMessage, null);
                    
                    // 保存错误信息，用于显示
                    lock (_conversionErrors)
                    {
                        _conversionErrors[file.FilePath] = errorMessage;
                    }
                    
                    FileConversionFailed?.Invoke(this, new FileItemEventArgs(file));
                }
            }
            catch (OperationCanceledException)
            {
                // 转换被取消
                await _loggingService.LogConversionCancelledAsync(file.FilePath);
                FileConversionCancelled?.Invoke(this, new FileItemEventArgs(file));
            }
            catch (Exception ex)
            {
                // 转换失败，记录异常信息
                errorMessage = $"转换过程中发生异常: {ex.Message}";
                await _loggingService.LogConversionFailedAsync(file.FilePath, errorMessage, ex);
                
                // 保存错误信息，用于显示
                lock (_conversionErrors)
                {
                    _conversionErrors[file.FilePath] = errorMessage;
                }
                
                FileConversionFailed?.Invoke(this, new FileItemEventArgs(file));
            }
        }

        /// <summary>
        /// 监控系统资源并调整线程数
        /// </summary>
        private async Task MonitorSystemResourcesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // 获取当前线程数
                    int currentThreadCount = _semaphore.CurrentCount;
                    
                    // 获取建议的线程数
                    int recommendedThreadCount = _systemMonitorService.GetRecommendedThreadCount(
                        currentThreadCount, 
                        _maxThreadCount);
                    
                    // 如果建议的线程数与当前线程数不同，调整线程数
                    if (recommendedThreadCount != currentThreadCount)
                    {
                        if (recommendedThreadCount > currentThreadCount)
                        {
                            // 增加线程数
                            _semaphore.Release(recommendedThreadCount - currentThreadCount);
                        }
                        else
                        {
                            // 减少线程数（通过不释放信号量实现）
                            // 注意：这种方式不会立即减少正在运行的线程，而是在当前任务完成后减少可用线程
                        }
                        
                        // 通知线程数变化
                        ThreadCountChanged?.Invoke(this, new ThreadCountChangedEventArgs(recommendedThreadCount));
                    }
                    
                    // 等待一段时间再检查
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 忽略监控错误
                }
            }
        }
    }

    // 事件参数类
    public class FileItemEventArgs : EventArgs
    {
        public FileItem File { get; }

        public FileItemEventArgs(FileItem file)
        {
            File = file;
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public FileItem File { get; }
        public double Progress { get; }

        public ProgressEventArgs(FileItem file, double progress)
        {
            File = file;
            Progress = progress;
        }
    }

    public class ThreadCountChangedEventArgs : EventArgs
    {
        public int NewThreadCount { get; }

        public ThreadCountChangedEventArgs(int newThreadCount)
        {
            NewThreadCount = newThreadCount;
        }
    }
}