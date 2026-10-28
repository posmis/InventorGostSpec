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

    public string NormControl { get; set; } = "";

    public string Approver { get; set; } = "";

    /// <summary>Первичное применение.</summary>
    public string FirstUse { get; set; } = "";

    /// <summary>Высота шрифта наименования в основной надписи для этого документа, мм; <c>null</c> — из настроек утилиты PDF.</summary>
    public double? NameTextHeight { get; set; }

    /// <summary>Изменения документа (графы «Изм.», «Лист», «№ докум.» основной надписи) — по порядку внесения.</summary>
    public List<ChangeRecord> Changes { get; set; } = new();
}

/// <summary>Запись об изменении документа (ГОСТ 2.503), как в графах изменений основной надписи.</summary>
public sealed class ChangeRecord
{
    /// <summary>Порядковый номер изменения («Изм.»).</summary>
    public string Number { get; set; } = "";

    /// <summary>Количество участков, изменённых на листе («Кол. уч.»).</summary>
    public string Areas { get; set; } = "";

    /// <summary>Номер изменённого листа («Лист»).</summary>
    public string Sheet { get; set; } = "";

    /// <summary>Обозначение документа об изменении («№ докум.»), например извещения.</summary>
    public string Document { get; set; } = "";

    /// <summary>Подпись («Подп.») — фамилия внёсшего изменение.</summary>
    public string Signature { get; set; } = "";

    /// <summary>Дата внесения («Дата»).</summary>
    public string Date { get; set; } = "";

    public ChangeRecord Clone() => (ChangeRecord)MemberwiseClone();

    /// <summary>Ни одна графа не заполнена (пустая строка таблицы изменений).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        new[] { Number, Areas, Sheet, Document, Signature, Date }.All(v => string.IsNullOrWhiteSpace(v));
}

/// <summary>
/// Данные основной надписи, которых нет в iProperties, — хранятся в спецификации (в файле сборки):
/// литера, высота шрифта наименования, изменения. Фамилии, организация и «Перв. примен.» — в iProperties сборки.
/// </summary>
public sealed class SpecTitleData
{
    /// <summary>Заполнялось в окне спецификации; нет — берётся из прежнего файла Excel (как раньше).</summary>
    public bool IsSet { get; set; }

    public string Letter { get; set; } = "";

    public double? NameTextHeight { get; set; }

    public List<ChangeRecord> Changes { get; set; } = new();

    /// <summary>Переносит данные в основную надпись.</summary>
    public void ApplyTo(TitleBlock title)
    {
        title.Letter = Letter;
        title.NameTextHeight = NameTextHeight;
        title.Changes = Changes.Where(c => !c.IsEmpty).Select(c => c.Clone()).ToList();
    }
}
