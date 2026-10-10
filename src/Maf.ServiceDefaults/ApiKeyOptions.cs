namespace Maf.ServiceDefaults;

public sealed class ApiKeyOptions
{
    public const string SectionName = "Security";
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Shared secret callers must send in the <c>X-Api-Key</c> header.
    /// When empty, authentication is disabled (local development only).
    /// </summary>
    public string? ApiKey { get; set; }
}
