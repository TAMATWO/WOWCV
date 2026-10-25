using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WOWCV.Services;

/// <summary>Owns registration and a message-only window on one dedicated STA thread.
/// Event handlers execute on that thread and must return promptly; UI consumers must marshal updates.</summary>
public sealed class RawInputService : IDisposable
{
    private readonly ConcurrentDictionary<Keys, byte> pressed = new();
    private readonly ManualResetEventSlim ready = new(false);
    private Thread? thread;
    private nint windowHandle;
    private Exception? startupError;
    private MouseInputEventArgs? latestMouse;
    private bool disposed;
    public event EventHandler<KeyboardInputEventArgs>? KeyboardInput;
    public event EventHandler<MouseInputEventArgs>? MouseInput;
    public event EventHandler<InputErrorEventArgs>? Error;
    public bool IsPressed(Keys key) => pressed.ContainsKey(key);
    public Keys[] PressedKeys => pressed.Keys.ToArray();
    public MouseInputEventArgs? LatestMouse => Volatile.Read(ref latestMouse);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread is not null) return;
        thread = new Thread(Run) { IsBackground = true, Name = "WOWCV Raw Input" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (startupError is not null) throw new InvalidOperationException("Raw Input 啟動失敗", startupError);
    }

    private void Run()
    {
        try
        {
            using var input = new RawInput();
            using var window = new InputWindow(input);
            input.KeyboardInput += (_, e) =>
            {
                if (e.ExtraInformation == WindowsSendInput.InputMarker) return;
                if (e.IsDown) pressed[e.Key] = 0; else pressed.TryRemove(e.Key, out byte ignored);
                KeyboardInput?.Invoke(this, e);
            };
            input.MouseInput += (_, e) => { Volatile.Write(ref latestMouse, e); MouseInput?.Invoke(this, e); };
            input.Error += (_, e) => Error?.Invoke(this, e);
            using var loop = new ApplicationContext();
            windowHandle = window.Handle;
            input.Start(window.Handle);
            ready.Set();
            Application.Run(loop);
        }
        catch (Exception ex)
        {
            if (!ready.IsSet) startupError = ex;
            else Error?.Invoke(this, new(ex.Message));
        }
        finally { pressed.Clear(); ready.Set(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (thread is not null)
        {
            ready.Wait();
            if (Thread.CurrentThread == thread) Application.ExitThread();
            else
            {
                if (thread.IsAlive && windowHandle != 0) PostMessage(windowHandle, 0x8001, 0, 0);
                thread.Join();
            }
        }
        // The worker's finally signals ready. Keep the handle alive until it has exited.
        if (thread is null || Thread.CurrentThread != thread) ready.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);

    private sealed class InputWindow : NativeWindow, IDisposable
    {
        private readonly IRawInput input;
        public InputWindow(IRawInput input)
        {
            this.input = input;
            CreateHandle(new CreateParams { Caption = "WOWCV Input", Parent = new nint(-3) });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x8001) { Application.ExitThread(); return; }
            try { input.ProcessMessage(m.Msg, m.LParam); }
            finally { base.WndProc(ref m); }
        }
        public void Dispose()
        {
            try { input.Stop(); }
            finally { DestroyHandle(); }
        }
    }
}
