using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using TStoMP4Converter.Models;
using TStoMP4Converter.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace TStoMP4Converter
{
    public partial class MainWindow : Window
    {
        private string _selectedFolderPath;
        private ObservableCollection<FileItem> _fileList;
        private DispatcherTimer _resourceMonitorTimer;
        private int _totalFiles;
        private int _completedFiles;
        private int _failedFiles;
        private bool _isConverting;
        
        // 服务
        private readonly FFmpegService _ffmpegService;
        private readonly SystemMonitorService _systemMonitorService;
        private readonly ConversionTaskService _conversionTaskService;
        private readonly TaskManagerService _taskManagerService;
        private readonly LoggingService _loggingService;

        public MainWindow()
        {
            InitializeComponent();
            
            _fileList = new ObservableCollection<FileItem>();
            FileListGrid.ItemsSource = _fileList;
            
            // 初始化服务
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FFmpeg", "ffmpeg.exe");
            _ffmpegService = new FFmpegService(ffmpegPath);
            _systemMonitorService = new SystemMonitorService();
            _taskManagerService = new TaskManagerService();
            _loggingService = new LoggingService();
            _conversionTaskService = new ConversionTaskService(_ffmpegService, _systemMonitorService, _loggingService);
            
            // 注册事件处理程序
            _conversionTaskService.FileConversionStarted += ConversionTaskService_FileConversionStarted;
            _conversionTaskService.FileConversionCompleted += ConversionTaskService_FileConversionCompleted;
            _conversionTaskService.FileConversionFailed += ConversionTaskService_FileConversionFailed;
            _conversionTaskService.FileConversionCancelled += ConversionTaskService_FileConversionCancelled;
            _conversionTaskService.ConversionProgressChanged += ConversionTaskService_ConversionProgressChanged;
            _conversionTaskService.ThreadCountChanged += ConversionTaskService_ThreadCountChanged;
            _conversionTaskService.ConversionCompleted += ConversionTaskService_ConversionCompleted;
            
            // 初始化系统资源监控定时器
            _resourceMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _resourceMonitorTimer.Tick += ResourceMonitorTimer_Tick;
            _resourceMonitorTimer.Start();
            
            // 初始化UI状态
            UpdateUIState(false);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "选择包含TS文件的文件夹",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedFolderPath = dialog.SelectedPath;
                CurrentFolderText.Text = _selectedFolderPath;
                LoadTsFiles(_selectedFolderPath);
            }
        }

        private void LoadTsFiles(string folderPath)
        {
            try
            {
                _fileList.Clear();
                var tsFiles = Directory.GetFiles(folderPath, "*.ts", SearchOption.TopDirectoryOnly);
                
                foreach (var file in tsFiles)
                {
                    var fileItem = new FileItem
                    {
                        FilePath = file,
                        Progress = 0,
                        Status = "等待中"
                    };
                    _fileList.Add(fileItem);
                }
                
                _totalFiles = _fileList.Count;
                _completedFiles = 0;
                _failedFiles = 0;
                
                UpdateStatistics();
                StatusText.Text = $"已加载 {_totalFiles} 个TS文件";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStartConversion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFolderPath))
            {
                System.Windows.MessageBox.Show("请先选择包含TS文件的文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_fileList.Count == 0)
            {
                System.Windows.MessageBox.Show("所选文件夹中没有TS文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!_ffmpegService.IsFFmpegAvailable())
            {
                System.Windows.MessageBox.Show("未找到FFmpeg可执行文件，请确保ffmpeg.exe已放置在应用程序目录下的FFmpeg文件夹中。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 开始转换
            _isConverting = true;
            UpdateUIState(_isConverting);
            
            // 获取设置
            bool useHardwareAcceleration = HardwareAccelerationToggle.IsChecked ?? false;
            int threadCount = (int)ThreadCountSlider.Value;
            
            // 更新状态
            StatusText.Text = $"开始转换 {_fileList.Count(f => f.Status == "等待中" || f.Status == "失败")} 个文件";
            
            // 启动转换任务
            Task.Run(() => _conversionTaskService.StartConversionAsync(_fileList.ToList(), threadCount, useHardwareAcceleration));
        }

        private void BtnStopConversion_Click(object sender, RoutedEventArgs e)
        {
            if (_isConverting)
            {
                _conversionTaskService.StopConversion();
                StatusText.Text = "正在停止转换...";
                
                // 保存当前进度到文件
                var saveDialog = new SaveFileDialog
                {
                    Filter = "进度文件 (*.task)|*.task",
                    Title = "保存转换进度",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    FileName = $"转换进度_{DateTime.Now:yyyyMMdd_HHmmss}.task"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    try
                    {
                        bool result = _taskManagerService.SaveTask(
                            saveDialog.FileName,
                            _selectedFolderPath,
                            _fileList,
                            _totalFiles,
                            _completedFiles,
                            _failedFiles,
                            HardwareAccelerationToggle.IsChecked ?? false,
                            (int)ThreadCountSlider.Value);

                        if (result)
                        {
                            System.Windows.MessageBox.Show(
                                $"转换已停止，进度已保存到:\n{saveDialog.FileName}\n\n您可以稍后通过\"加载任务\"按钮继续转换。",
                                "转换已停止",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("保存进度失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"保存进度时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                
                // 更新UI状态
                _isConverting = false;
                UpdateUIState(_isConverting);
            }
        }

        // 转换任务服务事件处理程序
        private void ConversionTaskService_FileConversionStarted(object sender, FileItemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                e.File.Status = "转换中";
                e.File.Progress = 0;
            });
        }

        private void ConversionTaskService_FileConversionCompleted(object sender, FileItemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                e.File.Status = "完成";
                e.File.Progress = 100;
                _completedFiles++;
                UpdateStatistics();
            });
        }

        private void ConversionTaskService_FileConversionFailed(object sender, FileItemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                e.File.Status = "失败";
                _failedFiles++;
                UpdateStatistics();
                
                // 获取错误信息并显示弹窗
                string errorMessage = _conversionTaskService.GetConversionError(e.File.FilePath);
                System.Windows.MessageBox.Show(
                    $"文件 {Path.GetFileName(e.File.FilePath)} 转换失败\n\n错误原因: {errorMessage}", 
                    "转换失败", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                
                // 记录到应用程序日志
                _loggingService.LogErrorAsync($"文件转换失败: {e.File.FilePath}", null);
            });
        }

        private void ConversionTaskService_FileConversionCancelled(object sender, FileItemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                e.File.Status = "已取消";
            });
        }

        private void ConversionTaskService_ConversionProgressChanged(object sender, ProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                e.File.Progress = e.Progress;
            });
        }

        private void ConversionTaskService_ThreadCountChanged(object sender, ThreadCountChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ThreadCountSlider.Value = e.NewThreadCount;
                StatusText.Text = $"系统资源使用率变化，已自动调整线程数为 {e.NewThreadCount}";
            });
        }

        private void ConversionTaskService_ConversionCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isConverting = false;
                UpdateUIState(_isConverting);
                StatusText.Text = "转换完成";
            });
        }

        private void ResourceMonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 获取系统资源使用率
                float cpuUsage = _systemMonitorService.GetCpuUsage();
                float gpuUsage = _systemMonitorService.GetGpuUsage();
                float diskUsage = _systemMonitorService.GetDiskUsage();
                
                // 更新UI
                CpuUsageBar.Value = cpuUsage;
                GpuUsageBar.Value = gpuUsage;
                DiskUsageBar.Value = diskUsage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"监控系统资源时出错: {ex.Message}");
            }
        }

        private void UpdateUIState(bool isConverting)
        {
            BtnStartConversion.IsEnabled = !isConverting;
            BtnStopConversion.IsEnabled = isConverting;
            BtnOpenFolder.IsEnabled = !isConverting;
            BtnLoadTask.IsEnabled = !isConverting;
            BtnSaveTask.IsEnabled = !isConverting && _fileList.Count > 0;
            HardwareAccelerationToggle.IsEnabled = !isConverting;
            ThreadCountSlider.IsEnabled = !isConverting;
        }

        private void UpdateStatistics()
        {
            TotalFilesText.Text = _totalFiles.ToString();
            CompletedFilesText.Text = _completedFiles.ToString();
            FailedFilesText.Text = _failedFiles.ToString();
        }

        private void BtnSaveTask_Click(object sender, RoutedEventArgs e)
        {
            if (_fileList.Count == 0 || string.IsNullOrEmpty(_selectedFolderPath))
            {
                System.Windows.MessageBox.Show("没有可保存的任务", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "任务文件 (*.task)|*.task",
                Title = "保存转换任务",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    bool result = _taskManagerService.SaveTask(
                        saveDialog.FileName,
                        _selectedFolderPath,
                        _fileList,
                        _totalFiles,
                        _completedFiles,
                        _failedFiles,
                        HardwareAccelerationToggle.IsChecked ?? false,
                        (int)ThreadCountSlider.Value);

                    if (result)
                    {
                        StatusText.Text = "任务已保存";
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("保存任务失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"保存任务时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadTask_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "任务文件 (*.task)|*.task",
                Title = "加载转换任务",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    // 检查任务文件是否有效
                    if (!_taskManagerService.IsTaskFileValid(openDialog.FileName))
                    {
                        System.Windows.MessageBox.Show("无效的任务文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // 加载任务数据
                    var taskData = _taskManagerService.LoadTask(openDialog.FileName);

                    // 加载任务数据到UI
                    _selectedFolderPath = taskData.FolderPath;
                    CurrentFolderText.Text = _selectedFolderPath;

                    _fileList.Clear();
                    foreach (var file in taskData.Files)
                    {
                        _fileList.Add(file);
                    }

                    _totalFiles = taskData.TotalFiles;
                    _completedFiles = taskData.CompletedFiles;
                    _failedFiles = taskData.FailedFiles;

                    // 更新UI
                    HardwareAccelerationToggle.IsChecked = taskData.UseHardwareAcceleration;
                    ThreadCountSlider.Value = taskData.ThreadCount;
                    UpdateStatistics();
                    BtnSaveTask.IsEnabled = true;

                    StatusText.Text = $"已加载任务，共 {_fileList.Count} 个文件";
                }
                catch (DirectoryNotFoundException ex)
                {
                    System.Windows.MessageBox.Show($"任务中的文件夹不存在: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"加载任务时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    // 注意：FileItem和TaskData类已移至Models文件夹
}