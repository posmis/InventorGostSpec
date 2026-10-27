using Inventor;
using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Запись номеров позиций из сохранённой спецификации в BOM Inventor,
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
            context.Messages.Warning("Спецификация ещё не сформирована. Откройте «Спецификация» и обновите её из сборки.");
            return;
        }

        string report;
        using (var transaction = new InventorTransaction(context.Application, document, "Позиции в BOM"))
        {
            report = PositionWriter.Write(assembly, specification);
            transaction.Commit();
        }

        context.Messages.Info(report);
    }
}
