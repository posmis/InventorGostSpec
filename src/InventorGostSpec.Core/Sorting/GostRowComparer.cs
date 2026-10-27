using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Sorting;

/// <summary>Порядок строк внутри раздела по ГОСТ 2.106-2019.</summary>
public sealed class GostRowComparer : IComparer<SpecRow>
{
    private readonly SortRule _rule;

    public GostRowComparer(SortRule rule)
    {
        _rule = rule;
    }

    public int Compare(SpecRow? x, SpecRow? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // Группа сортировки, заданная вручную, важнее правил ГОСТ; строки без группы — после всех групп.
        var byGroup = (x.SortGroup ?? int.MaxValue).CompareTo(y.SortGroup ?? int.MaxValue);
        if (byGroup != 0)
        {
            return byGroup;
        }

        var result = _rule switch
        {
            SortRule.Documentation => CompareDocuments(x, y),
            SortRule.ByDesignation => CompareByDesignation(x, y),
            SortRule.StandardProducts => CompareStandardProducts(x, y),
            SortRule.OtherProducts => CompareOtherProducts(x, y),
            SortRule.Materials => CompareMaterials(x, y),
            SortRule.ByName => 0,
            _ => 0,
        };

        return result != 0 ? result : CompareFallback(x, y);
    }

    private static int CompareDocuments(SpecRow x, SpecRow y) =>
        First(
            DocumentCodes.Compare(x.Get(SpecColumn.Designation), y.Get(SpecColumn.Designation)),
            () => NaturalComparer.CompareEmptyLast(x.Get(SpecColumn.Designation), y.Get(SpecColumn.Designation)));

    private static int CompareByDesignation(SpecRow x, SpecRow y) =>
        NaturalComparer.CompareEmptyLast(x.Get(SpecColumn.Designation), y.Get(SpecColumn.Designation));

    /// <summary>
    /// Категория стандарта → группа по назначению → наименование изделия →
    /// обозначение стандарта → основные параметры.
    /// </summary>
    private static int CompareStandardProducts(SpecRow x, SpecRow y)
    {
        var fx = SortFields.Of(x);
        var fy = SortFields.Of(y);
        return First(
            StandardCategories.Categorize(fx.StandardNumber).CompareTo(StandardCategories.Categorize(fy.StandardNumber)),
            () => NaturalComparer.CompareEmptyLast(fx.Group, fy.Group),
            () => NaturalComparer.CompareEmptyLast(fx.SortName, fy.SortName),
            () => NaturalComparer.CompareEmptyLast(
                StandardCategories.StripPrefix(fx.StandardNumber),
                StandardCategories.StripPrefix(fy.StandardNumber)),
            () => NaturalComparer.CompareEmptyLast(fx.Parameters, fy.Parameters));
    }

    /// <summary>Однородная группа → наименование → основные параметры.</summary>
    private static int CompareOtherProducts(SpecRow x, SpecRow y)
    {
        var fx = SortFields.Of(x);
        var fy = SortFields.Of(y);
        return First(
            NaturalComparer.CompareEmptyLast(fx.Group, fy.Group),
            () => NaturalComparer.CompareEmptyLast(fx.SortName, fy.SortName),
            () => NaturalComparer.CompareEmptyLast(fx.Parameters, fy.Parameters));
    }

    /// <summary>Вид материала → наименование → параметры.</summary>
    private static int CompareMaterials(SpecRow x, SpecRow y)
    {
        var fx = SortFields.Of(x);
        var fy = SortFields.Of(y);
        return First(
            MaterialKinds.Parse(x.Source.MaterialKind).CompareTo(MaterialKinds.Parse(y.Source.MaterialKind)),
            () => NaturalComparer.CompareEmptyLast(fx.SortName, fy.SortName),
            () => NaturalComparer.CompareEmptyLast(fx.Parameters, fy.Parameters));
    }

    private static int CompareFallback(SpecRow x, SpecRow y) =>
        First(
            NaturalComparer.CompareEmptyLast(x.Get(SpecColumn.Name), y.Get(SpecColumn.Name)),
            () => NaturalComparer.CompareEmptyLast(x.Get(SpecColumn.Designation), y.Get(SpecColumn.Designation)));

    private static int First(int result, params Func<int>[] next)
    {
        foreach (var compare in next)
        {
            if (result != 0)
            {
                return result;
            }

            result = compare();
        }

        return result;
    }

    /// <summary>
    /// Поля сортировки строки. Незаполненные поля берутся из разбора наименования,
    /// например, для изделий, добавленных вручную.
    /// </summary>
    private sealed record SortFields(string Group, string SortName, string StandardNumber, string Parameters)
    {
        public static SortFields Of(SpecRow row)
        {
            var source = row.Source;
            var parsed = StandardDesignationParser.Parse(row.Get(SpecColumn.Name));

            // Стандарт — из наименования, которое видно в спецификации: SPEC_StandardNumber мог остаться
            // от старого наименования (например, «ISO …» вместо «ГОСТ ISO …»). Свойство — только если в наименовании стандарта нет.
            var standard = parsed.StandardNumber.Length > 0 ? parsed.StandardNumber : source.StandardNumber;
            var sortName = source.SortName.Length > 0 ? source.SortName : parsed.Name;
            var parameters = source.Parameters.Length > 0 ? source.Parameters : parsed.Parameters;

            return new SortFields(source.Group, sortName, standard, parameters);
        }
    }
}
