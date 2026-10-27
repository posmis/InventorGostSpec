namespace InventorGostSpec.UI.ViewModels;

/// <summary>
/// Подтверждение действия, которое меняет файлы: выполнить можно только после того, как отмечена галочка.
/// </summary>
public sealed class ConfirmationViewModel : ObservableObject
{
    private bool _isConfirmed;

    public ConfirmationViewModel(string title, string message, string checkText, string actionText)
    {
        Title = title;
        Message = message;
        CheckText = checkText;
        ActionText = actionText;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Текст галочки («Понимаю, что…»).</summary>
    public string CheckText { get; }

    /// <summary>Текст кнопки действия («Выполнить»).</summary>
    public string ActionText { get; }

    public bool IsConfirmed
    {
        get => _isConfirmed;
        set => SetField(ref _isConfirmed, value);
    }
}
