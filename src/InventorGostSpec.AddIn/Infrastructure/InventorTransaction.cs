using Inventor;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>
/// Транзакция Inventor: все изменения команды отменяются одним «Отменить».
/// Без вызова <see cref="Commit"/> изменения откатываются.
/// </summary>
internal sealed class InventorTransaction : IDisposable
{
    private Transaction? _transaction;

    public InventorTransaction(Inventor.Application application, Document document, string name)
    {
        _transaction = application.TransactionManager.StartTransaction((_Document)document, name);
    }

    public void Commit()
    {
        _transaction?.End();
        _transaction = null;
    }

    public void Dispose()
    {
        _transaction?.Abort();
        _transaction = null;
    }
}
