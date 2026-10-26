using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WOWCV.Services;

/// <summary>Keyboard output to the current foreground window; throws on unsuccessful
/// injection.</summary>
public sealed class WindowsSendInput : ISendInput
{
    public const uint InputMarker = 0x574F5743;

    /// <summary>
    /// 送出指定按鍵的按下事件。
    /// </summary>
    public void KeyDown(Keys key) => Send(key, false);

    /// <summary>
    /// 送出指定按鍵的放開事件。
    /// </summary>
    public void KeyUp(Keys key) => Send(key, true);

    /// <summary>
    /// 透過 Windows SendInput 送出單一輸入事件，失敗時回報作業系統錯誤。
    /// </summary>
    private static void Send(Keys key, bool up)
    {
        bool extended = key is Keys.RControlKey or Keys.RMenu or Keys.Insert or Keys.Delete
            or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Up or Keys.Down
            or Keys.Left or Keys.Right or Keys.NumLock or Keys.Divide or Keys.PrintScreen
            or Keys.LWin or Keys.RWin or Keys.Apps;
        var input = new Input
        {
            Type = 1,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    Flags = (up ? 2u : 0) | (extended ? 1u : 0),
                    ExtraInformation = InputMarker
                }
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法送出鍵盤輸入。");
    }

    /// <summary>
    /// 拒絕目前鍵盤施放服務不支援的滑鼠按下操作。
    /// </summary>
    public void MouseDown(MouseButtons button) => throw new NotSupportedException("目前僅支援鍵盤施放。");

    /// <summary>
    /// 拒絕目前鍵盤施放服務不支援的滑鼠放開操作。
    /// </summary>
    public void MouseUp(MouseButtons button) => throw new NotSupportedException("目前僅支援鍵盤施放。");

    /// <summary>
    /// 拒絕目前鍵盤施放服務不支援的滑鼠移動操作。
    /// </summary>
    public void MoveMouse(int deltaX, int deltaY) => throw new NotSupportedException("目前僅支援鍵盤施放。");

    /// <summary>
    /// 拒絕目前鍵盤施放服務不支援的滑鼠滾輪操作。
    /// </summary>
    public void ScrollMouse(int delta,
        bool horizontal = false) => throw new NotSupportedException("目前僅支援鍵盤施放。");
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInformation;
    }

    /// <summary>
    /// 呼叫 Win32 API 注入鍵盤或滑鼠輸入事件。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
