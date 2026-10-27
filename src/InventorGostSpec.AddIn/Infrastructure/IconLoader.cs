using System.Drawing;
using System.Runtime.InteropServices;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>
/// Значки кнопок из встроенных PNG (Resources/Icons) в формате IPictureDisp, который принимает Inventor.
/// Значок создаётся как HICON — так сохраняется прозрачность.
/// </summary>
internal static class IconLoader
{
    private const int PictureTypeIcon = 3;
    private static readonly Guid PictureDispId = new("7BF80981-BF32-101A-8BBB-00AA00300CAB");

    /// <param name="name">Имя значка, например «specification».</param>
    /// <param name="size">16 или 32.</param>
    /// <param name="dark">Вариант для тёмной темы Inventor.</param>
    /// <returns>IPictureDisp или <c>Type.Missing</c>, если значок не найден — кнопка будет без значка.</returns>
    public static object Load(string name, int size, bool dark)
    {
        var resource = $"{typeof(IconLoader).Assembly.GetName().Name}.Resources.Icons.{name}-{size}{(dark ? "-dark" : "")}.png";
        try
        {
            using var stream = typeof(IconLoader).Assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                return Type.Missing;
            }

            using var bitmap = new Bitmap(stream);
            var description = new PictureDescription
            {
                Size = Marshal.SizeOf<PictureDescription>(),
                Type = PictureTypeIcon,
                Handle = bitmap.GetHicon(),
            };

            var iid = PictureDispId;
            var result = OleCreatePictureIndirect(ref description, ref iid, true, out var picture);
            Marshal.ThrowExceptionForHR(result);
            return picture;
        }
        catch (Exception ex)
        {
            Log.Error("IconLoader " + resource, ex);
            return Type.Missing;
        }
    }

    /// <summary>PICTDESC: размер объединения — два указателя.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PictureDescription
    {
        public int Size;
        public int Type;
        public IntPtr Handle;
        public IntPtr Reserved;
    }

    [DllImport("oleaut32.dll")]
    private static extern int OleCreatePictureIndirect(
        ref PictureDescription description,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Bool)] bool own,
        [MarshalAs(UnmanagedType.IDispatch)] out object picture);
}
