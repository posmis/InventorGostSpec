using InventorGostSpec.AddIn.Infrastructure;

namespace InventorGostSpec.AddIn.Commands;

internal abstract class AddInCommand
{
    /// <summary>Внутреннее имя кнопки Inventor.</summary>
    public abstract string InternalName { get; }

    /// <summary>Подпись кнопки, '\n' — перенос строки на ленте.</summary>
    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract void Execute(AddInContext context);
}
