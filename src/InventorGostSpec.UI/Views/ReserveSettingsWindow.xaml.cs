using System.Windows;
using System.Windows.Controls;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class ReserveSettingsWindow : Window
{
    public ReserveSettingsWindow(ReserveSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Items.CommitEdit(DataGridEditingUnit.Row, true);
        DialogResult = true;
    }
}
