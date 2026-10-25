namespace WOWCV.Services;

/// <summary>Input output contract. Tests supply a recording implementation.</summary>
public interface ISendInput
{
    void KeyDown(Keys key);
    void KeyUp(Keys key);
    void MouseDown(MouseButtons button);
    void MouseUp(MouseButtons button);
    void MoveMouse(int deltaX, int deltaY);
    void ScrollMouse(int delta, bool horizontal = false);
}
