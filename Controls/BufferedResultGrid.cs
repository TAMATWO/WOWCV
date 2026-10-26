namespace WOWCV.Controls;

internal sealed class BufferedResultGrid : DataGridView
{
    /// <summary>
    /// 初始化啟用雙緩衝的表格，降低更新儲存格時的閃爍。
    /// </summary>
    public BufferedResultGrid()
    {
        DoubleBuffered = true;
        AllowUserToOrderColumns = true;
    }
}
