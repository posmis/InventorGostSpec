namespace InventorGostSpec.Excel;

/// <summary>
/// Формат файла Excel спецификации. Общий для экспорта и для утилиты, которая строит из файла PDF по ГОСТ:
/// названия листов, порядок колонок и значения колонки «Тип строки».
/// </summary>
public static class ExcelFormat
{
    public const string SpecificationSheet = "Спецификация";
    public const string TitleBlockSheet = "Основная надпись";
    public const string ChangesSheet = "Изменения";
    public const string HelpSheet = "Как заполнять";

    /// <summary>Колонки листа «Спецификация» (номер колонки — индекс + 1).</summary>
    public static readonly string[] Columns =
    {
        "Тип строки", "Формат", "Зона", "Поз.", "Обозначение", "Наименование", "Кол.", "Ед. изм.", "Примечание",
    };

    public const int TypeColumn = 1;
    public const int FormatColumn = 2;
    public const int ZoneColumn = 3;
    public const int PositionColumn = 4;
    public const int DesignationColumn = 5;
    public const int NameColumn = 6;
    public const int QuantityColumn = 7;
    public const int UnitColumn = 8;
    public const int NoteColumn = 9;

    /// <summary>
    /// Служебная колонка разметки листов: номер листа PDF у первой строки каждого листа.
    /// Заполняется при выгрузке и командой утилиты PDF; при чтении не используется.
    /// </summary>
    public const int SheetColumn = 10;

    public const string SheetColumnTitle = "Лист PDF";

    /// <summary>Значения колонки «Тип строки».</summary>
    public static class RowTypes
    {
        /// <summary>Заголовок группы «Изделия, устанавливаемые по …».</summary>
        public const string Group = "Группа";

        /// <summary>Заголовок раздела (в графе «Наименование», подчёркнут).</summary>
        public const string Section = "Раздел";

        /// <summary>Строка изделия или документа.</summary>
        public const string Item = "Изделие";

        /// <summary>Текстовая строка без позиции.</summary>
        public const string Text = "Текст";

        /// <summary>Пустая строка резервной позиции.</summary>
        public const string Reserve = "Резерв";

        /// <summary>Пустая строка, добавленная вручную (например, чтобы сдвинуть запись на следующий лист).</summary>
        public const string Blank = "Пустая";

        /// <summary>Разрыв листа: следующие строки начинаются с нового листа.</summary>
        public const string PageBreak = "Новый лист";

        public static readonly string[] All = { Group, Section, Item, Text, Reserve, Blank, PageBreak };
    }

    /// <summary>Поля листа «Основная надпись» (колонка A — поле, B — значение).</summary>
    public static class TitleFields
    {
        public const string Designation = "Обозначение";
        public const string Name = "Наименование";
        public const string Organization = "Организация";
        public const string Letter = "Литера";
        public const string Developer = "Разработал";
        public const string Checker = "Проверил";
        public const string TechControl = "Т. контр.";
        public const string NormControl = "Н. контр.";
        public const string Approver = "Утвердил";
        public const string FirstUse = "Перв. примен.";

        /// <summary>Высота шрифта наименования, мм (2,5; 3,5; 5; 7; 10…). Пусто — 5.</summary>
        public const string NameHeight = "Шрифт наименования, мм";
    }

    /// <summary>
    /// Колонки листа «Изменения» — графы изменений основной надписи (ГОСТ 2.104, заполнение по ГОСТ 2.503).
    /// Одна строка — одна запись, по порядку внесения; пустые строки пропускаются.
    /// </summary>
    public static readonly string[] ChangeColumns = { "Изм.", "Кол. уч.", "Лист", "№ докум.", "Подп.", "Дата" };

    public const int ChangeNumberColumn = 1;
    public const int ChangeAreasColumn = 2;
    public const int ChangeSheetColumn = 3;
    public const int ChangeDocumentColumn = 4;
    public const int ChangeSignatureColumn = 5;
    public const int ChangeDateColumn = 6;
}
