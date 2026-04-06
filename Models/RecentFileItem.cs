using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mermaider.Models;

public partial class RecentFileItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string _filePath;

    public string FileName => Path.GetFileName(FilePath);

    public RecentFileItem(string filePath)
    {
        _filePath = filePath;
    }
}
