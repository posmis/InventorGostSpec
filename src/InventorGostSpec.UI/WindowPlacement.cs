using System.IO;
using System.Text.Json;
using System.Windows;

namespace InventorGostSpec.UI;

/// <summary>Запоминает размер и положение окна между запусками (%AppData%\InventorGostSpec\window-*.json).</summary>
public static class WindowPlacement
{
    private sealed record Placement(double Left, double Top, double Width, double Height, bool Maximized);

    public static void Attach(Window window, string name)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InventorGostSpec",
            $"window-{name}.json");

        window.SourceInitialized += (_, _) => Restore(window, path);
        // Closing, а не Closed: после закрытия окна RestoreBounds пуст (бесконечные координаты).
        window.Closing += (_, e) =>
        {
            if (!e.Cancel)
            {
                Save(window, path);
            }
        };
    }

    private static void Restore(Window window, string path)
    {
        try
        {
            if (!File.Exists(path) || JsonSerializer.Deserialize<Placement>(File.ReadAllText(path)) is not { } placement)
            {
                return;
            }

            // Окно должно оказаться на экране, даже если мониторов стало меньше.
            var screen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            if (!IsValid(placement.Left, placement.Top, placement.Width, placement.Height))
            {
                return;
            }

            var bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
            if (placement.Width < window.MinWidth || placement.Height < window.MinHeight || !screen.IntersectsWith(bounds))
            {
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            if (placement.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
        catch (Exception)
        {
            // Положение окна — удобство: при любой ошибке окно просто откроется по умолчанию.
        }
    }

    private static void Save(Window window, string path)
    {
        try
        {
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;
            if (bounds.IsEmpty || !IsValid(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
            {
                return;
            }

            var placement = new Placement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(placement));
        }
        catch (Exception)
        {
            // Не удалось сохранить положение — не страшно, работа окна от этого не зависит.
        }
    }

    private static bool IsValid(params double[] values) => values.All(double.IsFinite) && values[2] > 0 && values[3] > 0;
}
