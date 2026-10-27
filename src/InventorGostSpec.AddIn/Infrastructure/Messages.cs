using System.Runtime.InteropServices;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Сообщения поверх окна Inventor.</summary>
internal readonly struct Messages
{
    private const uint MbOk = 0x0;
    private const uint MbYesNo = 0x4;
    private const uint MbIconError = 0x10;
    private const uint MbIconQuestion = 0x20;
    private const uint MbIconWarning = 0x30;
    private const uint MbIconInformation = 0x40;
    private const int IdYes = 6;

    private readonly IntPtr _owner;

    public Messages(IntPtr owner)
    {
        _owner = owner;
    }

    public void Info(string text) => Show(text, MbOk | MbIconInformation);

    public void Warning(string text) => Show(text, MbOk | MbIconWarning);

    public void Error(string text) => Show(text, MbOk | MbIconError);

    public bool Confirm(string text) => Show(text, MbYesNo | MbIconQuestion) == IdYes;

    private int Show(string text, uint type) => MessageBoxW(_owner, text, AddInInfo.Title, type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
