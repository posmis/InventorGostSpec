using System.Windows;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class GroupWindow : Window
{
    private readonly GroupViewModel _viewModel;

    public GroupWindow(GroupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text.Length;
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var error = _viewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
