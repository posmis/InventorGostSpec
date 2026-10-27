using System.Windows;
using System.Windows.Interop;

namespace InventorGostSpec.UI;

public static class WindowOwner
{
    /// <summary>Делает окно дочерним для главного окна Inventor (по его HWND) и показывает модально.</summary>
    public static bool? ShowDialog(Window window, IntPtr ownerHandle)
    {
        if (ownerHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(window).Owner = ownerHandle;
        }

        return window.ShowDialog();
    }
}
