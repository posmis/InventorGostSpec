using Inventor;
using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Запись номеров позиций из нашей спецификации в BOM Inventor (Item Number),
/// чтобы выноски на чертеже совпадали со спецификацией.
/// </summary>
internal sealed class WritePositionsCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.WritePositions";

    public override string InternalName => Id;

    public override string DisplayName => "Позиции\nв BOM";

    public override string Description => "Записать номера позиций из спецификации в спецификацию Inventor (BOM)";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var document = (Document)assembly;
        var specification = SpecificationStore.Load(document);
        if (specification is null)
        {
            context.Messages.Warning("Спецификация ещё не сформирована. Выполните «Обновить спецификацию».");
            return;
        }

        var positions = specification.Rows
            .Where(r => r.Kind == RowKind.Component && !r.IsRemoved && r.Position.HasValue && r.ComponentKey is not null)
            .GroupBy(r => r.ComponentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Position!.Value, StringComparer.OrdinalIgnoreCase);

        var written = 0;
        var missing = new List<string>();
        using (var transaction = new InventorTransaction(context.Application, document, "Позиции в BOM"))
        {
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

            transaction.Commit();
        }

        var message = $"Записано позиций: {written}";
        if (missing.Count > 0)
        {
            message += "\n\nНет в спецификации или без номера позиции (обновите спецификацию):\n" + string.Join("\n", missing.Take(30));
            if (missing.Count > 30)
            {
                message += $"\n... и ещё {missing.Count - 30}";
            }
        }

        context.Messages.Info(message);
    }
}
