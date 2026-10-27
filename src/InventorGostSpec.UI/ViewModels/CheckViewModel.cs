using InventorGostSpec.Core.Checking;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Замечание в списке проверки.</summary>
public sealed class CheckItem
{
    public CheckItem(SpecIssue issue)
    {
        Issue = issue;
    }

    public SpecIssue Issue { get; }

    public string Message => Issue.Message;

    public string? RowId => Issue.RowId;

    public bool HasRow => Issue.RowId is not null;

    public bool IsError => Issue.Severity == IssueSeverity.Error;

    public bool IsWarning => Issue.Severity == IssueSeverity.Warning;

    public string SeverityText => Issue.Severity switch
    {
        IssueSeverity.Error => "Ошибка",
        IssueSeverity.Warning => "Внимание",
        _ => "Замечание",
    };
}

/// <summary>Окно «Проверка спецификации»: список замечаний и переход к строке.</summary>
public sealed class CheckViewModel : ObservableObject
{
    private CheckItem? _selectedItem;

    public CheckViewModel(IReadOnlyList<SpecIssue> issues)
    {
        Items = issues.Select(i => new CheckItem(i)).ToList();
        _selectedItem = Items.FirstOrDefault();

        var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        var warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        var infos = issues.Count - errors - warnings;
        Summary = $"Ошибок: {errors} · предупреждений: {warnings} · замечаний: {infos}";
    }

    public IReadOnlyList<CheckItem> Items { get; }

    public string Summary { get; }

    public CheckItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(CanGoToRow));
            }
        }
    }

    public bool CanGoToRow => SelectedItem?.HasRow == true;

    /// <summary>Строка, к которой перейти после закрытия окна.</summary>
    public string? RowToShow => CanGoToRow ? SelectedItem!.RowId : null;
}
