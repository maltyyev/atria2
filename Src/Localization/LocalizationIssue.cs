namespace Atria.Localization;

public sealed record LocalizationIssue(string File, string? Key, string Message, bool IsError = true, long? Line = null, long? Column = null)
{
    public override string ToString() => $"{File}{(Line is null ? "" : $":{Line}:{Column}")}{(Key is null ? "" : $" [{Key}]")}: {Message}";
}
