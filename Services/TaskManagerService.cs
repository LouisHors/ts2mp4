using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using TStoMP4Converter.Models;

namespace TStoMP4Converter.Services
{
    /// <summary>
    /// 任务管理服务，用于保存和加载转换任务
    /// </summary>
    public class TaskManagerService
    {
        /// <summary>
        /// 保存任务到文件
        /// </summary>
        public bool SaveTask(string filePath, string folderPath, ObservableCollection<FileItem> files, 
            int totalFiles, int completedFiles, int failedFiles, bool useHardwareAcceleration, int threadCount)
        {
            try
            {
                // 创建任务数据
                var taskData = new TaskData
                {
                    FolderPath = folderPath,
                    Files = new List<FileItem>(files),
                    TotalFiles = totalFiles,
                    CompletedFiles = completedFiles,
                    FailedFiles = failedFiles,
                    UseHardwareAcceleration = useHardwareAcceleration,
                    ThreadCount = threadCount,
                    LastModifiedTime = DateTime.Now
                };

                // 序列化并保存
                string json = JsonConvert.SerializeObject(taskData, Formatting.Indented);
                File.WriteAllText(filePath, json);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存任务时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从文件加载任务
        /// </summary>
        public TaskData LoadTask(string filePath)
        {
            try
            {
                // 读取并反序列化任务数据
                string json = File.ReadAllText(filePath);
                var taskData = JsonConvert.DeserializeObject<TaskData>(json);

                // 验证文件夹是否存在
                if (!Directory.Exists(taskData.FolderPath))
                {
                    throw new DirectoryNotFoundException($"任务中的文件夹不存在: {taskData.FolderPath}");
                }

                // 验证文件是否存在
                var validFiles = new List<FileItem>();
                foreach (var file in taskData.Files)
                {
                    if (File.Exists(file.FilePath))
                    {
                        validFiles.Add(file);
                    }
                }
                taskData.Files = validFiles;

                // 更新最后修改时间
                taskData.LastModifiedTime = DateTime.Now;

                return taskData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载任务时出错: {ex.Message}");
                throw; // 重新抛出异常，让调用者处理
            }
        }

        /// <summary>
        /// 检查任务文件是否有效
        /// </summary>
        public bool IsTaskFileValid(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                // 尝试读取并反序列化，检查是否是有效的任务文件
                string json = File.ReadAllText(filePath);
                var taskData = JsonConvert.DeserializeObject<TaskData>(json);

                // 检查必要的属性
                return !string.IsNullOrEmpty(taskData.FolderPath) && taskData.Files != null;
            }
            catch
            {
                return false;
            }
        }
    }
}