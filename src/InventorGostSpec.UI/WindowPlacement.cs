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
        window.Closed += (_, _) => Save(window, path);
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
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Нет сохранённого положения — окно откроется по умолчанию.
        }
    }

    private static void Save(Window window, string path)
    {
        try
        {
            var bounds = window.RestoreBounds;
            var placement = new Placement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(placement));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не удалось сохранить положение — не страшно.
        }
    }
}
