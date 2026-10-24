namespace WOWCV.Services;

/// <summary>Own one keyboard/mouse registration on the window thread.</summary>
public interface IRawInput : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<KeyboardInputEventArgs>? KeyboardInput;
    event EventHandler<MouseInputEventArgs>? MouseInput;
    event EventHandler<InputErrorEventArgs>? Error;
    void Start(nint windowHandle);
    void Stop();
    void ProcessMessage(int message, nint lParam);
}

public sealed class KeyboardInputEventArgs : EventArgs
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public nint Device { get; init; }
    public Keys Key { get; init; }
    public ushort ScanCode { get; init; }
    public ushort Flags { get; init; }
    public bool IsDown { get; init; }
}

[Flags]
public enum MouseTransitions : ushort
{
    None = 0, LeftDown = 1, LeftUp = 2, RightDown = 4, RightUp = 8,
    MiddleDown = 16, MiddleUp = 32, X1Down = 64, X1Up = 128,
    X2Down = 256, X2Up = 512
}

public sealed class MouseInputEventArgs : EventArgs
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public nint Device { get; init; }
    public MouseTransitions Buttons { get; init; }
    public int VerticalWheel { get; init; }
    public int HorizontalWheel { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsAbsolute { get; init; }
    public bool IsVirtualDesktop { get; init; }
}

public sealed class InputErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}