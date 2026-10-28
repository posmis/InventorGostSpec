using System.Windows;
using System.Windows.Controls;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class TitleBlockWindow : Window
{
    private readonly TitleBlockViewModel _viewModel;

    public TitleBlockWindow(TitleBlockViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => DeveloperBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // Незавершённая правка ячейки таблицы изменений тоже сохраняется.
        ChangesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var error = _viewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
