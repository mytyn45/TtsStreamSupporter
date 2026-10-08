using System;

namespace TtsStreamSupporter.Models;

public class PiperDownloadProgress
{
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? Math.Min(100.0, (double)DownloadedBytes / TotalBytes * 100.0) : 0;
    public string? CurrentFileName { get; set; }
}

public enum PiperDownloadStatus
{
    Success,
    AlreadyExists,
    Failed
}

public class PiperDownloadResult
{
    public PiperDownloadStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}

