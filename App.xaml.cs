using System;
using System.IO;
using System.Windows;

namespace TStoMP4Converter
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 确保FFmpeg目录存在
            string ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FFmpeg");
            if (!Directory.Exists(ffmpegDir))
            {
                Directory.CreateDirectory(ffmpegDir);
                MessageBox.Show("请将FFmpeg可执行文件放置在应用程序目录下的FFmpeg文件夹中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            
            // 确保log目录存在
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            
            // 确保conversions日志目录存在
            string conversionLogDir = Path.Combine(logDir, "conversions");
            if (!Directory.Exists(conversionLogDir))
            {
                Directory.CreateDirectory(conversionLogDir);
            }
            
            // 检查FFmpeg是否存在
            string ffmpegPath = Path.Combine(ffmpegDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                MessageBox.Show("未找到FFmpeg可执行文件，请确保ffmpeg.exe已放置在应用程序目录下的FFmpeg文件夹中。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}