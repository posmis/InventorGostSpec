using InventorGostSpec.Core.Model;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Запись номеров позиций спецификации в BOM Inventor (Item Number).</summary>
internal static class PositionWriter
{
    private const int MaxListed = 30;

    /// <returns>Отчёт для пользователя.</returns>
    public static string Write(AssemblyDocument assembly, Specification specification)
    {
        var positions = specification.Rows
            .Where(r => r.Kind == RowKind.Component && !r.IsRemoved && r.Position.HasValue && r.ComponentKey is not null)
            .GroupBy(r => r.ComponentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Position!.Value, StringComparer.OrdinalIgnoreCase);

        var written = 0;
        var missing = new List<string>();
        foreach (var item in BomAccess.GetFirstLevel(assembly))
        {
            if (!positions.TryGetValue(item.Key, out var position))
            {
                missing.Add(item.Target.DisplayName);
                continue;
            }

            var number = position.ToString();
            if (item.Row.ItemNumber != number)
            {
                item.Row.ItemNumber = number;
            }

            written++;
        }

        var report = $"Записано позиций в BOM: {written}";
        if (missing.Count > 0)
        {
            report += "\n\nНет в спецификации или без номера позиции (обновите спецификацию):\n" +
                      string.Join("\n", missing.Take(MaxListed));
            if (missing.Count > MaxListed)
            {
                report += $"\n... и ещё {missing.Count - MaxListed}";
            }
        }

        return report;
    }
}
