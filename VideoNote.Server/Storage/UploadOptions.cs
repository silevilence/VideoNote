namespace VideoNote.Server.Storage;
public sealed class UploadOptions
{
    public long MaxBytes { get; set; } = 1024L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = [".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v"];
}
public sealed class UploadRejectedException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
