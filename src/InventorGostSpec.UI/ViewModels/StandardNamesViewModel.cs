using System.Windows.Input;
using InventorGostSpec.Core.Parsing;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Стандартное изделие для переименования (данные из Inventor).</summary>
/// <param name="Key">Полный путь к файлу.</param>
/// <param name="FileName">Имя файла без расширения — источник наименования.</param>
/// <param name="CurrentName">Текущее наименование (Description).</param>
/// <param name="IsPreselected">Компонент выбран в сборке.</param>
public sealed record StandardNameCandidate(string Key, string FileName, string CurrentName, bool IsPreselected);

public enum StandardNameStatus
{
    /// <summary>Наименование будет заменено.</summary>
    Change,

    /// <summary>Не удалось найти наименование изделия или стандарт — нужно проверить вручную.</summary>
    Review,

    /// <summary>Текущее наименование уже в нужном виде — пропускается.</summary>
    AlreadyOk,
}

public sealed class StandardNameItem : ObservableObject
{
    private string _proposed;
    private bool _isSelected;

    public StandardNameItem(StandardNameCandidate candidate)
    {
        Candidate = candidate;

        // Наименование изделия («Болт») может быть только в текущем наименовании, а не в имени файла.
        var fallback = StandardDesignationParser.Parse(candidate.CurrentName).Name;
        var result = StandardNameFormatter.FromFileName(candidate.FileName, fallback);
        _proposed = result.Text;

        Status = IsAlreadyOk(candidate.CurrentName, result.Text)
            ? StandardNameStatus.AlreadyOk
            : result.IsComplete ? StandardNameStatus.Change : StandardNameStatus.Review;
    }

    public StandardNameCandidate Candidate { get; }

    public string FileName => Candidate.FileName;

    public string Current => Candidate.CurrentName;

    public StandardNameStatus Status { get; }

    public string StatusText => Status switch
    {
        StandardNameStatus.Change => "Будет изменено",
        StandardNameStatus.Review => "Проверьте",
        _ => "Уже в норме",
    };

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

    /// <summary>
    /// Наименование уже в норме: совпадает с предложенным или само имеет вид «наименование, размеры, стандарт»
    /// (размеры обязательны — «Болт ГОСТ 7798-70» без размера в норме не считается).
    /// </summary>
    private static bool IsAlreadyOk(string current, string proposed) =>
        current.Trim() == proposed ||
        (StandardNameFormatter.IsWellFormed(current) && StandardNameFormatter.FromFileName(current).Parts.Parameters.Length > 0);

    /// <summary>Будет ли запись действительно что-то менять.</summary>
    public bool WillChange => IsSelected && Proposed.Length > 0 && Proposed != Current.Trim();
}

/// <summary>Предпросмотр переименования стандартных изделий.</summary>
public sealed class StandardNamesViewModel : ObservableObject
{
    public StandardNamesViewModel(IEnumerable<StandardNameCandidate> candidates)
    {
        Items = candidates
            .Select(c => new StandardNameItem(c))
            .OrderBy(i => i.Status == StandardNameStatus.AlreadyOk)
            .ThenBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Если в сборке что-то выбрано — отмечаем только выбранное.
        var hasPreselection = Items.Any(i => i.Candidate.IsPreselected);
        foreach (var item in Items)
        {
            item.IsSelected = item.Status == StandardNameStatus.Change && (!hasPreselection || item.Candidate.IsPreselected);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }

        SelectAllCommand = new RelayCommand(() => SetSelection(i => i.Status != StandardNameStatus.AlreadyOk));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
    }

    public List<StandardNameItem> Items { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    public string Summary
    {
        get
        {
            var toChange = Items.Count(i => i.WillChange);
            var ok = Items.Count(i => i.Status == StandardNameStatus.AlreadyOk);
            var review = Items.Count(i => i.Status == StandardNameStatus.Review);
            return $"Всего: {Items.Count} · будет изменено: {toChange} · уже в норме (пропущено): {ok}" +
                   (review > 0 ? $" · требуют проверки: {review}" : "");
        }
    }

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
