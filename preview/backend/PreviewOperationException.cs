namespace RobloxMultiInstancePreview;
/// <summary>A safe, deliberately authored message suitable for the unprivileged UI.</summary>
public sealed class PreviewOperationException(string code,string message) : Exception(message)
{
    public string Code { get; } = code;
}
