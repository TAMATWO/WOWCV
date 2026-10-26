using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using WOWCV.Models;
using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class SkillEditorControl : UserControl
{
    private SkillDocument document = new();
    private readonly Stack<string> undo = new(), redo = new();
    private string saved = "", importNotice = "";
    private bool loading, refreshQueued;
    private int bindingIndex, skillIndex;
    private string? templateFolder;
    public string? TemplateFolderPath => templateFolder;
    private readonly Func<string, int, bool> confirmBindingDeletion;
    private readonly HashSet<SkillEntry> marked = [];
    private readonly ComboBox bindingList = new() { Name = "SkillBindingSelector", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TextBox pressInput = new() { Name = "PressInput", Width = 110 };
    private readonly TextBox search = new() { Name = "SkillSearch", Width = 220, PlaceholderText = "搜尋名稱、施放鍵或樣版" };
    private readonly ComboBox order = new() { Name = "SkillOrder", DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly DataGridView grid = new BufferedResultGrid { Name = "SkillOverview", Dock = DockStyle.Fill,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2 };
    private readonly Label footer = new() { Name = "SkillEditorStatus", Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 4, 12, 4), AutoEllipsis = true };
    private readonly Label selectionCount = new() { AutoSize = true, Margin = new Padding(6, 10, 12, 0) };
    private readonly FlowLayoutPanel batch = new() { Name = "SkillBatchToolbar", AutoSize = true, Dock = DockStyle.Top, Visible = false, Padding = new Padding(10, 4, 10, 0), BackColor = Color.FromArgb(224, 231, 255) };
    private readonly CheckBox batchPriority = new() { Name = "ApplyPriority", Text = "優先序", AutoSize = true };
    private readonly CheckBox batchWeight = new() { Name = "ApplyWeight", Text = "權重", AutoSize = true };
    private readonly CheckBox batchCooldown = new() { Name = "ApplyCooldown", Text = "冷卻秒數", AutoSize = true };
    private readonly NumericUpDown priorityValue = BatchNumber("BatchPriority", SkillValueLimits.MinimumPriority, SkillValueLimits.MaximumPriority, 1);
    private readonly NumericUpDown weightValue = BatchNumber("BatchWeight", SkillValueLimits.MinimumWeight, SkillValueLimits.MaximumWeight, 100);
    private readonly NumericUpDown cooldownValue = BatchNumber("BatchCooldown", SkillValueLimits.MinimumCooldown, SkillValueLimits.MaximumCooldown, 0);
    private readonly Label templateStatus = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(12, 2, 12, 0), AutoEllipsis = true };
    private readonly ToolTip tips = new();
    private Button undoButton = null!, redoButton = null!, deleteSkillButton = null!, deleteBindingButton = null!, applyBatchButton = null!;
    private SkillBinding? SelectedBinding => document.Data.ElementAtOrDefault(bindingIndex);
    private SkillEntry? SelectedSkill => SelectedBinding?.Process.ElementAtOrDefault(skillIndex);
    private string Snapshot() => SkillDocumentService.Serialize(document);

    public SkillEditorControl() : this(null) { }
    public SkillEditorControl(Func<string, int, bool>? confirmBindingDeletion)
    {
        this.confirmBindingDeletion = confirmBindingDeletion ?? ((press, count) => MessageBox.Show(this,
            $"確定刪除按鍵組「{press}」及其內的 {count} 個技能？\n刪除後可使用復原還原。", "刪除按鍵組",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        Text = "WOWCV · 技能流程編輯器";
        Font = new Font("Microsoft JhengHei UI", 10);
        Size = new Size(1280, 880);
        ForeColor = Color.FromArgb(30, 41, 59);
        BackColor = Color.FromArgb(241, 245, 249);
        DoubleBuffered = true;
        InitializeEditorLayout();
        InitializeGrid();
        saved = Snapshot(); RefreshEditor();
    }

    private void Edit(Action change)
    {
        if (loading) return;
        string previous = Snapshot(); change();
        if (previous == Snapshot()) return;
        undo.Push(previous); redo.Clear(); importNotice = "";
        UpdateStatus();
    }

    private bool CommitPendingEdits()
    {
        if (!grid.EndEdit()) return false;
        if (SelectedBinding is { } binding && binding.Press != pressInput.Text)
            Edit(() => binding.Press = pressInput.Text);
        return true;
    }

    private void RefreshEditor()
    {
        loading = true;
        try
        {
            bindingIndex = Math.Clamp(bindingIndex, 0, Math.Max(0, document.Data.Count - 1));
            skillIndex = Math.Clamp(skillIndex, 0, Math.Max(0, (SelectedBinding?.Process.Count ?? 0) - 1));
            bindingList.Items.Clear();
            foreach (var binding in document.Data) bindingList.Items.Add($"按鍵 {binding.Press} · {binding.Process.Count} 個技能");
            bindingList.SelectedIndex = SelectedBinding is null ? -1 : bindingIndex;
            bindingList.Enabled = SelectedBinding is not null;
            pressInput.Enabled = SelectedBinding is not null; pressInput.Text = SelectedBinding?.Press ?? "";
        }
        finally { loading = false; }
        RefreshRows(SelectedSkill);
        RefreshTemplateSelection();
    }

    public void SetTemplateFolder(string? folder)
    {
        templateFolder = folder; RefreshTemplateSelection();
    }

    private void RefreshTemplateSelection()
    {
        bool available = templateFolder is not null && Directory.Exists(templateFolder);
        templateStatus.Text = available
            ? $"樣版來源：{templateFolder} · 列內選圖／清除；儲存後請於主頁重新匯入。"
            : "請先於主頁載入樣版資料夾，再使用列內「選圖」綁定。既有綁定會保留。";
        tips.SetToolTip(templateStatus, templateStatus.Text);
        if (grid.Columns.Contains("PickTemplate")) grid.Columns["PickTemplate"]!.ReadOnly = !available;
    }

    private void SelectTemplateImage()
    {
        if (SelectedSkill is null || templateFolder is null || !Directory.Exists(templateFolder)) return;
        using var dialog = new OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
            InitialDirectory = templateFolder, FileName = SelectedSkill.TemplateFile, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { BindTemplateImage(dialog.FileName); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
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
        Edit(() => SelectedSkill.TemplateFile = Path.GetFileName(fullPath)); RefreshRows(SelectedSkill);
    }

    private void AddBinding()
    {
        marked.Clear(); search.Clear();
        Edit(() => { document.Data.Add(new SkillBinding()); bindingIndex = document.Data.Count - 1; skillIndex = 0; }); RefreshEditor();
    }
    private void AddSkill()
    {
        marked.Clear(); search.Clear();
        Edit(() =>
        {
            if (SelectedBinding is null) { document.Data.Add(new SkillBinding()); bindingIndex = document.Data.Count - 1; }
            SelectedBinding!.Process.Add(new SkillEntry()); skillIndex = SelectedBinding.Process.Count - 1;
        });
        RefreshEditor();
        if (grid.CurrentRow is { } row) { grid.CurrentCell = row.Cells["SkillName"]; grid.Focus(); grid.BeginEdit(true); }
    }
    private void DeleteSkill()
    {
        if (SelectedBinding is not { } binding) return;
        var targets = marked.Count > 0 ? marked.ToArray() : SelectedSkill is { } skill ? new[] { skill } : [];
        if (targets.Length == 0) return;
        int next = binding.Process.IndexOf(targets.OrderBy(skill => binding.Process.IndexOf(skill)).First());
        Edit(() => binding.Process.RemoveAll(skill => targets.Contains(skill)));
        marked.Clear(); skillIndex = Math.Clamp(next, 0, Math.Max(0, binding.Process.Count - 1)); RefreshEditor();
    }
    private void DeleteBinding()
    {
        if (SelectedBinding is not { } binding || !confirmBindingDeletion(binding.Press, binding.Process.Count)) return;
        Edit(() => { document.Data.RemoveAt(bindingIndex); skillIndex = 0; });
        marked.Clear(); search.Clear(); RefreshEditor();
    }
    private void Restore(Stack<string> source, Stack<string> destination)
    {
        if (source.Count == 0) return;
        destination.Push(Snapshot()); document = JsonSerializer.Deserialize<SkillDocument>(source.Pop())!;
        marked.Clear(); RefreshEditor();
    }

    private void ApplyBatch()
    {
        if (SelectedBinding is null) return;
        if (!ValidateChildren(ValidationConstraints.Enabled)) return;
        var targets = grid.Rows.Cast<DataGridViewRow>().Where(row => row.Tag is SkillEntry skill && marked.Contains(skill))
            .Select(row => (SkillEntry)row.Tag!).ToArray();
        if (targets.Length == 0 || !(batchPriority.Checked || batchWeight.Checked || batchCooldown.Checked)) return;
        Edit(() =>
        {
            foreach (var skill in targets)
            {
                if (batchPriority.Checked) skill.Priority = (int)priorityValue.Value;
                if (batchWeight.Checked) skill.SkillWeight = (int)weightValue.Value;
                if (batchCooldown.Checked) skill.CdSec = cooldownValue.Value;
            }
        });
        RefreshRows(SelectedSkill);
    }

    private void UpdateStatus()
    {
        undoButton.Enabled = undo.Count > 0; redoButton.Enabled = redo.Count > 0;
        deleteSkillButton.Enabled = grid.CurrentRow is not null || marked.Count > 0;
        deleteBindingButton.Enabled = SelectedBinding is not null;
        deleteSkillButton.Text = marked.Count > 0 ? $"刪除技能（{marked.Count}）" : "刪除技能";
        selectionCount.Text = $"本組 {SelectedBinding?.Process.Count ?? 0} 項 · 顯示 {grid.Rows.Count} 項 · 已選 {marked.Count} 項";
        batch.Visible = marked.Count > 0;
        applyBatchButton.Enabled = marked.Count > 0 && (batchPriority.Checked || batchWeight.Checked || batchCooldown.Checked);
        footer.Text = (Snapshot() == saved ? "已儲存 · 就緒" : "有尚未儲存的修改") + (importNotice.Length > 0 ? $" · {importNotice}" : "")
            + "\n決策順位依優先序 ↑、權重 ↓、原始順序；不是固定施放次序。";
        footer.ForeColor = ForeColor;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
    public bool ConfirmDiscard() => CommitPendingEdits() && (Snapshot() == saved || MessageBox.Show(this, "有尚未儲存的修改，要捨棄嗎？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);

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
        loading = true; grid.CancelEdit(); grid.EndEdit(); search.Clear(); loading = false;
        document = imported; undo.Clear(); redo.Clear(); marked.Clear(); bindingIndex = skillIndex = 0;
        saved = adjustments.Count == 0 ? Snapshot() : "";
        importNotice = $"已匯入：{fileName}" + (adjustments.Count > 0 ? $" · {adjustments.Count} 個 cd_sec 已四捨五入；原檔未修改" : "");
        RefreshEditor();
    }

    private void SaveToFile(string path)
    {
        if (!CommitPendingEdits()) return;
        var validated = SkillDocumentService.Parse(Snapshot());
        File.WriteAllText(path, SkillDocumentService.Serialize(validated), new System.Text.UTF8Encoding(false));
        document = validated; marked.Clear(); importNotice = ""; saved = Snapshot(); RefreshEditor();
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
