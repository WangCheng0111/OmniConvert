namespace OmniConvert.Models;

public sealed record FormatDefinition(
    FormatCategory Category,
    string Extension,
    string DisplayName);
