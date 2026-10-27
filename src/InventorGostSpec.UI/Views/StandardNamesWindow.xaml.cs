using System.Windows;
using System.Windows.Controls;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class StandardNamesWindow : Window
{
    private readonly StandardNamesViewModel _viewModel;

    public StandardNamesWindow(StandardNamesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var count = _viewModel.GetChanges().Count;
        if (count == 0)
        {
            MessageBox.Show(this, "Нет отмеченных изделий, у которых изменится наименование.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Изменить наименование у {count} стандартных изделий?", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            DialogResult = true;
        }
    }
}
