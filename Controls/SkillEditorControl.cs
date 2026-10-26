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
    private readonly ComboBox bindingList = new() { Name = "SkillBindingSelector", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox skillList = new() { Name = "SkillEntrySelector", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox pressInput = new(), nameInput = new(), checkInput = new();
    private readonly TextBox castKeyInput = new() { Name = "CastKeyInput" };
    private readonly TextBox templateInput = new() { Name = "TemplateFileInput", ReadOnly = true, Width = 180 };
    private readonly Button selectTemplateButton = new() { Name = "SelectTemplateImage", Text = "選擇圖片…", Left = 185, Width = 90, Height = 28 };
    private readonly Button clearTemplateButton = new() { Name = "ClearTemplateImage", Text = "清除", Left = 280, Width = 40, Height = 28 };
    private readonly Label templateStatus = new() { AutoSize = true, MaximumSize = new Size(320, 0) };
    private string? templateFolder;
    public string? TemplateFolderPath => templateFolder;
    private readonly ComboBox statusInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
    private readonly Label footer = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(20, 5, 0, 0) };
    private Button undoButton = null!, redoButton = null!;
    private readonly Button deleteSkillButton, deleteBindingButton;
    private readonly Func<string, int, bool> confirmBindingDeletion;
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

    public SkillEditorControl() : this(null) { }

    public SkillEditorControl(Func<string, int, bool>? confirmBindingDeletion)
    {
        this.confirmBindingDeletion = confirmBindingDeletion ?? ((press, count) => MessageBox.Show(this,
            $"確定刪除按鍵組「{press}」及其內的 {count} 個技能？\n刪除後可使用「修改前（復原）」還原。",
            "刪除按鍵組", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        Text = "WOWCV · 技能流程編輯器";
        Font = new Font("Microsoft JhengHei UI", 10);
        Size = new Size(1280, 880);

        ForeColor = Color.FromArgb(30, 41, 59);
        BackColor = Color.FromArgb(241, 245, 249);
        var toolbar = new FlowLayoutPanel { Name = "SkillEditorToolbar", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 14, 0, 6), BackColor = Color.White };
        deleteSkillButton = MakeButton("刪除技能", DeleteSkill); deleteSkillButton.Name = "DeleteSkill";
        deleteBindingButton = MakeButton("刪除按鍵組", DeleteBinding); deleteBindingButton.Name = "DeleteSkillBinding";
        toolbar.Controls.AddRange([MakeButton("開啟 JSON / TXT", ImportDocument), MakeButton("儲存 JSON / TXT", SaveDocument),
            MakeButton("新增按鍵組", AddBinding), MakeButton("新增技能", AddSkill), deleteSkillButton, deleteBindingButton]);
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
        Field("狀態 · status（保留，本階段不解讀）", statusInput, true);
        showPriority = NumberField(editor, "優先序 · priority（1～10，小值優先）", "Priority", SkillValueLimits.MinimumPriority, SkillValueLimits.MaximumPriority, v => Edit(() => SelectedSkill!.Priority = (int)v));
        showWeight = NumberField(editor, "權重 · skillweight（1～100，大值優先）", "Weight", SkillValueLimits.MinimumWeight, SkillValueLimits.MaximumWeight, v => Edit(() => SelectedSkill!.SkillWeight = (int)v));
        showCd = NumberField(editor, "冷卻秒數 · cd_sec（整數 0～600）", "Cooldown", SkillValueLimits.MinimumCooldown, SkillValueLimits.MaximumCooldown, v => Edit(() => SelectedSkill!.CdSec = v));
        Field("冷卻檢查技能 · cooldown_check", checkInput, true);
        var history = new FlowLayoutPanel { Width = 330, Height = 48, Margin = new Padding(0, 16, 0, 0) };
        undoButton = MakeButton("修改前（復原）", () => Restore(undo, redo));
        redoButton = MakeButton("修改後（重做）", () => Restore(redo, undo));
        history.Controls.AddRange([undoButton, redoButton]); editor.Controls.Add(history);
        editor.Controls.Add(new Label { Text = "調整後立即更新預覽。\n點選右側技能卡片可切換編輯。\n數字輸入與拉桿使用相同範圍。", AutoSize = true, ForeColor = Color.SlateGray });
        var templatePicker = new Panel { Name = "TemplateImagePicker", Height = 32 };
        templatePicker.Controls.AddRange([templateInput, selectTemplateButton, clearTemplateButton]);
        Field("樣版圖片 · template_file", templatePicker, true);
        Field("施放按鍵 · cast_key（目前只記錄，不送鍵）", castKeyInput, true);
        editor.Controls.Add(templateStatus);
        bindingList.SelectedIndexChanged += (_, _) => { if (!loading) { bindingIndex = bindingList.SelectedIndex; skillIndex = 0; RefreshEditor(); } };
        skillList.SelectedIndexChanged += (_, _) => { if (!loading) { skillIndex = skillList.SelectedIndex; RefreshEditor(); } };
        bindingList.DropDown += (_, _) => RefreshSelectorLabels();
        skillList.DropDown += (_, _) => RefreshSelectorLabels();
        pressInput.TextChanged += (_, _) => { if (SelectedBinding is not null) Edit(() => SelectedBinding.Press = pressInput.Text); };
        nameInput.TextChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.SkillName = nameInput.Text); };
        checkInput.TextChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.CooldownCheck = checkInput.Text); };
        statusInput.SelectedIndexChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.Status = statusInput.Text); };
        selectTemplateButton.Click += (_, _) => SelectTemplateImage();
        clearTemplateButton.Click += (_, _) =>
        {
            if (SelectedSkill is null) return;
            Edit(() => SelectedSkill.TemplateFile = ""); RefreshTemplateSelection();
        };
        castKeyInput.TextChanged += (_, _) => { if (SelectedSkill is not null) Edit(() => SelectedSkill.CastKey = castKeyInput.Text); };
        cards.Resize += (_, _) => ResizeCards();
        saved = Snapshot(); RefreshEditor();

    }

    private Action<decimal> NumberField(FlowLayoutPanel editor, string title, string key, int minimum, int maximum, Action<decimal> changed)
    {
        editor.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 9, 0, 4) });
        var panel = new Panel { Width = 325, Height = 40 };
        var slider = new TrackBar { Name = key + "Slider", Minimum = minimum, Maximum = maximum, TickStyle = TickStyle.None, Width = 205, SmallChange = 1, LargeChange = 10 };
        var number = new NumericUpDown { Name = key + "Input", Left = 210, Width = 110, DecimalPlaces = 0, Increment = 1, Minimum = minimum, Maximum = maximum };
        bool syncing = false;
        slider.ValueChanged += (_, _) => { if (!syncing) number.Value = slider.Value; };
        number.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            decimal value = decimal.Round(number.Value, 0, MidpointRounding.AwayFromZero);
            syncing = true;
            try { number.Value = value; slider.Value = (int)value; }
            finally { syncing = false; }
            changed(value);
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
        bindingList.Enabled = document.Data.Count > 0;
        skillList.Items.Clear();
        if (SelectedBinding is not null) foreach (var skill in SelectedBinding.Process) skillList.Items.Add(skill.SkillName);
        skillList.SelectedIndex = SelectedSkill is null ? -1 : skillIndex;
        skillList.Enabled = (SelectedBinding?.Process.Count ?? 0) > 0;
        deleteSkillButton.Enabled = SelectedSkill is not null;
        deleteBindingButton.Enabled = SelectedBinding is not null;
        pressInput.Enabled = SelectedBinding is not null; pressInput.Text = SelectedBinding?.Press ?? "";
        foreach (var control in skillInputs) control.Enabled = SelectedSkill is not null;
        nameInput.Text = SelectedSkill?.SkillName ?? ""; checkInput.Text = SelectedSkill?.CooldownCheck ?? "";
        statusInput.SelectedIndex = SelectedSkill is null ? -1 : statusInput.Items.IndexOf(SelectedSkill.Status);
        castKeyInput.Text = SelectedSkill?.CastKey ?? "";
        RefreshTemplateSelection();
        showPriority(SelectedSkill?.Priority ?? SkillValueLimits.MinimumPriority);
        showWeight(SelectedSkill?.SkillWeight ?? SkillValueLimits.MaximumWeight);
        showCd(SelectedSkill?.CdSec ?? SkillValueLimits.MinimumCooldown);
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
                var card = new Panel { Height = 145, Padding = new Padding(15), Margin = new Padding(0, 0, 0, 12), Cursor = Cursors.Hand, BackColor = b == bindingIndex && item.index == skillIndex ? Color.FromArgb(224, 231, 255) : Color.White };
                var heading = new Label { Text = $"{++rank:00}   {skill.SkillName}     [{skill.Status}]", Dock = DockStyle.Top, Height = 32, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), AutoEllipsis = true };
                var detail = new Label { Text = $"優先序 {skill.Priority}     權重 {skill.SkillWeight}     冷卻 {skill.CdSec:0.##} 秒\n冷卻檢查：{(string.IsNullOrEmpty(skill.CooldownCheck) ? "未設定" : skill.CooldownCheck)}\n樣版：{(skill.TemplateFile.Length == 0 ? "未綁定" : skill.TemplateFile)}    施放鍵：{(skill.CastKey.Length == 0 ? "未設定" : skill.CastKey)}", Dock = DockStyle.Fill, ForeColor = Color.SlateGray, AutoEllipsis = true };
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
    public void SetTemplateFolder(string? folder)
    {
        templateFolder = folder;
        bool wasLoading = loading; loading = true;
        try { RefreshTemplateSelection(); }
        finally { loading = wasLoading; }
    }

    private void RefreshTemplateSelection()
    {
        string selected = SelectedSkill?.TemplateFile ?? "";
        templateInput.Text = selected;
        bool folderExists = templateFolder is not null && Directory.Exists(templateFolder);
        selectTemplateButton.Enabled = SelectedSkill is not null && folderExists;
        clearTemplateButton.Enabled = SelectedSkill is not null && selected.Length > 0;
        templateStatus.Text = folderExists
            ? $"來源：{templateFolder}\n按「選擇圖片」開啟檔案視窗；綁定後請儲存，再於主頁重新匯入流程。"
            : "請先在主頁載入樣版資料夾，再選擇對應圖片。";
        if (selected.Length > 0 && (!folderExists || !File.Exists(Path.Combine(templateFolder!, selected))))
            templateStatus.Text += "\n目前綁定圖片不在資料夾內，原設定已保留。";
    }

    private void SelectTemplateImage()
    {
        if (SelectedSkill is null || templateFolder is null || !Directory.Exists(templateFolder)) return;
        using var dialog = new OpenFileDialog
        {
            Title = "選擇樣版圖片（目前主頁樣版資料夾）",
            Filter = "樣版圖片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
            InitialDirectory = templateFolder, FileName = SelectedSkill.TemplateFile,
            CheckFileExists = true, RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { BindTemplateImage(dialog.FileName); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, "無法綁定樣版圖片", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void BindTemplateImage(string path)
    {
        if (SelectedSkill is null || templateFolder is null) throw new InvalidOperationException("請先選擇技能並載入主頁樣版資料夾。");
        string fullPath = Path.GetFullPath(path);
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(templateFolder));
        if (!string.Equals(Path.GetDirectoryName(fullPath), folder, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("請選擇目前主頁樣版資料夾內的圖片。若圖片在其他位置，請先於主頁載入該資料夾。");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到選取的圖片。", fullPath);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(fullPath).ToLowerInvariant()))
            throw new ArgumentException("請選擇支援的樣版圖片格式。");
        Edit(() => SelectedSkill.TemplateFile = Path.GetFileName(fullPath));
        RefreshTemplateSelection();
    }
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
    private void DeleteSkill()
    {
        if (SelectedBinding is not { } binding || SelectedSkill is null) return;
        Edit(() => binding.Process.RemoveAt(skillIndex));
        RefreshEditor();
    }

    private void DeleteBinding()
    {
        if (SelectedBinding is not { } binding || !confirmBindingDeletion(binding.Press, binding.Process.Count)) return;
        Edit(() => { document.Data.RemoveAt(bindingIndex); skillIndex = 0; });
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
            var imported = SkillDocumentService.Parse(File.ReadAllText(dialog.FileName), out var adjustments);
            if (!ConfirmDiscard()) return;
            ApplyImportedDocument(imported, adjustments, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "匯入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void ApplyImportedDocument(SkillDocument imported, IReadOnlyList<string> adjustments, string fileName)
    {
        document = imported; undo.Clear(); redo.Clear(); bindingIndex = skillIndex = 0;
        saved = adjustments.Count == 0 ? Snapshot() : "";
        RefreshEditor();
        footer.Text = $"已匯入：{fileName}" + (adjustments.Count > 0
            ? $" · {adjustments.Count} 個 cd_sec 已四捨五入；有尚未儲存的修改，請儲存後更新原檔。" : "");
    }

    private void SaveToFile(string path)
    {
        var validated = SkillDocumentService.Parse(Snapshot());
        File.WriteAllText(path, SkillDocumentService.Serialize(validated), new System.Text.UTF8Encoding(false));
        document = validated; saved = Snapshot(); RefreshEditor();
    }

    private void SaveDocument()
    {
        try { SkillDocumentService.Parse(Snapshot()); }
        catch (JsonException ex) { MessageBox.Show(this, ex.Message, "請先完成設定"); return; }
        using var dialog = new SaveFileDialog { Filter = "JSON 設定 (*.json)|*.json|文字設定 (*.txt)|*.txt", FileName = "skills.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SaveToFile(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "儲存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
