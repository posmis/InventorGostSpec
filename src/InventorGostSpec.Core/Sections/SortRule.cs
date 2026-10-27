namespace InventorGostSpec.Core.Sections;

/// <summary>Правило сортировки строк внутри раздела.</summary>
public enum SortRule
{
    /// <summary>По коду документа в последовательности ГОСТ 2.102, затем по обозначению.</summary>
    Documentation,

    /// <summary>В порядке возрастания обозначений (комплексы, сборочные единицы, детали, комплекты).</summary>
    ByDesignation,

    /// <summary>По категории стандарта, группе, наименованию, обозначению стандарта, параметрам.</summary>
    StandardProducts,

    /// <summary>По однородным группам, наименованию, параметрам.</summary>
    OtherProducts,

    /// <summary>По видам материалов, наименованию, параметрам.</summary>
    Materials,

    /// <summary>В алфавитном порядке наименований.</summary>
    ByName,
}
