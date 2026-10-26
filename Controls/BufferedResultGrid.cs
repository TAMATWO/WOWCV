namespace WOWCV.Controls;

internal sealed class BufferedResultGrid : DataGridView
{
    public BufferedResultGrid()
    {
        DoubleBuffered = true;
        AllowUserToOrderColumns = true;
    }
}
