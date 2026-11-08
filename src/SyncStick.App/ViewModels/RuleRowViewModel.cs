using SyncStick.App.Infrastructure;
using SyncStick.App.Localization;
using SyncStick.Core.Models;

namespace SyncStick.App.ViewModels;

public sealed class RuleRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isEditing;
    private string _draftName = string.Empty;

    public RuleRowViewModel(MainViewModel owner, SyncRule rule)
    {
        _owner = owner;
        Rule = rule;
        ActivateCommand = new RelayCommand(() => _owner.ActivateRule(this));
        EditSourcesCommand = new RelayCommand(() => _owner.EditRuleSources(this));
        RenameCommand = new RelayCommand(BeginRenameEdit);
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateRule(this));
        DeleteCommand = new RelayCommand(() => _owner.DeleteRule(this), () => _owner.Rules.Count > 1);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
    }

    public SyncRule Rule { get; }

    public string Id => Rule.Id;

    public string Name => Loc.RuleName(Rule.Name);

    public bool IsActive => string.Equals(_owner.ActiveRuleId, Rule.Id, StringComparison.Ordinal);

    public bool CanDelete => _owner.Rules.Count > 1;

    public int EnabledCount => Rule.Sources.Count(source => source.Enabled);

    public string SourceCountText => Loc.IsEnglish
        ? Rule.Sources.Count == 1 ? "1 sync item" : $"{Rule.Sources.Count} sync items"
        : $"{Rule.Sources.Count} 个同步项";

    public string EnabledCountText => Loc.IsEnglish
        ? EnabledCount == 1 ? "1 enabled" : $"{EnabledCount} enabled"
        : $"已启用 {EnabledCount} 个";

    public string ActiveBadgeText => Loc.IsEnglish ? "ACTIVE" : "当前";

    public string SourcesSummary
    {
        get
        {
            var names = Rule.Sources
                .Select(source => source.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(3)
                .ToList();
            if (names.Count == 0)
            {
                return Loc.IsEnglish ? "No sources yet" : "还没有同步内容";
            }

            var joined = Loc.IsEnglish ? string.Join(", ", names) : string.Join("、", names);
            var extra = Rule.Sources.Count - names.Count;
            if (extra <= 0)
            {
                return joined;
            }

            return Loc.IsEnglish ? $"{joined} +{extra} more" : $"{joined} 等 {extra} 项";
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public string DraftName
    {
        get => _draftName;
        set => SetProperty(ref _draftName, value);
    }

    public RelayCommand ActivateCommand { get; }

    public RelayCommand EditSourcesCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand DuplicateCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand CommitRenameCommand { get; }

    public RelayCommand CancelRenameCommand { get; }

    internal void RefreshDerived()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(SourceCountText));
        OnPropertyChanged(nameof(EnabledCountText));
        OnPropertyChanged(nameof(SourcesSummary));
        (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void BeginRenameEdit()
    {
        DraftName = Rule.Name;
        IsEditing = true;
    }

    private void CommitRename()
    {
        if (!_owner.RenameRule(this, DraftName))
        {
            IsEditing = false;
            return;
        }

        IsEditing = false;
    }

    private void CancelRename()
    {
        IsEditing = false;
        DraftName = Rule.Name;
    }
}
