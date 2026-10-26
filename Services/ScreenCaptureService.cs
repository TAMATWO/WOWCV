using System.Drawing.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace WOWCV.Services;

public static class ScreenCaptureService
{
    /// <summary>
    /// 擷取指定螢幕矩形為影像，由呼叫端負責釋放回傳資源。
    /// </summary>
    public static Mat Capture(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("沒有可用的螢幕。");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        return BitmapConverter.ToMat(bitmap);
    }
}
