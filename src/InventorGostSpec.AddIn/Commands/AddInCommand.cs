using InventorGostSpec.AddIn.Infrastructure;

namespace InventorGostSpec.AddIn.Commands;

internal abstract class AddInCommand
{
    /// <summary>Внутреннее имя кнопки Inventor.</summary>
    public abstract string InternalName { get; }

    /// <summary>Подпись кнопки, '\n' — перенос строки на ленте.</summary>
    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    /// <summary>Имя значка в Resources/Icons (без размера и темы).</summary>
    public abstract string IconName { get; }

    public abstract void Execute(AddInContext context);
}
