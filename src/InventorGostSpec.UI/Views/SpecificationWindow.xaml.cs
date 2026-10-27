using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InventorGostSpec.Core.Model;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Views;

public partial class SpecificationWindow : Window, IDialogService
{
    private const string DragFormat = "InventorGostSpec.RowId";

    private static readonly Brush OverriddenBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xBF));

    private readonly ISpecificationHost _host;
    private SpecificationViewModel? _viewModel;
    private bool _restoringSelection;
    private bool _rebuilding;
    private double _savedOffset;

    private SpecificationWindow(ISpecificationHost host)
    {
        _host = host;
        InitializeComponent();
        CreateColumns();

        Grid.BeginningEdit += OnBeginningEdit;
        Grid.DragOver += OnDragOver;
        Grid.Drop += OnDrop;
        Grid.PreviewMouseRightButtonDown += OnPreviewRightButtonDown;
        Grid.SelectionChanged += (_, _) =>
        {
            if (!_restoringSelection)
            {
                _viewModel?.SetSelectedRows(Grid.SelectedItems.OfType<SpecRowViewModel>());
            }
        };
        Grid.ContextMenuOpening += (_, _) => ClearOverrideItem.CommandParameter = Grid.CurrentColumn?.SortMemberPath;

        PreviewKeyDown += OnPreviewKeyDown;
        WindowPlacement.Attach(this, "specification");

        Dispatcher.UnhandledException += OnUnhandledException;
        Closed += (_, _) => Dispatcher.UnhandledException -= OnUnhandledException;
    }

    /// <summary>Открывает окно спецификации модально поверх окна Inventor.</summary>
    /// <param name="isNew">Спецификация только что сформирована и ещё не сохранена в сборку.</param>
    public static void ShowDialog(ISpecificationHost host, Specification specification, IntPtr ownerHandle, bool isNew)
    {
        var window = new SpecificationWindow(host);
        var viewModel = new SpecificationViewModel(
            host,
            window,
            specification,
            isNew,
            action => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, action));
        window.Attach(viewModel);
        WindowOwner.ShowDialog(window, ownerHandle);
    }

    public bool Confirm(string text) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string text) => MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warning(string text) => MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool? AskSave() =>
        MessageBox.Show(this, "Сохранить изменения спецификации в сборку?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public bool EditNewSection(NewSectionViewModel viewModel) =>
        new NewSectionWindow(viewModel) { Owner = this }.ShowDialog() == true;

    public bool EditReserves(ReserveSettingsViewModel viewModel) =>
        new ReserveSettingsWindow(viewModel) { Owner = this }.ShowDialog() == true;

    public bool EditComponentProperties(AssignSectionViewModel viewModel) =>
        new AssignSectionWindow(viewModel) { Owner = this }.ShowDialog() == true;

    public bool EditGroup(GroupViewModel viewModel) =>
        new GroupWindow(viewModel) { Owner = this }.ShowDialog() == true;

    protected override void OnClosing(CancelEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_viewModel is not null && !_viewModel.CanClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void Attach(SpecificationViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.EditRequested += OnEditRequested;
        // Перестроение таблицы не должно сбивать прокрутку: после «По ГОСТ», «Перенумеровать», обновления
        // таблица остаётся на месте; к строке прокручиваем, только если операция касалась её.
        viewModel.Rebuilding += () =>
        {
            _rebuilding = true;
            _savedOffset = FindScrollViewer()?.VerticalOffset ?? 0;
        };
        viewModel.Rebuilt += target =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _rebuilding = false;
                if (target is not null)
                {
                    Grid.ScrollIntoView(target);
                }
                else
                {
                    FindScrollViewer()?.ScrollToVerticalOffset(_savedOffset);
                }
            });
        };
        viewModel.SelectionRestored += rows =>
        {
            _restoringSelection = true;
            try
            {
                foreach (var row in rows.Where(r => !Grid.SelectedItems.Contains(r)))
                {
                    Grid.SelectedItems.Add(row);
                }
            }
            finally
            {
                _restoringSelection = false;
            }
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            // Выделенная строка (после перехода к изменению, переноса, добавления) должна быть видна.
            if (!_rebuilding && e.PropertyName == nameof(SpecificationViewModel.SelectedRow) && viewModel.SelectedRow is { } row)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Grid.ScrollIntoView(row));
            }
        };
        viewModel.CommitEdits = () =>
        {
            if (!Grid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                Grid.CancelEdit(DataGridEditingUnit.Row);
            }
        };
    }

    private void CreateColumns()
    {
        Grid.Columns.Add(CreateHandleColumn());
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "",
            Binding = new Binding(nameof(SpecRowViewModel.StatusMark)),
            Width = 64,
            IsReadOnly = true,
            ElementStyle = CreateStatusStyle(),
        });

        AddColumn(SpecColumn.Format, "Формат", 58, center: true);
        AddColumn(SpecColumn.Zone, "Зона", 50, center: true);
        AddColumn(SpecColumn.Position, "Поз.", 50, center: true);
        AddColumn(SpecColumn.Designation, "Обозначение", 210, center: false);
        AddColumn(SpecColumn.Name, "Наименование", new DataGridLength(1, DataGridLengthUnitType.Star), center: false);
        AddColumn(SpecColumn.Quantity, "Кол.", 56, center: true);
        AddColumn(SpecColumn.Unit, "Ед. изм.", 60, center: true);
        AddColumn(SpecColumn.Note, "Примечание", 190, center: false);
    }

    private void AddColumn(SpecColumn column, string header, DataGridLength width, bool center)
    {
        var key = column.ToString();
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = width,
            MinWidth = 40,
            SortMemberPath = key,
            Binding = new Binding(key) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            ElementStyle = CreateCellStyle(column, center),
            EditingElementStyle = CreateEditingStyle(),
        });
    }

    private static Style CreateCellStyle(SpecColumn column, bool center)
    {
        var key = column.ToString();
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(3, 1, 3, 1)));
        style.Setters.Add(new Setter(ToolTipProperty, new Binding($"{nameof(SpecRowViewModel.ToolTips)}[{key}]")));
        if (center)
        {
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        }

        var overridden = new DataTrigger { Binding = new Binding($"{nameof(SpecRowViewModel.Overridden)}[{key}]"), Value = true };
        overridden.Setters.Add(new Setter(TextBlock.ForegroundProperty, OverriddenBrush));
        overridden.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
        style.Triggers.Add(overridden);

        if (column is SpecColumn.Designation or SpecColumn.Name)
        {
            var removed = new DataTrigger { Binding = new Binding(nameof(SpecRowViewModel.IsRemoved)), Value = true };
            removed.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Strikethrough));
            style.Triggers.Add(removed);
        }

        if (column == SpecColumn.Name)
        {
            // Заголовок раздела по ГОСТ: в графе «Наименование», по центру, подчёркнут.
            var header = new DataTrigger { Binding = new Binding(nameof(SpecRowViewModel.IsHeader)), Value = true };
            header.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline));
            header.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            style.Triggers.Add(header);
        }

        return style;
    }

    private static Style CreateEditingStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(1)));
        return style;
    }

    private static Style CreateStatusStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Gray));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(3, 0, 3, 0)));
        return style;
    }

    /// <summary>Колонка-ручка для перетаскивания строк.</summary>
    private DataGridTemplateColumn CreateHandleColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "⋮⋮");
        text.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(BackgroundProperty, Brushes.Transparent);
        text.SetValue(CursorProperty, Cursors.SizeAll);
        text.SetValue(ToolTipProperty, "Перетащите, чтобы изменить порядок");
        text.SetBinding(VisibilityProperty, new Binding(nameof(SpecRowViewModel.CanMove)) { Converter = new BooleanToVisibilityConverter() });
        text.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnHandleMouseDown));

        return new DataGridTemplateColumn
        {
            Header = "",
            Width = 24,
            CanUserResize = false,
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = text },
        };
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpecRowViewModel { CanMove: true, RowId: { } id } row })
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            Grid.SelectedItem = row;
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, id), DragDropEffects.Move);
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) && FindAncestor<DataGridRow>(e.OriginalSource) is { Item: SpecRowViewModel { CanMove: true } }
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is string id &&
            FindAncestor<DataGridRow>(e.OriginalSource) is { Item: SpecRowViewModel { RowId: { } target } })
        {
            _viewModel?.MoveRow(id, target);
        }

        e.Handled = true;
    }

    private void OnMoveToSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Option option } && _viewModel?.MoveToSectionCommand is { } command &&
            command.CanExecute(option.Id))
        {
            command.Execute(option.Id);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private ScrollViewer? FindScrollViewer() => FindDescendant<ScrollViewer>(Grid);

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Ctrl+F — к поиску, Esc в поиске — очистить.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            Grid.Focus();
            e.Handled = true;
        }
    }

    private void OnMoveToGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Option option } && _viewModel?.MoveToGroupCommand is { } command &&
            command.CanExecute(option.Id))
        {
            command.Execute(option.Id);
        }
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        e.Cancel = e.Row.Item is not SpecRowViewModel row ||
                   !Enum.TryParse<SpecColumn>(e.Column.SortMemberPath, out var column) ||
                   !row.CanEdit(column);
    }

    /// <summary>Правый клик выделяет строку и ячейку, к которым относится контекстное меню.</summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource);
        var row = FindAncestor<DataGridRow>(e.OriginalSource);
        if (row is null)
        {
            return;
        }

        // Правый щелчок по уже выбранной строке не сбрасывает множественный выбор.
        if (!row.IsSelected)
        {
            Grid.SelectedItem = row.Item;
        }

        if (cell is not null)
        {
            Grid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
        }
    }

    private void OnEditRequested(SpecRowViewModel row, SpecColumn column)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var gridColumn = Grid.Columns.FirstOrDefault(c => c.SortMemberPath == column.ToString());
            if (gridColumn is null)
            {
                return;
            }

            Grid.ScrollIntoView(row, gridColumn);
            Grid.SelectedItem = row;
            Grid.CurrentCell = new DataGridCellInfo(row, gridColumn);
            Grid.Focus();
            Grid.BeginEdit();
        });
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ошибка в окне не должна ронять Inventor.
        e.Handled = true;
        _host.ReportError(e.Exception);
        MessageBox.Show(this, e.Exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static T? FindAncestor<T>(object? source)
        where T : DependencyObject
    {
        var current = source as DependencyObject;
        while (current is not null && current is not T)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
