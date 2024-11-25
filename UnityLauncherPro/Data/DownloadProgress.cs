namespace UnityLauncherPro
{
    public readonly struct DownloadProgress
    {
        public long TotalRead { get; }
        public long TotalBytes { get; }

        public DownloadProgress(long totalRead, long totalBytes)
        {
            TotalRead = totalRead;
            TotalBytes = totalBytes;
        }
    }
}