using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using SyncStick.App.Infrastructure;
using SyncStick.App.Localization;
using SyncStick.Core.Models;

namespace SyncStick.App.Views.Dialogs;

public partial class RulePickerDialog : Window
{
    private RulePickerOption? _selected;

    public RulePickerDialog(
        IReadOnlyList<SyncRule> rules,
        string activeRuleId,
        string? lastRuleId)
    {
        InitializeComponent();

        foreach (var rule in rules)
        {
            var option = new RulePickerOption(
                rule,
                string.Equals(rule.Id, activeRuleId, StringComparison.Ordinal));
            option.PropertyChanged += OnOptionPropertyChanged;
            Options.Add(option);
        }

        RulesList.ItemsSource = Options;

        _selected = Options.FirstOrDefault(option =>
                        string.Equals(option.Id, lastRuleId, StringComparison.Ordinal))
                    ?? Options.FirstOrDefault(option =>
                        string.Equals(option.Id, activeRuleId, StringComparison.Ordinal))
                    ?? Options.FirstOrDefault();

        if (_selected is not null)
        {
            _selected.IsSelected = true;
        }

        ConfirmButton.IsEnabled = _selected is not null;
        VisualLocalizer.Attach(this);
    }

    public ObservableCollection<RulePickerOption> Options { get; } = new();

    public string? SelectedRuleId => _selected?.Id;

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RulePickerOption.IsSelected))
        {
            return;
        }

        if (sender is RulePickerOption { IsSelected: true } option)
        {
            _selected = option;
            ConfirmButton.IsEnabled = true;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class RulePickerOption : ObservableObject
{
    private bool _isSelected;

    public RulePickerOption(SyncRule rule, bool isActive)
    {
        Id = rule.Id;
        Name = Loc.RuleName(rule.Name);
        IsActive = isActive;
        CountText = Loc.IsEnglish
            ? rule.Sources.Count == 1 ? "1 item" : $"{rule.Sources.Count} items"
            : $"{rule.Sources.Count} 个同步项";

        var names = rule.Sources
            .Select(source => source.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();
        if (names.Count == 0)
        {
            DetailText = Loc.IsEnglish ? "No sources yet" : "还没有同步内容";
        }
        else
        {
            var joined = Loc.IsEnglish ? string.Join(", ", names) : string.Join("、", names);
            var extra = rule.Sources.Count - names.Count;
            DetailText = extra <= 0
                ? joined
                : Loc.IsEnglish ? $"{joined} +{extra} more" : $"{joined} 等 {extra} 项";
        }
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsActive { get; }

    public string ActiveBadgeText => Loc.IsEnglish ? "ACTIVE" : "当前";

    public string CountText { get; }

    public string DetailText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
