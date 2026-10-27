using InventorGostSpec.Core.Model;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Связь окна спецификации с Inventor (реализуется в аддине).</summary>
public interface ISpecificationHost
{
    /// <summary>Имя сборки для заголовка окна.</summary>
    string DocumentName { get; }

    SpecSettings LoadSettings();

    void SaveSettings(SpecSettings settings);

    /// <summary>Текущий состав первого уровня сборки.</summary>
    IReadOnlyList<ComponentData> ReadComponents();

    /// <summary>Записывает спецификацию в файл сборки.</summary>
    void Save(Specification specification);

    /// <summary>Записывает номера позиций в BOM Inventor и возвращает отчёт для пользователя.</summary>
    string WritePositions(Specification specification);

    /// <summary>Записывает непредвиденную ошибку окна в журнал.</summary>
    void ReportError(Exception exception);
}

/// <summary>Диалоги, которые показывает окно спецификации.</summary>
public interface IDialogService
{
    bool Confirm(string text);

    void Info(string text);

    void Warning(string text);

    /// <summary>Сохранить изменения? <c>true</c> — да, <c>false</c> — нет, <c>null</c> — отмена.</summary>
    bool? AskSave();

    bool EditNewSection(NewSectionViewModel viewModel);

    bool EditReserves(ReserveSettingsViewModel viewModel);

    bool EditGroup(GroupViewModel viewModel);
}
