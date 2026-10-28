namespace InventorGostSpec.Core.Storage;

/// <summary>Имена пользовательских iProperties (набор «Inventor User Defined Properties»).</summary>
public static class PropertyNames
{
    /// <summary>Раздел спецификации, значение из <see cref="Sections.SectionIds"/> или id пользовательского раздела.</summary>
    public const string Section = "SPEC_Section";

    /// <summary>Формат документа (А4, А3, ...).</summary>
    public const string Format = "SPEC_Format";

    /// <summary>Однородная группа изделий для сортировки.</summary>
    public const string Group = "SPEC_Group";

    /// <summary>Наименование изделия для сортировки («Болт»).</summary>
    public const string SortName = "SPEC_SortName";

    /// <summary>Обозначение стандарта («ГОСТ 7798-70»).</summary>
    public const string StandardNumber = "SPEC_StandardNumber";

    /// <summary>Основные параметры или размеры («М12х60»).</summary>
    public const string Parameters = "SPEC_Parameters";

    /// <summary>Вид материала, имя значения <see cref="Sections.MaterialKind"/>.</summary>
    public const string MaterialKind = "SPEC_MaterialKind";

    /// <summary>Единица измерения количества.</summary>
    public const string Unit = "SPEC_Unit";

    /// <summary>
    /// Первичная применяемость («Перв. примен.» в основной надписи чертежа и спецификации, ГОСТ 2.104) —
    /// обозначение изделия, в которое компонент впервые вошёл. Хранится в компоненте, а не в спецификации.
    /// </summary>
    public const string FirstUse = "SPEC_FirstUse";

    /// <summary>
    /// Пометка в сборке-исполнении: обозначение головной сборки, в групповую спецификацию которой она входит.
    /// Только для сообщения, ограничений не накладывает.
    /// </summary>
    public const string VersionOf = "SPEC_VersionOf";
}
