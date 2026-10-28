using InventorGostSpec.Core.Model;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Tests;

internal sealed class FakeHost : ISpecificationHost
{
    public List<ComponentData> Components { get; } = new();

    public SpecSettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public Specification? Saved { get; private set; }

    public int WritePositionsCount { get; private set; }

    public string DocumentName => "Сборка.iam";

    public string AssemblyFolder { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Сборка");

    public SpecSettings LoadSettings() => Settings;

    public void SaveSettings(SpecSettings settings) => Settings = settings;

    public IReadOnlyList<ComponentData> ReadComponents() => Components.ToList();

    public void Save(Specification specification)
    {
        SaveCount++;
        Saved = specification;
    }

    public string WritePositions(Specification specification)
    {
        WritePositionsCount++;
        return "ok";
    }

    /// <summary>Ошибки, переданные окном в журнал.</summary>
    public List<Exception> Errors { get; } = new();

    public void ReportError(Exception exception) => Errors.Add(exception);

    /// <summary>Компоненты, запись свойств которых завершается ошибкой (файл только для чтения).</summary>
    public HashSet<string> ReadOnlyComponents { get; } = new();

    /// <summary>Примечания из свойств компонентов по ключу.</summary>
    public Dictionary<string, string> Notes { get; } = new();

    public string? ReadComponentNote(string componentKey) =>
        Components.All(c => c.Key != componentKey) ? null : Notes.TryGetValue(componentKey, out var note) ? note : "";

    public string ExportFolder { get; set; } = Path.GetTempPath();

    /// <summary>Основная надпись из «iProperties сборки».</summary>
    public Func<TitleBlock> Title { get; set; } = () => new TitleBlock { Designation = "АБВГ.301234.001", Name = "Привод" };

    public ExportInfo GetExportInfo() => new(Title(), Path.Combine(ExportFolder, "spec.xlsx"));

    /// <summary>Записанные в «iProperties сборки» данные основной надписи.</summary>
    public List<TitleBlock> WrittenTitles { get; } = new();

    public void WriteTitleProperties(TitleBlock title) => WrittenTitles.Add(title);

    /// <summary>Вызовы утилиты PDF: (Excel, PDF). Пока идёт вызов, временный Excel существует.</summary>
    public List<(string Excel, string Pdf, bool ExcelExisted)> PdfCalls { get; } = new();

    public string? PdfError { get; set; }

    /// <summary>Чертежи и их форматы по ключу компонента; нет ключа — чертёж не найден.</summary>
    public Dictionary<string, string> Drawings { get; } = new();

    public List<IReadOnlyList<string>> DrawingRequests { get; } = new();

    /// <summary>Файлы компонентов по ключу.</summary>
    public Dictionary<string, string> Files { get; } = new();

    public string? GetComponentFile(string componentKey) => Files.TryGetValue(componentKey, out var file) ? file : null;

    public IReadOnlyDictionary<string, DrawingFormat> FindDrawingFormats(IReadOnlyList<string> componentKeys)
    {
        DrawingRequests.Add(componentKeys);
        return componentKeys
            .Where(k => Components.Any(c => c.Key == k))
            .ToDictionary(k => k, k => Drawings.TryGetValue(k, out var format) ? new DrawingFormat(k + ".idw", format) : new DrawingFormat(null, ""));
    }

    /// <summary>Номера позиций в BOM; <c>null</c> — чтение BOM завершается ошибкой.</summary>
    public Dictionary<string, string>? BomItemNumbers { get; set; } = new();

    public IReadOnlyDictionary<string, string> ReadBomItemNumbers() =>
        BomItemNumbers ?? throw new InvalidOperationException("BOM недоступна.");

    public List<string?> SettingsOpened { get; } = new();

    public string? OpenPdfSettings(string? excelPath)
    {
        SettingsOpened.Add(excelPath);
        return null;
    }

    /// <summary>Вызывается, пока временный Excel для PDF ещё существует.</summary>
    public Action<string>? OnBuildPdf { get; set; }

    /// <summary>Основная надпись во временном Excel последнего вызова утилиты PDF.</summary>
    public TitleBlock? LastPdfTitle { get; private set; }

    public string? BuildPdf(string excelPath, string pdfPath)
    {
        PdfCalls.Add((excelPath, pdfPath, File.Exists(excelPath)));
        LastPdfTitle = Excel.ExcelReader.Read(excelPath).TitleBlock;
        OnBuildPdf?.Invoke(excelPath);
        return PdfError;
    }

    public string GetAutoSectionId(string componentKey)
    {
        var component = Components.Single(c => c.Key == componentKey);
        return SectionResolver.GetDefault(component.Kind, component.IsContentCenter);
    }

    /// <summary>iProperties SPEC_* компонентов по ключу.</summary>
    public Dictionary<string, Dictionary<string, string>> Properties { get; } = new();

    public IReadOnlyDictionary<string, string?>? ReadComponentProperties(string componentKey)
    {
        if (Components.All(c => c.Key != componentKey))
        {
            return null;
        }

        return Properties.TryGetValue(componentKey, out var values)
            ? values.ToDictionary(p => p.Key, p => (string?)p.Value)
            : new Dictionary<string, string?>();
    }

    public ComponentData WriteComponentProperties(string componentKey, IReadOnlyDictionary<string, string> changes)
    {
        if (ReadOnlyComponents.Contains(componentKey))
        {
            throw new InvalidOperationException("Файл только для чтения.");
        }

        var values = Properties.TryGetValue(componentKey, out var existing) ? existing : Properties[componentKey] = new();
        foreach (var (name, value) in changes)
        {
            values[name] = value;
        }

        var component = Components.Single(c => c.Key == componentKey);
        if (changes.TryGetValue(Core.Storage.PropertyNames.Section, out var section))
        {
            component.SectionId = section;
        }

        if (changes.TryGetValue(Core.Storage.PropertyNames.Format, out var format))
        {
            component.Format = format;
        }

        return component;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;

    public bool? SaveAnswer { get; set; } = true;

    public List<string> Warnings { get; } = new();

    public List<string> Infos { get; } = new();

    public Action<NewSectionViewModel>? OnNewSection { get; set; }

    public Action<ReserveSettingsViewModel>? OnReserves { get; set; }

    public Action<GroupViewModel>? OnGroup { get; set; }

    /// <summary>Путь, который «выбирает» пользователь при экспорте; <c>null</c> — отмена.</summary>
    public Func<string, string?> ExportPath { get; set; } = path => path;

    public List<string> OpenedFiles { get; } = new();

    public string? AskExportPath(string defaultPath) => ExportPath(defaultPath);

    public string? AskPdfPath(string defaultPath) => ExportPath(defaultPath);

    /// <summary>Последнее показанное окно проверки и что «выбрал» пользователь (строка для перехода).</summary>
    public CheckViewModel? LastCheck { get; private set; }

    public Func<CheckViewModel, string?> OnCheck { get; set; } = _ => null;

    public string? ShowCheckResults(CheckViewModel viewModel)
    {
        LastCheck = viewModel;
        return OnCheck(viewModel);
    }

    public void OpenFile(string path) => OpenedFiles.Add(path);

    public List<string> ShownInFolder { get; } = new();

    public void ShowInFolder(string path) => ShownInFolder.Add(path);

    /// <summary>Файлы, которые «выберет» пользователь в окне выбора документов.</summary>
    public IReadOnlyList<string>? DocumentFiles { get; set; }

    public IReadOnlyList<string>? AskDocumentFiles(string folder) => DocumentFiles;

    public bool Confirm(string text) => ConfirmAnswer;

    public void Info(string text) => Infos.Add(text);

    public void Warning(string text) => Warnings.Add(text);

    public bool? AskSave() => SaveAnswer;

    public bool EditNewSection(NewSectionViewModel viewModel)
    {
        OnNewSection?.Invoke(viewModel);
        return OnNewSection is not null;
    }

    public Action<AssignSectionViewModel>? OnComponentProperties { get; set; }

    public bool EditComponentProperties(AssignSectionViewModel viewModel)
    {
        OnComponentProperties?.Invoke(viewModel);
        return OnComponentProperties is not null;
    }

    public Action<TitleBlockViewModel>? OnTitleBlock { get; set; }

    public bool EditTitleBlock(TitleBlockViewModel viewModel)
    {
        OnTitleBlock?.Invoke(viewModel);
        return OnTitleBlock is not null;
    }

    public bool EditGroup(GroupViewModel viewModel)
    {
        OnGroup?.Invoke(viewModel);
        return OnGroup is not null;
    }

    public bool EditReserves(ReserveSettingsViewModel viewModel)
    {
        OnReserves?.Invoke(viewModel);
        return OnReserves is not null;
    }
}
