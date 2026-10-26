using System.Reflection;
using System.Text.Json;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillValueLimitTests
{
    /// <summary>
    /// 驗證數值邊界、舊冷卻轉換及編輯器與啟動還原的一致性。
    /// </summary>
    public static void Run(string root, Action<bool, string> check)
    {
        foreach (var (priority, weight, cooldown) in new[]
        {
            (1, 1, 0m),
            (10, 100, 600m)
        }
        )
        {
            var parsed = SkillDocumentService.Parse(Json(priority, weight, cooldown),
                out var adjustments).Data[0].Process[0];
            check(
                parsed.Priority == priority && parsed.SkillWeight == weight
                && parsed.CdSec == cooldown && adjustments.Count == 0,
                $"Skill numeric limits accept exact boundary {priority}/{weight}/{cooldown}"
            );
        }

        foreach (var (priority, weight, cooldown, field) in new[]
        {
            (0, 1, 0m, "priority"),
            (11, 1, 0m, "priority"),
            (1, 0, 0m, "skillweight"),
            (1, 101, 0m, "skillweight"),
            (1, 1, -0.4m, "cd_sec"),
            (1, 1, 600.1m, "cd_sec")
        }
        )
        {
            try
            {
                SkillDocumentService.Parse(Json(priority, weight, cooldown));
                check(false, "Out-of-range values must fail");
            }
            catch (JsonException ex)
            {
                check(
                    ex.Message.Contains("數值技能") && ex.Message.Contains(field)
                    && ex.Message.Contains("～"),
                    $"Invalid {field} identifies the skill and allowed range"
                );
            }
        }

        foreach (var (input, expected) in new[]
        {
            (15.45m, 15m),
            (15.5m, 16m),
            (16.5m, 17m),
            (0.5m, 1m),
            (599.5m, 600m)
        }
        )
        {
            var parsed = SkillDocumentService.Parse(Json(1, 100, input), out var adjustments);
            check(
                parsed.Data[0].Process[0].CdSec == expected && adjustments.Count == 1
                && adjustments[0].Contains("數值技能"),
                ($"Legacy cooldown {input} rounds half-up to {expected}"
                + $" with a named adjustment notice")
            );
        }

        var runtime = new RuntimeConfiguration();
        runtime.Import(Json(1, 100, 15.5m), out var notices);
        long revision = runtime.Bindings.Revision;
        check(
            runtime.Read()!.Data[0].Process[0].CdSec == 16 && notices.Count == 1,
            "Runtime import normalizes cooldowns and exposes adjustments"
        );
        try
        {
            runtime.Import(Json(11, 100, 10));
            check(false, "Invalid runtime import must fail");
        }
        catch (JsonException)
        {
            check(
                runtime.Bindings.Revision == revision
                && runtime.Read()!.Data[0].Process[0].CdSec == 16,
                "Rejected range changes preserve the previous runtime document and revision"
            );
        }

        string directory = Path.Combine(root, "skill-values");
        Directory.CreateDirectory(directory);
        TestEditor(directory, check);
        TestStartup(directory, check);
    }

    /// <summary>
    /// 建立含指定數值的技能 JSON，供邊界與舊資料轉換驗證。
    /// </summary>
    private static string Json(int priority, int weight,
        decimal cooldown) => SkillDocumentService.Serialize(new() { Data = [
        new() { Press = "E", Process = [
            new() { SkillName = "數值技能", Priority = priority, SkillWeight = weight,
        CdSec = cooldown, Extra = new() { ["custom"] = JsonSerializer.SerializeToElement(1.25m) } }
        ] }
    ] });

    /// <summary>
    /// 驗證表格整數範圍、舊冷卻轉換、無效輸入與儲存復原。
    /// </summary>
    private static void TestEditor(string directory, Action<bool, string> check)
    {
        string path = Path.Combine(directory, "legacy.json"), original = Json(1, 100, 15.5m);
        File.WriteAllText(path, original);
        using var editor = new SkillEditorControl((_, _) => true)
        {
            Dock = DockStyle.Fill
        };
        using var host = new Form
        {
            ClientSize = new(1100, 740),
            ShowInTaskbar = false,
            Opacity = 0,
            StartPosition = FormStartPosition.Manual,
            Location = new(-30000, -30000)
        };
        host.Controls.Add(editor);
        host.Show();
        var parsed = SkillDocumentService.Parse(File.ReadAllText(path), out var adjustments);
        Invoke(editor, "ApplyImportedDocument", parsed, adjustments, "legacy.json");
        var priority = Find<NumericUpDown>(editor).Single(input => input.Name == "BatchPriority");
        var weight = Find<NumericUpDown>(editor).Single(input => input.Name == "BatchWeight");
        var cooldown = Find<NumericUpDown>(editor).Single(input => input.Name == "BatchCooldown");
        check(
            priority.Minimum == 1 && priority.Maximum == 10 && weight.Minimum == 1
            && weight.Maximum == 100 && cooldown.Minimum == 0 && cooldown.Maximum == 600
            && new[] { priority, weight, cooldown }.All(input => input.DecimalPlaces == 0
            && input.Increment == 1),
            "Batch numeric inputs enforce integer ranges and unit steps"
        );
        check(
            Field<Label>(editor, "footer").Text.Contains("四捨五入") && Field<string>(editor,
            "saved") != Snapshot(editor) && File.ReadAllText(path) == original,
            "Rounded imports are unsaved and leave the source file untouched"
        );
        check(
            SkillEditorUi.Edit(editor, "數值技能", "Priority", "10") && SkillEditorUi.Edit(editor,
            "數值技能", "Weight", "1") && SkillEditorUi.Edit(editor, "數值技能", "Cooldown", "600"),
            "Grid commits valid numeric boundaries directly"
        );
        foreach (var (column, value) in new[]
        {
            ("Priority", "11"),
            ("Weight", "0"),
            ("Cooldown", "601"),
            ("Cooldown", "15.5"),
            ("Cooldown", "abc")
        }
        )
        {
            check(
                !SkillEditorUi.Edit(editor, "數值技能", column, value)
                && Read(editor).Data[0].Process[0].CdSec == 600,
                $"Grid rejects invalid {column} input {value} before changing the document"
            );
            SkillEditorUi.Grid(editor).CancelEdit();
            Application.DoEvents();
        }

        SkillEditorUi.Edit(editor, "數值技能", "Cooldown", "16");
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check(
            Read(editor).Data[0].Process[0].CdSec == 600,
            "Undo restores the previous integer cell value"
        );
        Find<Button>(editor).Single(button => button.Text == "修改後（重做）").PerformClick();
        check(
            Read(editor).Data[0].Process[0].CdSec == 16,
            "Redo restores the edited integer cell value"
        );
        Invoke(editor, "SaveToFile", path);
        using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var entry = saved.RootElement.GetProperty("data")[0].GetProperty("process")[0];
            check(
                entry.GetProperty("cd_sec").GetRawText() == "16"
                && entry.GetProperty("custom").GetDecimal() == 1.25m && Field<string>(editor,
                "saved") == Snapshot(editor),
                ("Explicit save writes integer cooldowns, preserves custom values and "
                + "clears unsaved state")
            );
        }

        using (var image = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(directory, "integer-fields.png"));
        }

        Find<Button>(editor).Single(button => button.Name == "DeleteSkill").PerformClick();
        Find<Button>(editor).Single(button => button.Name == "DeleteSkillBinding").PerformClick();
        check(
            Read(editor).Data.Count == 0 && SkillEditorUi.Grid(editor).Rows.Count == 0,
            "Deleting the final group leaves an empty grid"
        );
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check(
            Read(editor).Data[0].Process[0].CdSec == 16,
            "Undo restores numeric values after an empty document"
        );
        host.Close();
    }

    /// <summary>
    /// 驗證啟動還原會正規化舊流程數值並提示，且不覆寫原檔。
    /// </summary>
    private static void TestStartup(string directory, Action<bool, string> check)
    {
        string flow = Path.Combine(directory, "startup-flow.json"),
            image = Path.Combine(directory, "source.png"), config = Path.Combine(directory,
            "config.json");
        string original = Json(1, 100, 15.5m);
        File.WriteAllText(flow, original);
        using (var source = new Bitmap(40, 40))
            source.Save(image);
        using (var store = new WorkbenchConfigurationStore(config))
        {
            store.QueueSave(new()
            {
                Source = new()
                {
                    Kind = SourceKind.Image,
                    ImagePath = image
                },
                SkillFlowPath = flow
            });
            store.Flush();
        }

        using var host = new Form1(config)
        {
            ShowInTaskbar = false,
            Opacity = 0,
            StartPosition = FormStartPosition.Manual,
            Location = new(-30000, -30000)
        };
        host.Show();
        var detection = Find<DetectionControl>(host).Single();
        check(
            detection.Configuration.Read()!.Data[0].Process[0].CdSec == 16
            && detection.ConfigurationNotice.Contains("四捨五入"),
            "Startup normalizes legacy flow cooldowns and displays the conversion notice"
        );
        host.Close();
        check(
            File.ReadAllText(flow) == original,
            "Startup restoration and shutdown never rewrite the original skill flow"
        );
    }

    /// <summary>
    /// 讀取並解析隔離測試使用的資料或編輯器快照。
    /// </summary>
    private static SkillDocument Read(SkillEditorControl editor) => SkillDocumentService.Parse(
        Snapshot(editor));

    /// <summary>
    /// 取得技能編輯文件的 JSON 快照，供數值與未儲存狀態驗證。
    /// </summary>
    private static string Snapshot(SkillEditorControl editor) => (string)Invoke(editor,
        "Snapshot")!;

    /// <summary>
    /// 透過反射呼叫待驗證的非公開方法。
    /// </summary>
    private static object? Invoke(SkillEditorControl editor, string name,
        params object[] args) => typeof(SkillEditorControl).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);

    /// <summary>
    /// 透過反射取得驗證所需的物件欄位。
    /// </summary>
    private static T Field<T>(SkillEditorControl editor,
        string name) => (T)typeof(SkillEditorControl).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;

    /// <summary>
    /// 遞迴找出指定型別的子控制項，供介面驗證定位。
    /// </summary>
    private static IEnumerable<T> Find<T>(Control parent)
        where T : Control => parent.Controls.Cast<Control>().SelectMany(child => (child is T match
        ? new[] { match } : []).Concat(Find<T>(child)));
}
