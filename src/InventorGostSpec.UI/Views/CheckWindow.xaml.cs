using System.Windows;
using System.Windows.Input;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

/// <summary>Список замечаний проверки; «Перейти к строке» закрывает окно и выделяет строку в спецификации.</summary>
public partial class CheckWindow : Window
{
    private readonly CheckViewModel _viewModel;

    public CheckWindow(CheckViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => List.Focus();
    }

    private void OnGoTo(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanGoToRow)
        {
            DialogResult = true;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnGoTo(sender, e);
}
