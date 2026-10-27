using System.Windows;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class AssignSectionWindow : Window
{
    public AssignSectionWindow(AssignSectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
