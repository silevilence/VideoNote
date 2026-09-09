namespace VideoNote.Server.Storage;

public sealed class UploadOptions
{
    public long MaxBytes { get; set; } = 1024L * 1024 * 1024;

    // Defaults live in appsettings.json; config binding appends to non-empty collections.
    public string[] AllowedExtensions { get; set; } = [];
}
public sealed class UploadRejectedException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
