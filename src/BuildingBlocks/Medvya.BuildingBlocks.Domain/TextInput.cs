namespace Medvya.BuildingBlocks.Domain;

public static class TextInput
{
    /// <summary>Optional free text as stored: trimmed, and blank or whitespace-only becomes null.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
