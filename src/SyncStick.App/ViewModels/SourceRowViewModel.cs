using System.Diagnostics;
using System.IO;
using SyncStick.App.Infrastructure;
using SyncStick.Core;
using SyncStick.Core.Models;

namespace SyncStick.App.ViewModels;

public sealed class SourceRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public SourceRowViewModel(MainViewModel owner, SourceItem source)
    {
        _owner = owner;
        Source = source;
        RemoveCommand = new RelayCommand(() => _owner.RemoveSource(this));
        RevealCommand = new RelayCommand(Reveal);
        BrowseDestinationCommand = new RelayCommand(() => _owner.BrowseDestination(this));
    }

    public SourceItem Source { get; }

    public string Name => Source.Name;

    public string Path => Source.Path;

    public bool IsFolder => Directory.Exists(Source.Path);

    public bool PathExists => IsFolder || File.Exists(Source.Path);

    public bool HasPathError => !PathExists;

    public bool IsEnabled
    {
        get => Source.Enabled;
        set
        {
            if (Source.Enabled == value)
            {
                return;
            }

            Source.Enabled = value;
            OnPropertyChanged();
            _owner.OnSourceEnabledChanged();
        }
    }

    public string DestinationSubfolder
    {
        get => Source.DestinationSubfolder ?? string.Empty;
        set
        {
            var normalizedInput = value.Trim();
            var current = Source.DestinationSubfolder ?? string.Empty;
            if (string.Equals(current, normalizedInput, StringComparison.Ordinal))
            {
                return;
            }

            Source.DestinationSubfolder = normalizedInput.Length == 0 ? null : normalizedInput;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationHint));
            OnPropertyChanged(nameof(HasDestinationError));
            OnPropertyChanged(nameof(DestinationTitle));
            _owner.OnSourceDestinationChanged();
        }
    }

    public string DestinationPlaceholder => IsFolder ? $"默认：{Name}" : "U 盘根目录";

    public string DestinationTitle => IsFolder ? "写入到" : "写入到";

    public string DestinationHint
    {
        get
        {
            try
            {
                var normalized = PathGuard.NormalizeSubfolder(Source.DestinationSubfolder);
                if (string.IsNullOrEmpty(normalized))
                {
                    return IsFolder ? $"{_owner.TargetDriveHint}\\{Name}\\" : $"{_owner.TargetDriveHint}\\";
                }

                return $"{_owner.TargetDriveHint}\\{normalized}";
            }
            catch (FormatException)
            {
                return "目标子目录无效";
            }
        }
    }

    public bool HasDestinationError
    {
        get
        {
            try
            {
                PathGuard.NormalizeSubfolder(Source.DestinationSubfolder);
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }
    }

    public string MetaText
    {
        get
        {
            if (!PathExists)
            {
                return "路径不存在";
            }

            if (IsFolder)
            {
                return "文件夹 · 保留原目录结构";
            }

            try
            {
                return $"{ByteSize.Format(new FileInfo(Path).Length)} · 单文件";
            }
            catch (Exception)
            {
                return "单文件";
            }
        }
    }

    public string IconResourceKey => IsFolder ? "Icon.Folder" : "Icon.File";

    public System.Windows.Media.Geometry Icon =>
        System.Windows.Application.Current.TryFindResource(IconResourceKey) as System.Windows.Media.Geometry
        ?? System.Windows.Media.Geometry.Empty;

    public RelayCommand RemoveCommand { get; }

    public RelayCommand RevealCommand { get; }

    public RelayCommand BrowseDestinationCommand { get; }

    public void RefreshDerived()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(PathExists));
        OnPropertyChanged(nameof(HasPathError));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(DestinationHint));
        OnPropertyChanged(nameof(DestinationPlaceholder));
        OnPropertyChanged(nameof(HasDestinationError));
        OnPropertyChanged(nameof(DestinationTitle));
    }

    private void Reveal()
    {
        try
        {
            if (!PathExists)
            {
                return;
            }

            if (IsFolder)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience; never surface an error toast for it.
        }
    }
}
