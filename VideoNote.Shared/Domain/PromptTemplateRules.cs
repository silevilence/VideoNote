namespace VideoNote.Shared.Domain;

public static class PromptTemplateRules
{
    public const int MaxNameLength = 200;

    public static string CopyName(string name)
    {
        const string suffix = " 副本";
        var length = Math.Min(name.Length, MaxNameLength - suffix.Length);
        // Keep a Unicode surrogate pair intact at the truncation boundary.
        if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1]) && char.IsLowSurrogate(name[length]))
            length--;
        return name[..length] + suffix;
    }
}
