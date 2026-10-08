namespace TtsStreamSupporter.Services;

public enum TtsProcessStatus
{
    Success,
    Skipped,       // команда skip / skip all
    Blacklisted,   // юзер в блэклисте
    Filtered,      // сообщение начинается с "запрещённого" префикса
    Empty          // реально пустое/невалидное сообщение (на будущее)
}

public readonly record struct TtsProcessResult(
    TtsProcessStatus Status,
    ProcessedTtsMessage? Message = null)
{
    public static TtsProcessResult Success(ProcessedTtsMessage message) =>
        new(TtsProcessStatus.Success, message);

    public static TtsProcessResult Skipped() =>
        new(TtsProcessStatus.Skipped);

    public static TtsProcessResult Blacklisted() =>
        new(TtsProcessStatus.Blacklisted);

    public static TtsProcessResult Filtered() =>
        new(TtsProcessStatus.Filtered);

    public static TtsProcessResult Empty() =>
        new(TtsProcessStatus.Empty);
}