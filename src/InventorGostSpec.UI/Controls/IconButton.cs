using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InventorGostSpec.UI.Controls;

/// <summary>
/// Плоская кнопка панели команд: значок 16×16 и подпись. Шаблон — неявный стиль в Themes/Controls.xaml.
/// </summary>
public class IconButton : Button
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(IconButton));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(IconButton));

    public static readonly DependencyProperty IsPrimaryProperty =
        DependencyProperty.Register(nameof(IsPrimary), typeof(bool), typeof(IconButton));

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Подпись. Пусто — только значок (подпись тогда в подсказке).</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Основное действие окна: синяя кнопка.</summary>
    public bool IsPrimary
    {
        get => (bool)GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }
}
