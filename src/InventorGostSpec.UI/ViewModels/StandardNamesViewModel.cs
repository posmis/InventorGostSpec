using System.Windows.Input;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Стандартное изделие для переименования (данные из Inventor).</summary>
/// <param name="Key">Полный путь к файлу.</param>
/// <param name="FileName">Имя файла без расширения — правильное наименование.</param>
/// <param name="CurrentName">Текущее наименование (Description).</param>
/// <param name="IsPreselected">Компонент выбран в сборке.</param>
public sealed record StandardNameCandidate(string Key, string FileName, string CurrentName, bool IsPreselected);

public enum StandardNameStatus
{
    /// <summary>Наименование будет заменено именем файла.</summary>
    Change,

    /// <summary>Наименование уже совпадает с именем файла — пропускается.</summary>
    AlreadyOk,
}

public sealed class StandardNameItem : ObservableObject
{
    private string _proposed;
    private bool _isSelected;

    public StandardNameItem(StandardNameCandidate candidate)
    {
        Candidate = candidate;

        // Имя файла записывается как есть, без перестановок и замен.
        _proposed = candidate.FileName.Trim();
        Status = candidate.CurrentName.Trim() == _proposed ? StandardNameStatus.AlreadyOk : StandardNameStatus.Change;
    }

    public StandardNameCandidate Candidate { get; }

    public string FileName => Candidate.FileName;

    public string Current => Candidate.CurrentName;

    public StandardNameStatus Status { get; }

    public string StatusText => Status == StandardNameStatus.Change ? "Будет изменено" : "Уже в норме";

    /// <summary>Новое наименование. Можно поправить вручную.</summary>
    public string Proposed
    {
        get => _proposed;
        set
        {
            if (SetField(ref _proposed, value?.Trim() ?? ""))
            {
                IsSelected = _proposed.Length > 0 && _proposed != Current.Trim();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>Наименование уже совпадает с именем файла.</summary>
    public bool IsSkipped => Status == StandardNameStatus.AlreadyOk;

    /// <summary>Будет ли запись действительно что-то менять.</summary>
    public bool WillChange => IsSelected && Proposed.Length > 0 && Proposed != Current.Trim();
}

/// <summary>Предпросмотр записи имени файла в наименование стандартных изделий.</summary>
public sealed class StandardNamesViewModel : ObservableObject
{
    public StandardNamesViewModel(IEnumerable<StandardNameCandidate> candidates)
    {
        Items = candidates
            .Select(c => new StandardNameItem(c))
            .OrderBy(i => i.IsSkipped)
            .ThenBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Если в сборке что-то выбрано — отмечаем только выбранное.
        var hasPreselection = Items.Any(i => i.Candidate.IsPreselected);
        foreach (var item in Items)
        {
            item.IsSelected = !item.IsSkipped && (!hasPreselection || item.Candidate.IsPreselected);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }

        SelectAllCommand = new RelayCommand(() => SetSelection(i => !i.IsSkipped));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
    }

    public List<StandardNameItem> Items { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    public string Summary =>
        $"Всего: {Items.Count} · будет изменено: {Items.Count(i => i.WillChange)} · уже в норме (пропущено): {Items.Count(i => i.IsSkipped)}";

    /// <summary>Отмеченные изделия, у которых наименование действительно изменится.</summary>
    public IReadOnlyList<StandardNameItem> GetChanges() => Items.Where(i => i.WillChange).ToList();

    private void SetSelection(Func<StandardNameItem, bool> selector)
    {
        foreach (var item in Items)
        {
            item.IsSelected = selector(item);
        }
    }
}
