using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WOWCV.Services;

public sealed class RawInput : IRawInput
{
    private nint buffer;
    private uint capacity;
    private bool disposed;
    public bool IsRunning
    {
        get; private set;
    }

    public event EventHandler<KeyboardInputEventArgs>? KeyboardInput;
    public event EventHandler<MouseInputEventArgs>? MouseInput;
    public event EventHandler<InputErrorEventArgs>? Error;

    /// <summary>
    /// 為指定視窗註冊鍵盤與滑鼠的背景原始輸入事件。
    /// </summary>
    public void Start(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (windowHandle == 0)
            throw new ArgumentException("Window handle is required.", nameof(windowHandle));
        Stop();
        if (!RegisterRawInputDevices(Devices(0x100, windowHandle), 2,
            (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法註冊 Raw Input。");
        IsRunning = true;
    }

    /// <summary>
    /// 停止接收輸入並解除已註冊的原始輸入裝置。
    /// </summary>
    public void Stop()
    {
        if (!IsRunning)
            return;
        if (!RegisterRawInputDevices(Devices(1, 0), 2, (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法解除 Raw Input 註冊。");
        IsRunning = false;
    }

    /// <summary>
    /// 建立鍵盤與滑鼠原始輸入裝置的註冊資料。
    /// </summary>
    private static RawDevice[] Devices(uint flags, nint target) => [
        new()
        {
            UsagePage = 1,
            Usage = 6,
            Flags = flags,
            Target = target
        },
        new()
        {
            UsagePage = 1,
            Usage = 2,
            Flags = flags,
            Target = target
        }
    ];

    /// <summary>
    /// 辨識原始輸入訊息並交由封包處理流程解析。
    /// </summary>
    public void ProcessMessage(int message, nint lParam)
    {
        if (message != 0xFF || !IsRunning || disposed)
            return;
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(lParam, 0x10000003, 0, ref size, headerSize) == uint.MaxValue)
        {
            ReportReadError();
            return;
        }

        if (size < headerSize || size > 1024 * 1024)
            return;
        if (size > capacity)
        {
            nint replacement = Marshal.AllocHGlobal(checked((int)size));
            Marshal.FreeHGlobal(buffer);
            buffer = replacement;
            capacity = size;
        }

        uint read = GetRawInputData(lParam, 0x10000003, buffer, ref size, headerSize);
        if (read == uint.MaxValue)
        {
            ReportReadError();
            return;
        }

        if (read < headerSize || read > capacity)
            return;
        ProcessPacket(buffer, read);
    }

    /// <summary>
    /// 解析鍵盤或滑鼠封包並發布對應輸入事件。
    /// </summary>
    private void ProcessPacket(nint packet, uint read)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawHeader>();
        if (read < headerSize)
            return;
        RawHeader header = Marshal.PtrToStructure<RawHeader>(packet);
        if (header.Size < headerSize || header.Size > read)
            return;
        nint payload = packet + (int)headerSize;
        if (header.Type == 1 && header.Size >= headerSize + Marshal.SizeOf<RawKeyboard>())
        {
            RawKeyboard k = Marshal.PtrToStructure<RawKeyboard>(payload);
            if (k.VKey == 0 || k.VKey >= 255 || k.MakeCode == 0xFF)
                return;
            Keys key = (Keys)k.VKey;
            if (key == Keys.ShiftKey)
                key = (Keys)MapVirtualKey(k.MakeCode, 3);
            else if (key == Keys.ControlKey)
                key = (k.Flags & 2) != 0 ? Keys.RControlKey : Keys.LControlKey;
            else if (key == Keys.Menu)
                key = (k.Flags & 2) != 0 ? Keys.RMenu : Keys.LMenu;
            KeyboardInput?.Invoke(
                this,
                new()
                {
                    Device = header.Device,
                    Key = key,
                    ScanCode = k.MakeCode,
                    Flags = k.Flags,
                    IsDown = (k.Flags & 1) == 0,
                    ExtraInformation = k.ExtraInformation
                }
            );
        }
        else if (header.Type == 0 && header.Size >= headerSize + Marshal.SizeOf<RawMouse>())
        {
            RawMouse m = Marshal.PtrToStructure<RawMouse>(payload);
            MouseInput?.Invoke(
                this,
                new()
                {
                    Device = header.Device,
                    Buttons = (MouseTransitions)(m.ButtonFlags & 0x3FF),
                    VerticalWheel = (m.ButtonFlags & 0x400) != 0 ? (short)m.ButtonData : 0,
                    HorizontalWheel = (m.ButtonFlags & 0x800) != 0 ? (short)m.ButtonData : 0,
                    X = m.X,
                    Y = m.Y,
                    IsAbsolute = (m.Flags & 1) != 0,
                    IsVirtualDesktop = (m.Flags & 2) != 0
                }
            );
        }
    }

    /// <summary>
    /// 將原始輸入讀取錯誤發布給服務使用者。
    /// </summary>
    private void ReportReadError() => Error?.Invoke(this,
        new(new Win32Exception(Marshal.GetLastWin32Error()).Message));

    /// <summary>
    /// 釋放此物件持有的資源，必要時先停止背景工作。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;
        try
        {
            Stop();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            buffer = 0;
            capacity = 0;
            disposed = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader
    {
        public uint Type, Size;
        public nint Device, WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode, Flags, Reserved, VKey;
        public uint Message, ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct RawMouse
    {
        [FieldOffset(0)]
        public ushort Flags;
        [FieldOffset(4)]
        public ushort ButtonFlags;
        [FieldOffset(6)]
        public ushort ButtonData;
        [FieldOffset(12)]
        public int X;
        [FieldOffset(16)]
        public int Y;
    }

    /// <summary>
    /// 呼叫 Win32 API 註冊或解除原始輸入裝置。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count,
        uint size);

    /// <summary>
    /// 呼叫 Win32 API 讀取原始輸入封包資料。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size,
        uint headerSize);

    /// <summary>
    /// 呼叫 Win32 API 在虛擬按鍵與掃描碼之間轉換。
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
