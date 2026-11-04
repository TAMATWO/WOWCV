using System.Reflection;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;
using static SkillEditorUi;

internal static class UiThemeTests
{
    /// <summary>驗證主題配置相容、三頁配色、布局與編輯狀態不受切換影響。</summary>
    public static void Run(string root, Action<bool, string> check)
    {
        check(WorkbenchConfigurationStore.Parse("{\"version\":1}").Theme == UiTheme.Light,
            "Old configuration defaults to light theme");
        foreach (string value in new[] { "\"unknown\"", "3", "null", "{}", "[]" })
        {
            var parsed = WorkbenchConfigurationStore.Parse(
                "{\"version\":1,\"theme\":" + value + ",\"search\":{\"threshold\":0.93}}");
            check(parsed.Theme == UiTheme.Light && parsed.Search.Threshold == 0.93,
                "Unknown theme preserves other settings: " + value);
        }
        string path = Path.Combine(root, "theme", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string imagePath = Path.Combine(root, "theme-source.png");
        using (var image = new Bitmap(800, 200))
            image.Save(imagePath);
        File.WriteAllText(path, WorkbenchConfigurationStore.Serialize(new()
        {
            Source = new() { Kind = SourceKind.Image, ImagePath = imagePath }
        }));
        using var form = new Form1(path)
        {
            ShowInTaskbar = false,
            Opacity = 0,
            StartPosition = FormStartPosition.Manual,
            Location = new(-30000, -30000)
        };
        form.Show();
        var detection = Find<DetectionControl>(form).Single();
        var tabs = form.Controls.OfType<TabControl>().Single();
        var toggle = Find<Button>(form).Single(button => button.Name == "ThemeToggle");
        var editor = Find<SkillEditorControl>(form).Single();
        tabs.SelectedIndex = 2;
        Load(editor, new SkillDocument
        {
            Data = [new() { Press = "E", Process = Enumerable.Range(0, 35)
                .Select(i => new SkillEntry { SkillName = "S" + i, Priority = 2,
                    SkillWeight = 80 }).ToList() }]
        });
        var grid = Grid(editor);
        grid.CurrentCell = grid.Rows[12].Cells["SkillName"];
        grid.FirstDisplayedScrollingRowIndex = 10;
        grid.BeginEdit(true);
        var input = (TextBox)grid.EditingControl!;
        input.Text = "尚未提交";
        input.SelectionStart = 2;
        input.SelectionLength = 2;
        var cell = grid.CurrentCell;
        int scroll = grid.FirstDisplayedScrollingRowIndex;
        string snapshot = Snapshot(editor);
        var rectangles = tabs.TabPages.Cast<TabPage>().Select(page => page.Bounds).ToArray();
        var preview = Find<RoiPictureBox>(detection).Single();
        var previewBackground = preview.BackColor;
        long version = Version(detection);
        MouseClick(toggle);
        check(detection.SelectedTheme == UiTheme.Dark, "Theme button switches to dark");
        check(tabs.TabPages.Cast<TabPage>().All(page => page.BackColor.GetBrightness() < 0.3),
            "All three pages receive dark surfaces");
        check(grid.IsCurrentCellInEditMode && grid.EditingControl == input
            && input.Text == "尚未提交" && input.SelectionStart == 2 && input.SelectionLength == 2,
            "Theme switch preserves active editor, uncommitted text and text selection");
        check(grid.CurrentCell == cell && grid.FirstDisplayedScrollingRowIndex == scroll
            && Snapshot(editor) == snapshot,
            "Theme switch preserves selection, scroll and document");
        check(input.BackColor.GetBrightness() < 0.3
            && input.ForeColor.GetBrightness() > 0.7, "Active editor has readable dark colors");
        check(Version(detection) == version && preview.BackColor == previewBackground,
            "Theme switch preserves detection version and preview colors");
        var footer = Field<Label>(editor, "footer");
        footer.ForeColor = Color.DarkRed;
        check(footer.ForeColor.R > 200 && footer.ForeColor.G > 100,
            "Dynamic error text remains readable in dark mode");
        using (var icon = new Bitmap(toggle.Width, toggle.Height))
        {
            toggle.DrawToBitmap(icon, new Rectangle(Point.Empty, icon.Size));
            icon.Save(Path.Combine(root, "theme-sun.png"));
        }
        for (int index = 0; index < tabs.TabCount; index++)
        {
            if (index == 0)
            {
                // 產物僅使用合成圖片，不保存桌面擷取內容。
                var previous = preview.Image;
                preview.Image = new Bitmap(800, 200);
                previous?.Dispose();
            }
            grid.CancelEdit();
            tabs.SelectedIndex = index;
            Application.DoEvents();
            using var rendered = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, $"theme-dark-page-{index + 1}.png"));
        }
        footer.ForeColor = Color.DarkRed;
        toggle.PerformClick();
        check(detection.SelectedTheme == UiTheme.Light && footer.ForeColor == Color.DarkRed,
            "Light theme restores the latest semantic warning color");
        toggle.PerformClick();
        footer.ForeColor = editor.ForeColor;
        toggle.PerformClick();
        check(footer.ForeColor == editor.ForeColor,
            "Normal dynamic status text restores readable light colors");
        check(tabs.TabPages.Cast<TabPage>().Select(page => page.Bounds)
            .SequenceEqual(rectangles), "Theme switches preserve content bounds");
        form.Size = form.MinimumSize;
        form.PerformLayout();
        var last = tabs.GetTabRect(tabs.TabCount - 1);
        check(toggle.Left > tabs.Left + last.Right && toggle.Bottom <= tabs.Top + last.Bottom,
            "Theme icon fits beside tabs at minimum window size");
        form.Scale(new SizeF(1.5f, 1.5f));
        form.PerformLayout();
        last = tabs.GetTabRect(tabs.TabCount - 1);
        check(toggle.Left > tabs.Left + last.Right && toggle.Bottom <= tabs.Top + last.Bottom,
            "Theme icon stays in tab strip after simulated scaling");
        toggle.PerformClick();
        check(detection.FlushConfiguration() is null, "Theme preference flushes successfully");
        using var restored = new Form1(path)
        {
            ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000)
        };
        restored.Show();
        check(Find<DetectionControl>(restored).Single().SelectedTheme == UiTheme.Dark
            && restored.BackColor.GetBrightness() < 0.3, "Restart restores saved dark theme");
    }

    /// <summary>取得辨識設定版本，確認純外觀切換沒有要求重新分析。</summary>
    private static long Version(DetectionControl detection) => (long)typeof(DetectionControl)
        .GetField("version", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(detection)!;

    /// <summary>走過圖示的滑鼠按下與放開路徑，驗證點擊不搶走表格編輯焦點。</summary>
    private static void MouseClick(Button button)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2,
            button.Height / 2, 0);
        button.GetType().GetMethod("OnMouseDown", flags)!.Invoke(button, [mouse]);
        button.GetType().GetMethod("OnMouseUp", flags)!.Invoke(button, [mouse]);
    }
}
