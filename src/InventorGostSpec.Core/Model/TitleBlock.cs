namespace InventorGostSpec.Core.Model;

/// <summary>Данные основной надписи спецификации (ГОСТ 2.104).</summary>
public sealed class TitleBlock
{
    /// <summary>Обозначение документа (обозначение сборки без «СБ»).</summary>
    public string Designation { get; set; } = "";

    /// <summary>Наименование изделия.</summary>
    public string Name { get; set; } = "";

    public string Organization { get; set; } = "";

    /// <summary>Литера.</summary>
    public string Letter { get; set; } = "";

    public string Developer { get; set; } = "";

    public string Checker { get; set; } = "";

    public string TechControl { get; set; } = "";

    public string NormControl { get; set; } = "";

    public string Approver { get; set; } = "";

    /// <summary>Первичное применение.</summary>
    public string FirstUse { get; set; } = "";
}
