namespace InventorGostSpec.Pdf;

/// <summary>
/// Высота шрифтов PDF (высота прописных букв, мм, ГОСТ 2.304). Хранится в настройках утилиты (pdf.json);
/// значения по умолчанию — как в бланке спецификации КОМПАС-3D.
/// </summary>
public sealed class FontSizes
{
    /// <summary>Строки таблицы: изделия, заголовки разделов и групп, текст, формат.</summary>
    public double TableRows { get; set; } = 5;

    /// <summary>Горизонтальные заголовки граф: «Обозначение», «Наименование», «Примечание».</summary>
    public double ColumnTitles { get; set; } = 5;

    /// <summary>Вертикальные заголовки граф: «Формат», «Зона», «Поз.», «Кол.».</summary>
    public double VerticalTitles { get; set; } = 3.5;

    /// <summary>Обозначение документа в основной надписи.</summary>
    public double Designation { get; set; } = 7;

    /// <summary>Наименование изделия в основной надписи (если в файле Excel не задано своё).</summary>
    public double Name { get; set; } = 5;

    /// <summary>Организация в основной надписи (одной строкой).</summary>
    public double Organization { get; set; } = 7;

    /// <summary>Надписи и данные основной надписи: «Изм.», «Разраб.», фамилии, даты, изменения, литера, листы.</summary>
    public double TitleBlock { get; set; } = 3.5;

    /// <summary>Графы на поле подшивки, «Копировал», «Формат А4».</summary>
    public double Margin { get; set; } = 3.5;

    /// <summary>Неположительные значения (испорченный файл настроек) заменяются значениями по умолчанию.</summary>
    public FontSizes Normalized()
    {
        var defaults = new FontSizes();
        static double Pick(double value, double fallback) => value > 0 && value < 100 ? value : fallback;
        return new FontSizes
        {
            TableRows = Pick(TableRows, defaults.TableRows),
            ColumnTitles = Pick(ColumnTitles, defaults.ColumnTitles),
            VerticalTitles = Pick(VerticalTitles, defaults.VerticalTitles),
            Designation = Pick(Designation, defaults.Designation),
            Name = Pick(Name, defaults.Name),
            Organization = Pick(Organization, defaults.Organization),
            TitleBlock = Pick(TitleBlock, defaults.TitleBlock),
            Margin = Pick(Margin, defaults.Margin),
        };
    }
}
