using InventorGostSpec.Core.Model;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Связь окна спецификации с Inventor (реализуется в аддине).</summary>
public interface ISpecificationHost
{
    /// <summary>Имя сборки для заголовка окна.</summary>
    string DocumentName { get; }

    /// <summary>Папка сборки: относительно неё хранятся пути сторонних документов.</summary>
    string AssemblyFolder { get; }

    SpecSettings LoadSettings();

    void SaveSettings(SpecSettings settings);

    /// <summary>Текущий состав первого уровня сборки.</summary>
    IReadOnlyList<ComponentData> ReadComponents();

    /// <summary>Записывает спецификацию в файл сборки.</summary>
    void Save(Specification specification);

    /// <summary>Записывает номера позиций в BOM Inventor и возвращает отчёт для пользователя.</summary>
    string WritePositions(Specification specification);

    /// <summary>
    /// Текущие значения свойств спецификации (iProperties SPEC_*) компонента.
    /// <c>null</c> — компонента нет в сборке.
    /// </summary>
    IReadOnlyDictionary<string, string?>? ReadComponentProperties(string componentKey);

    /// <summary>
    /// Записывает свойства в iProperties компонента (имя свойства → значение, пустое — удалить)
    /// и возвращает его свежие данные для обновления строки.
    /// </summary>
    ComponentData WriteComponentProperties(string componentKey, IReadOnlyDictionary<string, string> changes);

    /// <summary>Раздел, который компонент получает автоматически, по типу (если SPEC_Section не задан).</summary>
    string GetAutoSectionId(string componentKey);

    /// <summary>
    /// Примечание из свойств компонента (пользовательское свойство из настроек, иначе «Комментарии»).
    /// Пустая строка — примечания нет; <c>null</c> — компонента нет в сборке.
    /// </summary>
    string? ReadComponentNote(string componentKey);

    /// <summary>
    /// Чертежи компонентов и форматы всех их листов («А1, А2х3, А2») по ключу компонента.
    /// Компонента нет в сборке — ключа нет в ответе; чертёж не найден — <see cref="DrawingFormat.DrawingPath"/> пуст.
    /// </summary>
    IReadOnlyDictionary<string, DrawingFormat> FindDrawingFormats(IReadOnlyList<string> componentKeys);

    /// <summary>Полный путь к файлу компонента (модели или чертежа из «Документации»); <c>null</c> — нет в сборке.</summary>
    string? GetComponentFile(string componentKey);

    /// <summary>Номера позиций в BOM Inventor (Item Number) по ключу компонента — для проверки расхождений.</summary>
    IReadOnlyDictionary<string, string> ReadBomItemNumbers();

    /// <summary>Данные основной надписи (из iProperties сборки) и путь к файлу экспорта по умолчанию.</summary>
    ExportInfo GetExportInfo();

    /// <summary>
    /// Записывает в iProperties сборки фамилии (Разработал, Проверил, Н. контр., Утвердил), организацию
    /// и «Перв. примен.» (SPEC_FirstUse).
    /// </summary>
    void WriteTitleProperties(TitleBlock title);

    /// <summary>
    /// Строит PDF по ГОСТ из файла Excel внешней утилитой (InventorGostSpec.PdfTool).
    /// <c>null</c> — готово, иначе текст ошибки для пользователя.
    /// </summary>
    string? BuildPdf(string excelPath, string pdfPath);

    /// <summary>
    /// Открывает окно утилиты PDF (шрифт, размеры шрифтов, сужение), подставив файл Excel, если он есть.
    /// <c>null</c> — открыто, иначе текст ошибки.
    /// </summary>
    string? OpenPdfSettings(string? excelPath);

    /// <summary>
    /// Состав сборки-исполнения (как <see cref="ReadComponents"/>, ключи — относительно папки головной сборки)
    /// и её обозначение. <c>null</c> — файл не открылся.
    /// </summary>
    VersionData? ReadVersion(string path);

    /// <summary>Записывает в сборку-исполнение пометку «исполнение сборки …» (SPEC_VersionOf).</summary>
    void MarkVersion(string path, string headDesignation);

    /// <summary>Убирает пометку об исполнении из сборки.</summary>
    void UnmarkVersion(string path);

    /// <summary>
    /// Номера позиций групповой спецификации головной сборки для этой сборки-исполнения: ключ компонента (этой сборки)
    /// → номер. Ошибка (нет пути к головной, нет спецификации, исполнения нет) — исключение с текстом для пользователя.
    /// </summary>
    IReadOnlyDictionary<string, int>? ReadHeadPositions();

    /// <summary>Эта сборка — исполнение другой: текст сообщения; <c>null</c> — нет.</summary>
    string? VersionOf { get; }

    /// <summary>Записывает непредвиденную ошибку окна в журнал.</summary>
    void ReportError(Exception exception);
}

/// <param name="DrawingPath">Найденный чертёж; <c>null</c> — чертежа нет.</param>
/// <param name="Format">Форматы листов чертежа; пусто — не распознаны.</param>
public sealed record DrawingFormat(string? DrawingPath, string Format);

/// <param name="Designation">Обозначение сборки-исполнения (Part Number без «СБ»).</param>
/// <param name="GroupTitles">Группы «устанавливаемые по …» её спецификации: ключ компонента → заголовок группы.</param>
public sealed record VersionData(
    string Designation,
    IReadOnlyList<ComponentData> Components,
    IReadOnlyDictionary<string, string>? GroupTitles = null);

/// <param name="DefaultPath">Путь к файлу Excel по умолчанию (папка сборки).</param>
public sealed record ExportInfo(TitleBlock TitleBlock, string DefaultPath);

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

    bool EditComponentProperties(AssignSectionViewModel viewModel);

    bool EditTitleBlock(TitleBlockViewModel viewModel);

    /// <summary>Выбор файла для сохранения Excel. <c>null</c> — отмена.</summary>
    string? AskExportPath(string defaultPath);

    /// <summary>Выбор файла PDF. <c>null</c> — отмена.</summary>
    string? AskPdfPath(string defaultPath);

    /// <summary>
    /// Показывает замечания проверки. Возвращает строку, к которой нужно перейти (<c>null</c> — просто закрыть).
    /// </summary>
    string? ShowCheckResults(CheckViewModel viewModel);

    /// <summary>Выбор сборки-исполнения (.iam). <c>null</c> — отмена.</summary>
    string? AskVersionFile(string folder);

    /// <summary>Выбор сторонних документов (PDF, Word…) для «Документации». <c>null</c> — отмена.</summary>
    IReadOnlyList<string>? AskDocumentFiles(string folder);

    /// <summary>Открывает папку файла в Проводнике с выделенным файлом.</summary>
    void ShowInFolder(string path);

    /// <summary>Открывает файл в связанной программе (Excel).</summary>
    void OpenFile(string path);
}
