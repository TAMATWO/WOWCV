using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

public sealed partial class RoiPictureBox
{
    private bool legendEnabled;
    private ToolTip? legendTip;
    private int hoveredLegend = -1;
    private Rectangle[] legendBounds = [];
    private static readonly (Color Color, bool Dashed, string Tip)[] legendItems =
    [
        (Color.LimeGreen, false,
            "辨識命中：OpenCV 候選通過，且已啟用的五點驗證通過。"),
        (Color.Yellow, false,
            "候選待確認：OpenCV 候選通過，但五點驗證未通過。"),
        (Color.LimeGreen, true,
            "定位後只做五點驗證：通過。在記住的位置檢查，不需當次 OpenCV 通過。"),
        (Color.Cyan, true,
            "定位後只做五點驗證：未通過。在記住的位置檢查，不需當次 OpenCV 通過。"),
        (Color.Orange, false,
            "ROI：使用者框選的偵測範圍，不代表命中狀態。")
    ];

    [DefaultValue(false)]
    public bool LegendEnabled
    {
        get => legendEnabled;
        set
        {
            if (legendEnabled == value)
                return;
            legendEnabled = value;
            ClearLegendTip();
            Invalidate();
        }
    }

    /// <summary>
    /// 將圖例排列於下方留白右下角，空間不足時不佔用圖片範圍。
    /// </summary>
    private Rectangle[] GetLegendBounds()
    {
        if (!LegendEnabled || Image is null)
            return [];
        var imageBounds = DisplayBounds();
        if (imageBounds.IsEmpty)
            return [];
        int side = LogicalToDeviceUnits(20);
        int gap = LogicalToDeviceUnits(12);
        int margin = LogicalToDeviceUnits(8);
        int width = side * legendItems.Length + gap * (legendItems.Length - 1);
        int availableHeight = ClientSize.Height - imageBounds.Bottom;
        if (ClientSize.Width < width + margin * 2 || availableHeight < side + margin * 2)
            return [];
        int left = ClientSize.Width - margin - width;
        int top = ClientSize.Height - margin - side;
        return Enumerable.Range(0, legendItems.Length)
            .Select(index => new Rectangle(left + index * (side + gap), top, side, side))
            .ToArray();
    }

    /// <summary>
    /// 僅於控件留白繪製圖例，不寫入預覽圖片或原始影格。
    /// </summary>
    private void DrawLegend(Graphics graphics)
    {
        var nextBounds = GetLegendBounds();
        if (!legendBounds.SequenceEqual(nextBounds))
            ClearLegendTip();
        legendBounds = nextBounds;
        for (int index = 0; index < legendBounds.Length; index++)
        {
            var item = legendItems[index];
            using var brush = new SolidBrush(item.Color);
            graphics.FillRectangle(brush, legendBounds[index]);
            if (item.Dashed)
            {
                using var pen = new Pen(Color.White, LogicalToDeviceUnits(2))
                {
                    DashStyle = DashStyle.Dash
                };
                var border = legendBounds[index];
                border.Inflate(-LogicalToDeviceUnits(1), -LogicalToDeviceUnits(1));
                graphics.DrawRectangle(pen, border);
            }
        }
    }

    /// <summary>
    /// 滑鼠切換圖例時更新提示，使用與繪圖相同的範圍判斷。
    /// </summary>
    private void UpdateLegendTip(Point location)
    {
        var bounds = GetLegendBounds();
        int index = Array.FindIndex(bounds, rect => rect.Contains(location));
        if (index == hoveredLegend)
            return;
        ClearLegendTip();
        if (index < 0)
            return;
        hoveredLegend = index;
        legendTip ??= new ToolTip();
        legendTip.SetToolTip(this, legendItems[index].Tip);
    }

    /// <summary>
    /// 離開圖例或顯示範圍改變時清除舊提示。
    /// </summary>
    private void ClearLegendTip()
    {
        hoveredLegend = -1;
        legendTip?.SetToolTip(this, null);
    }
}
