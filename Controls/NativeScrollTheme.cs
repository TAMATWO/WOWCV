using System.Runtime.InteropServices;

namespace WOWCV.Controls;

/// <summary>只切換捲動條的原生繪製主題，不取代捲動或輸入處理。</summary>
internal static class NativeScrollTheme
{
    /// <summary>套用目前視窗的捲動條配色；不建立控制項 Handle 或改動全機設定。</summary>
    public static void Apply(Control control, bool dark)
    {
        if (!control.IsHandleCreated || SystemInformation.HighContrast)
            return;
        bool scrollbar = control is ScrollBar
            or ScrollableControl { AutoScroll: true };
        bool text = control is TextBoxBase { Multiline: true };
        if (!scrollbar && !text)
            return;
        SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : null,
            dark && scrollbar ? "ScrollBar" : null);
        control.Invalidate();
    }

    /// <summary>指定單一視窗的視覺樣式，回復淺色時清除本程式的覆寫。</summary>
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? appName, string? idList);
}
