using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WOWCV.Services;

/// <summary>Keyboard output to the current foreground window; throws on unsuccessful injection.</summary>
public sealed class WindowsSendInput : ISendInput
{
    public const uint InputMarker = 0x574F5743;
    public void KeyDown(Keys key) => Send(key, false);
    public void KeyUp(Keys key) => Send(key, true);
    private static void Send(Keys key, bool up)
    {
        bool extended = key is Keys.RControlKey or Keys.RMenu or Keys.Insert or Keys.Delete or Keys.Home or Keys.End
            or Keys.PageUp or Keys.PageDown or Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.NumLock or Keys.Divide
            or Keys.PrintScreen or Keys.LWin or Keys.RWin or Keys.Apps;
        var input = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput
        {
            VirtualKey = (ushort)key, Flags = (up ? 2u : 0) | (extended ? 1u : 0), ExtraInformation = InputMarker
        } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法送出鍵盤輸入。");
    }
    public void MouseDown(MouseButtons button) => throw new NotSupportedException("目前僅支援鍵盤施放。");
    public void MouseUp(MouseButtons button) => throw new NotSupportedException("目前僅支援鍵盤施放。");
    public void MoveMouse(int deltaX, int deltaY) => throw new NotSupportedException("目前僅支援鍵盤施放。");
    public void ScrollMouse(int delta, bool horizontal = false) => throw new NotSupportedException("目前僅支援鍵盤施放。");

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInformation; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
