namespace PresentationSpace.Editor.Uno;

public sealed record WorkspaceFile(string Name,byte[] Data);
/// <summary>Host-provided storage. Saving must return false when the user cancels; recovery must not imply a portable file was saved.</summary>
public interface IWorkspaceStorage
{
    Task<WorkspaceFile?> OpenAsync(IReadOnlyList<string> extensions);
    Task<bool> SaveAsync(string suggestedName,byte[] data,string mimeType);
    Task<string?> ReadRecoveryAsync();
    Task WriteRecoveryAsync(string json);
}
