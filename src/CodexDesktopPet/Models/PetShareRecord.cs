namespace CodexDesktopPet.Models;

internal sealed record PetShareRecord(
    string PetId,
    string PetName,
    string GistId,
    string Url,
    DateTime CreatedAtUtc);
