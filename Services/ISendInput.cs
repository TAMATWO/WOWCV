namespace WOWCV.Services;

/// <summary>Input output contract. Tests supply a recording implementation.</summary>
public interface ISendInput
{
    /// <summary>
    /// 送出指定按鍵的按下事件。
    /// </summary>
    void KeyDown(Keys key);

    /// <summary>
    /// 送出指定按鍵的放開事件。
    /// </summary>
    void KeyUp(Keys key);

    /// <summary>
    /// 送出指定滑鼠按鈕的按下事件。
    /// </summary>
    void MouseDown(MouseButtons button);

    /// <summary>
    /// 送出指定滑鼠按鈕的放開事件。
    /// </summary>
    void MouseUp(MouseButtons button);

    /// <summary>
    /// 送出指定座標或位移的滑鼠移動事件。
    /// </summary>
    void MoveMouse(int deltaX, int deltaY);

    /// <summary>
    /// 送出指定方向與幅度的滑鼠滾輪事件。
    /// </summary>
    void ScrollMouse(int delta, bool horizontal = false);
}
