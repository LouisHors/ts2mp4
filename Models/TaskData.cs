using System;
using System.Collections.Generic;

namespace TStoMP4Converter.Models
{
    /// <summary>
    /// 任务数据类，用于保存和加载转换任务的状态
    /// </summary>
    [Serializable]
    public class TaskData
    {
        /// <summary>
        /// 文件夹路径
        /// </summary>
        public string FolderPath { get; set; }
        
        /// <summary>
        /// 文件列表
        /// </summary>
        public List<FileItem> Files { get; set; }
        
        /// <summary>
        /// 总文件数
        /// </summary>
        public int TotalFiles { get; set; }
        
        /// <summary>
        /// 已完成文件数
        /// </summary>
        public int CompletedFiles { get; set; }
        
        /// <summary>
        /// 失败文件数
        /// </summary>
        public int FailedFiles { get; set; }
        
        /// <summary>
        /// 是否使用硬件加速
        /// </summary>
        public bool UseHardwareAcceleration { get; set; }
        
        /// <summary>
        /// 线程数
        /// </summary>
        public int ThreadCount { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }
        
        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModifiedTime { get; set; }
        
        public TaskData()
        {
            Files = new List<FileItem>();
            CreatedTime = DateTime.Now;
            LastModifiedTime = DateTime.Now;
            UseHardwareAcceleration = true;
            ThreadCount = 2;
        }
    }
}