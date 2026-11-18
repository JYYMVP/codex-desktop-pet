namespace CodexDesktopPet.Models;

internal sealed record PetLibraryItem(
    string Id,
    string Name,
    string Description,
    string PackPath,
    bool IsBuiltIn = false)
{
    public string DisplayName => IsBuiltIn ? $"{Name}（内置）" : Name;
}

internal sealed record PetValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PetValidationResult Success { get; } = new(true, []);
}
