using System.Windows;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

/// <summary>Предупреждение с обязательной галочкой перед действием, которое меняет файлы.</summary>
public partial class ConfirmationWindow : Window
{
    private readonly ConfirmationViewModel _viewModel;

    public ConfirmationWindow(ConfirmationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => ConfirmBox.Focus();
    }

    private void OnRun(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsConfirmed)
        {
            DialogResult = true;
        }
    }
}
