using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
namespace PresentationSpace.App;

public sealed class LocalWorkspaceStorage : IWorkspaceStorage
{
    public async Task<WorkspaceFile?> OpenAsync(IReadOnlyList<string> extensions)
    {
        var picker=new FileOpenPicker{SuggestedStartLocation=PickerLocationId.DocumentsLibrary};foreach(string extension in extensions)picker.FileTypeFilter.Add(extension);
        var file=await picker.PickSingleFileAsync();if(file is null)return null;var properties=await file.GetBasicPropertiesAsync();if(properties.Size>DocumentSerializer.MaxFileBytes)throw new InvalidDataException("Files are limited to 64 MB.");var buffer=await FileIO.ReadBufferAsync(file);var bytes=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(bytes);return new(file.Name,bytes);
    }
    public async Task<bool> SaveAsync(string suggestedName,byte[] data,string mimeType)
    {
        var picker=new FileSavePicker{SuggestedFileName=Path.GetFileNameWithoutExtension(suggestedName),SuggestedStartLocation=PickerLocationId.DocumentsLibrary};picker.FileTypeChoices.Add("PresentationSpace export",new List<string>{Path.GetExtension(suggestedName)});var file=await picker.PickSaveFileAsync();if(file is null)return false;await FileIO.WriteBytesAsync(file,data);return true;
    }
    public async Task<string?> ReadRecoveryAsync()
    {
        var item=await ApplicationData.Current.LocalFolder.TryGetItemAsync("presentation-recovery.pspace");if(item is not StorageFile file)return null;return await FileIO.ReadTextAsync(file);
    }
    public async Task WriteRecoveryAsync(string json)
    {
        var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("presentation-recovery.pspace",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(file,json);
    }
}
