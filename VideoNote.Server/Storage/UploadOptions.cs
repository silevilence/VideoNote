namespace VideoNote.Server.Storage;

public sealed class UploadOptions
{
    public long MaxBytes { get; set; } = 1024L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = [".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v"];

    public void Bind(IConfiguration section)
    {
        MaxBytes = section.GetValue(nameof(MaxBytes), MaxBytes);
        // Bind into a fresh array: IConfiguration otherwise appends to the code defaults.
        AllowedExtensions = section.GetSection(nameof(AllowedExtensions)).Get<string[]>() ?? AllowedExtensions;
    }
}
public sealed class UploadRejectedException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
