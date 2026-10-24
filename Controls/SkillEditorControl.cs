using System.Text.Json;
using System.Text.Encodings.Web;

using WOWCV.Models;
using WOWCV.Services;
namespace WOWCV.Controls;

public sealed class SkillEditorControl : UserControl
{
    private SkillDocument document = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly Stack<string> undo = new(), redo = new();
    private string saved = "";
    private bool loading;
    private int bindingIndex, skillIndex;
    private readonly ComboBox bindingList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox skillList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox pressInput = new(), nameInput = new(), checkInput = new();
    private readonly ComboBox statusInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
    private readonly Label footer = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(20, 5, 0, 0) };
    private Button undoButton = null!, redoButton = null!;
    private readonly List<Control> skillInputs = [];
    private Action<decimal> showPriority = _ => { }, showWeight = _ => { }, showCd = _ => { };
    private SkillBinding? SelectedBinding => document.Data.ElementAtOrDefault(bindingIndex);
    private SkillEntry? SelectedSkill => SelectedBinding?.Process.ElementAtOrDefault(skillIndex);
    private string Snapshot() => JsonSerializer.Serialize(document, JsonOptions);

    private Button MakeButton(string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 35, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 8) };
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.Click += (_, _) => action();
        return button;
    }

    public SkillEditorControl()
    {
        Text = "WOWCV · 技能流程編輯器";
        Font = new Font("Microsoft JhengHei UI", 10);
        Size = new Size(1280, 880);

        ForeColor = Color.FromArgb(30, 41, 59);
        BackColor = Color.FromArgb(241, 245, 249);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(20, 14, 0, 0), BackColor = Color.White };
        toolbar.Controls.AddRange([MakeButton("開啟 JSON / TXT", ImportDocument), MakeButton("儲存 JSON / TXT", SaveDocument), MakeButton("新增按鍵組", AddBinding), MakeButton("新增技能", AddSkill)]);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = ClientSize, FixedPanel = FixedPanel.Panel1, SplitterDistance = 390, Panel1MinSize = 360, Panel2MinSize = 400 };
        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), BackColor = Color.White };
        split.Panel1.Controls.Add(editor);
        split.Panel2.Controls.Add(cards);
        Controls.Add(split); Controls.Add(toolbar); Controls.Add(footer);
        void Field(string title, Control input, bool skill = false)
        {
            editor.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 9, 0, 5) });
            input.Width = 320;
            editor.Controls.Add(input);
            if (skill) skillInputs.Add(input);
        }
        editor.Controls.Add(new Label { Text = "流程設定", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true });
        Field("選擇按鍵組", bindingList);
        Field("觸發按鍵 · press", pressInput);
        Field("選擇技能（原始陣列順序）", skillList);
        Field("技能名稱 · skillname", nameInput, true);
        statusInput.Items.AddRange(["Y", "A"]);
        Field("狀態 · status", statusInput, true);
        showPriority = NumberField(editor, "優先序 · priority（小值優先）", false, v => Edit(() => SelectedSkill!.Priority = (int)v));
        showWeight = NumberField(editor, "權重 · skillweight（同優先序，大值優先）", false, v => Edit(() => SelectedSkill!.SkillWeight = (int)v));
        showCd = NumberField(editor, "冷卻秒數 · cd_sec", true, v => Edit(() => SelectedSkill!.CdSec = v));
        Field("冷卻檢查技能 · cooldown_check", checkInput, true);
        var history = new FlowLayoutPanel { Width = 330, Height = 48, Margin = new Padding(0, 16, 0, 0) };
        undoButton = MakeButton("修改前（復原）", () => Restore(undo, redo));
        redoButton = MakeButton("修改後（重做）", () => Restore(redo, undo));
        history.Controls.AddRange([undoButton, redoButton]); editor.Controls.Add(history);
        editor.Controls.Add(new Label { Text = "調整後立即更新預覽。\n點選右側技能卡片可切換編輯。\n拉桿旁可輸入超出拉桿範圍的數值。", AutoSize = true, ForeColor = Color.SlateGray });
        bindingList.SelectedIndexChanged += (_, _) => { if (!loading) { bindingIndex = bindingList.SelectedIndex; skillIndex = 0; RefreshEditor(); } };
        skillList.SelectedIndexChanged += (_, _) => { if (!loading) { skillIndex = skillList.SelectedIndex; RefreshEditor(); } };
        bindingList.DropDown += (_, _) => RefreshSelectorLabels();
        skillList.DropDown += (_, _) => RefreshSelectorLabels();
        pressInput.TextChanged += (_, _) => { if (SelectedBinding is not null) Edit(() => SelectedBinding.Press = pressInput.Text); };
        nameInput.TextChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.SkillName = nameInput.Text); };
        checkInput.TextChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.CooldownCheck = checkInput.Text); };
        statusInput.SelectedIndexChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.Status = statusInput.Text); };
        cards.Resize += (_, _) => ResizeCards();
        saved = Snapshot(); RefreshEditor();

    }

    private Action<decimal> NumberField(FlowLayoutPanel editor, string title, bool fractional, Action<decimal> changed)
    {
        editor.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 9, 0, 4) });
        var panel = new Panel { Width = 325, Height = 40 };
        var slider = new TrackBar { Minimum = 0, Maximum = fractional ? 6000 : 1000, TickStyle = TickStyle.None, Width = 205, SmallChange = 1, LargeChange = 10 };
        var number = new NumericUpDown { Left = 210, Width = 110, DecimalPlaces = fractional ? 2 : 0, Increment = fractional ? 0.01m : 1, Minimum = fractional ? 0 : int.MinValue, Maximum = fractional ? decimal.MaxValue : int.MaxValue };
        bool syncing = false;
        decimal scale = fractional ? 100 : 1;
        slider.ValueChanged += (_, _) => { if (!syncing) number.Value = slider.Value / scale; };
        number.ValueChanged += (_, _) =>
        {
            syncing = true;
            slider.Value = (int)(Math.Clamp(number.Value, slider.Minimum / scale, slider.Maximum / scale) * scale);
            syncing = false;
            changed(number.Value);
        };
        panel.Controls.AddRange([slider, number]); editor.Controls.Add(panel); skillInputs.Add(panel);
        return value => number.Value = value;
    }

    private void Edit(Action change)
    {
        if (loading) return;
        string previous = Snapshot(); change();
        if (previous == Snapshot()) return;
        undo.Push(previous); redo.Clear(); RefreshCards();
    }

    private void RefreshSelectorLabels()
    {
        loading = true;
        for (int i = 0; i < document.Data.Count; i++)
            bindingList.Items[i] = $"按鍵 {document.Data[i].Press} · {document.Data[i].Process.Count} 個技能";
        if (SelectedBinding is not null)
            for (int i = 0; i < SelectedBinding.Process.Count; i++) skillList.Items[i] = SelectedBinding.Process[i].SkillName;
        loading = false;
    }

    private void RefreshEditor()
    {
        loading = true;
        bindingIndex = Math.Clamp(bindingIndex, 0, Math.Max(0, document.Data.Count - 1));
        skillIndex = Math.Clamp(skillIndex, 0, Math.Max(0, (SelectedBinding?.Process.Count ?? 0) - 1));
        bindingList.Items.Clear();
        foreach (var binding in document.Data) bindingList.Items.Add($"按鍵 {binding.Press} · {binding.Process.Count} 個技能");
        bindingList.SelectedIndex = SelectedBinding is null ? -1 : bindingIndex;
        skillList.Items.Clear();
        if (SelectedBinding is not null) foreach (var skill in SelectedBinding.Process) skillList.Items.Add(skill.SkillName);
        skillList.SelectedIndex = SelectedSkill is null ? -1 : skillIndex;
        pressInput.Enabled = SelectedBinding is not null; pressInput.Text = SelectedBinding?.Press ?? "";
        foreach (var control in skillInputs) control.Enabled = SelectedSkill is not null;
        nameInput.Text = SelectedSkill?.SkillName ?? ""; checkInput.Text = SelectedSkill?.CooldownCheck ?? "";
        statusInput.SelectedIndex = SelectedSkill is null ? -1 : statusInput.Items.IndexOf(SelectedSkill.Status);
        showPriority(SelectedSkill?.Priority ?? 0); showWeight(SelectedSkill?.SkillWeight ?? 0); showCd(SelectedSkill?.CdSec ?? 0);
        loading = false; RefreshCards();
    }

    private void RefreshCards()
    {
        cards.SuspendLayout();
        int scroll = -cards.AutoScrollPosition.Y;
        while (cards.Controls.Count > 0) cards.Controls[0].Dispose();
        PreviewLabel("技能流程總覽", 22, Color.FromArgb(30, 41, 59));
        PreviewLabel("優先序由小到大 → 權重由大到小 · 相同時保留原順序", 10, Color.SlateGray);
        if (document.Data.Count == 0) PreviewLabel("開啟現有設定，或新增按鍵組開始編輯。", 12, Color.SlateGray);
        for (int b = 0; b < document.Data.Count; b++)
        {
            var binding = document.Data[b];
            PreviewLabel($"按鍵  [ {binding.Press} ]    ·    {binding.Process.Count} 個技能", 16, Color.FromArgb(79, 70, 229));
            if (binding.Process.Count == 0) PreviewLabel("此按鍵尚未設定技能", 10, Color.SlateGray);
            int rank = 0;
            foreach (var item in binding.Process.Select((skill, index) => (skill, index)).OrderBy(x => x.skill.Priority).ThenByDescending(x => x.skill.SkillWeight))
            {
                int targetBinding = b, targetSkill = item.index;
                var skill = item.skill;
                var card = new Panel { Height = 112, Padding = new Padding(15), Margin = new Padding(0, 0, 0, 12), Cursor = Cursors.Hand, BackColor = b == bindingIndex && item.index == skillIndex ? Color.FromArgb(224, 231, 255) : Color.White };
                var heading = new Label { Text = $"{++rank:00}   {skill.SkillName}     [{skill.Status}]", Dock = DockStyle.Top, Height = 32, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), AutoEllipsis = true };
                var detail = new Label { Text = $"優先序 {skill.Priority}     權重 {skill.SkillWeight}     冷卻 {skill.CdSec:0.##} 秒\n冷卻檢查：{(string.IsNullOrEmpty(skill.CooldownCheck) ? "未設定" : skill.CooldownCheck)}", Dock = DockStyle.Fill, ForeColor = Color.SlateGray, AutoEllipsis = true };
                void SelectCard(object? sender, EventArgs e) { bindingIndex = targetBinding; skillIndex = targetSkill; RefreshEditor(); }
                card.Click += SelectCard; heading.Click += SelectCard; detail.Click += SelectCard;
                card.Controls.Add(detail); card.Controls.Add(heading); cards.Controls.Add(card);
            }
        }
        ResizeCards(); cards.ResumeLayout(); cards.AutoScrollPosition = new Point(0, scroll);
        undoButton.Enabled = undo.Count > 0; redoButton.Enabled = redo.Count > 0;
        footer.Text = Snapshot() == saved ? "已儲存 · 就緒" : "有尚未儲存的修改";
    }

    private void PreviewLabel(string text, float size, Color color) => cards.Controls.Add(new Label { Text = text, Height = size > 14 ? 48 : 34, ForeColor = color, Font = new Font(Font.FontFamily, size, size > 14 ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true });
    private void ResizeCards() { foreach (Control control in cards.Controls) control.Width = Math.Max(200, cards.ClientSize.Width - 66); }
    private void AddBinding() { Edit(() => { document.Data.Add(new SkillBinding()); bindingIndex = document.Data.Count - 1; skillIndex = 0; }); RefreshEditor(); }
    private void AddSkill()
    {
        Edit(() =>
        {
            if (SelectedBinding is null) { document.Data.Add(new SkillBinding()); bindingIndex = document.Data.Count - 1; }
            SelectedBinding!.Process.Add(new SkillEntry()); skillIndex = SelectedBinding.Process.Count - 1;
        });
        RefreshEditor();
    }
    private void Restore(Stack<string> source, Stack<string> destination)
    {
        if (source.Count == 0) return;
        destination.Push(Snapshot()); document = JsonSerializer.Deserialize<SkillDocument>(source.Pop())!; RefreshEditor();
    }
    public bool ConfirmDiscard() => Snapshot() == saved || MessageBox.Show(this, "有尚未儲存的修改，要捨棄嗎？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void ImportDocument()
    {
        using var dialog = new OpenFileDialog { Filter = "技能設定 (*.json;*.txt)|*.json;*.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = SkillDocumentService.Parse(File.ReadAllText(dialog.FileName));
            if (!ConfirmDiscard()) return;
            document = imported; undo.Clear(); redo.Clear(); bindingIndex = skillIndex = 0; saved = Snapshot(); RefreshEditor();
            footer.Text = $"已匯入：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "匯入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void SaveDocument()
    {
        try { SkillDocumentService.Parse(Snapshot()); }
        catch (JsonException ex) { MessageBox.Show(this, ex.Message, "請先完成設定"); return; }
        using var dialog = new SaveFileDialog { Filter = "JSON 設定 (*.json)|*.json|文字設定 (*.txt)|*.txt", FileName = "skills.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, Snapshot(), new System.Text.UTF8Encoding(false)); saved = Snapshot(); RefreshCards(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "儲存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}