using System.Diagnostics;
using System.Text.Json;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class WorkbenchConfigurationTests
{
    /// <summary>
    /// 驗證配置讀寫、延後保存、重新啟動及失效參照還原。
    /// </summary>
    public static void Run(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "configuration-tests");
        Directory.CreateDirectory(directory);
        var sample = new WorkbenchConfiguration
        {
            Source = new()
            {
                Kind = SourceKind.Image,
                ScreenDeviceName = Screen.AllScreens.FirstOrDefault()?.DeviceName,
                ImagePath = "../../pixel-ui.png",
                TemplateFolderPath = "../../pixel-templates"
            },
            Search = new()
            {
                Threshold = 0.9,
                ProcessScale = 0.5,
                UseRoi = true,
                Roi = new(35, 25, 60, 60, 140, 120)
            },
            Verification = new()
            {
                RgbTolerance = 2,
                MinimumMatches = 4,
                Up = new(-1, -10)
            },
            SkillFlowPath = "flow.json"
        };
        check(
            WorkbenchConfigurationStore.Parse(WorkbenchConfigurationStore.Serialize(
            sample)) == sample,
            "Unified configuration round-trips all four sections"
        );
        Reject(
            "{\"version\":\"wrong\"}",
            "String format version is handled as a configuration error"
        );
        Reject("{\"version\":2}", "Unknown unified configuration version is rejected");
        Reject(
            "{\"version\":1,\"search\":{\"processScale\":0}}",
            "Invalid search settings cannot reach controls"
        );
        Reject(
            "{\"version\":1,\"verification\":{\"minimumMatches\":9}}",
            "Unified config validates five-point settings"
        );
        Reject("{\"version\":1,\"source\":null}", "Null configuration sections are rejected");
        Reject(
            "{\"data\":[]}",
            "Skill flow documents are not treated as application configuration"
        );
        string path = Path.Combine(directory, "store", "config.json");
        using (var store = new WorkbenchConfigurationStore(path, delayMs: 10000))
        {
            check(
                store.Load(out var warning) == new WorkbenchConfiguration() && warning is null
                && !File.Exists(path),
                "First launch uses model defaults without writing a config file"
            );
            check(
                store.ResolveReference("example.png") == Path.Combine(Path.GetDirectoryName(
                path)!, "example.png"),
                "Relative references resolve against the configuration directory"
            );
            store.QueueSave(sample);
            check(store.Flush(), "First explicit configuration edit writes atomically");
            check(
                Read(path) == sample && !File.Exists(path + ".tmp"),
                "Successful writes leave valid JSON and no temporary file"
            );
            var last = sample with
            {
                Search = sample.Search with
                {
                    Threshold = 0.85
                }
            };
            store.QueueSave(sample with
            {
                Search = sample.Search with
                {
                    Threshold = 0.82
                }
            });
            store.QueueSave(last);
            store.Flush();
            check(
                Read(path) == last && Read(path + ".bak") == sample,
                "Rapid edits coalesce and preserve the previous configuration as backup"
            );
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read))
            {
                store.QueueSave(sample);
                check(
                    !store.Flush() && store.LastError is not null && Read(path) == last,
                    "Write failure is reported while preserving the valid configuration"
                );
            }

            check(
                store.Flush() && store.LastError is null && Read(path) == sample,
                "Failed writes keep a pending snapshot for retry"
            );
        }

        string debouncePath = Path.Combine(directory, "debounce", "config.json");
        using (var store = new WorkbenchConfigurationStore(debouncePath, delayMs: 80))
        {
            store.QueueSave(sample);
            store.QueueSave(sample with
            {
                Verification = sample.Verification with
                {
                    RgbTolerance = 8
                }
            });
            WaitFor(() => File.Exists(debouncePath));
            check(
                Read(debouncePath).Verification.RgbTolerance == 8,
                "Debounced configuration saving happens automatically"
            );
            store.QueueSave(sample);
        }

        check(
            Read(debouncePath) == sample,
            "Disposal flushes edits before the debounce timer expires"
        );
        string corrupt = Path.Combine(directory, "corrupt", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllText(corrupt, "{broken");
        using (var store = new WorkbenchConfigurationStore(corrupt))
        {
            check(
                store.Load(out var warning) == new WorkbenchConfiguration() && warning is not null,
                "Malformed config falls back to defaults with a warning"
            );
            store.Flush();
            check(
                File.ReadAllText(corrupt) == "{broken",
                "Loading or closing does not overwrite a broken configuration"
            );
            store.QueueSave(sample);
            store.Flush();
            check(
                File.ReadAllText(corrupt + ".bak") == "{broken" && Read(corrupt) == sample,
                "First later edit backs up the malformed original before replacing it"
            );
        }

        string denied = Path.Combine(directory, "blocked", "config.json");
        Directory.CreateDirectory(denied + ".tmp");
        using (var store = new WorkbenchConfigurationStore(denied))
        {
            store.QueueSave(sample);
            check(
                !store.Flush() && store.LastError is not null,
                "Unwritable configuration targets report an error instead of crashing"
            );
        }

        TestRestart(directory, root, sample, check);
        TestUnavailableReferences(directory, sample, check);
        void Reject(string json, string message)
        {
            try
            {
                WorkbenchConfigurationStore.Parse(json);
            }
            catch (JsonException)
            {
                check(true, message);
                return;
            }

            check(false, message);
        }
    }

    /// <summary>
    /// 驗證配置延後保存、正常關閉與再次啟動時的介面還原。
    /// </summary>
    private static void TestRestart(string directory, string root, WorkbenchConfiguration sample,
        Action<bool, string> check)
    {
        string path = Path.Combine(directory, "restart", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string flow = "{\"data\":[{\"press\":\"E\",\"process\":[]}]}";
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "flow.json"), flow);
        Write(path, sample);
        string original = File.ReadAllText(path);
        using (var form = Host(path))
        {
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            var preview = Find<RoiPictureBox>(detection).Single();
            WaitFor(() => preview.Image is not null);
            check(
                preview.Selection == new Rectangle(35, 25, 60, 60)
                && preview.DisplayedSourceBounds == preview.Selection,
                "Startup restores ROI and focused static-image preview before detection"
            );
            check(
                Find<NumericUpDown>(detection).Single(control => control.Name == "Threshold")
                .Value == 0.9m
                && Find<NumericUpDown>(detection).Single(control => control.Name == "ProcessScale")
                .Value == 0.5m,
                "Startup restores search values"
            );
            var pixel = Find<PixelVerificationControl>(detection).Single();
            check(
                pixel.GetOptions() == sample.Verification,
                "Startup restores all editable five-point parameters"
            );
            check(
                detection.Configuration.Read()?.Data.Single().Press == "E",
                "Startup reloads the skill flow file into independent runtime memory"
            );
            check(
                Find<Button>(detection).Any(button => button.Text == "開始偵測")
                && Find<DataGridView>(detection).Single(grid => grid.Name == "DetectionResults")
                .Rows.Count == 0,
                "Restoring paths and settings never automatically starts detection"
            );
            check(
                File.ReadAllText(path) == original,
                "Batched startup restoration does not rewrite user configuration"
            );
            Find<NumericUpDown>(detection).Single(control => control.Name == "Threshold")
                .Value = 0.87m;
            Find<NumericUpDown>(detection).Single(control => control.Name == "ProcessScale")
                .Value = 1m;
            Find<CheckBox>(detection).Single(control => control.Text == "僅偵測選取區").Checked = false;
            pixel.ApplyOptions(pixel.GetOptions() with
            {
                RgbTolerance = 5,
                MinimumMatches = 3
            });
            WaitFor(() => Read(path).Search.Threshold == 0.87
                && Read(path).Verification.RgbTolerance == 5);
            var saved = Read(path);
            check(
                saved.Search is { ProcessScale: 1, UseRoi: false }
                && saved.Verification.MinimumMatches == 3,
                ("Main controls and imported pixel options automatically update the "
                + "unified configuration")
            );
            check(
                saved.Source == sample.Source && saved.SkillFlowPath == sample.SkillFlowPath,
                ("Editing search settings preserves source references and the external "
                + "skill flow path")
            );
            check(
                Read(path + ".bak") == sample,
                "Startup events do not create intermediate partial configuration backups"
            );
            pixel.ApplyOptions(pixel.GetOptions() with
            {
                RgbTolerance = 7
            });
            form.Close();
        }

        check(
            Read(path).Verification.RgbTolerance == 7,
            "Normal form close flushes the last unsaved parameter edit"
        );
        using (var form = Host(path))
        {
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            var preview = Find<RoiPictureBox>(detection).Single();
            WaitFor(() => preview.Image is not null);
            check(
                Find<PixelVerificationControl>(detection).Single().GetOptions().RgbTolerance == 7
                && Find<NumericUpDown>(detection).Single(control => control.Name == "Threshold")
                .Value == 0.87m,
                "A second application instance restores the last saved configuration"
            );
            check(
                preview.Selection is not null && preview.DisplayedSourceBounds == new Rectangle(0,
                0, 140, 120),
                "An unchecked ROI is remembered without forcing preview focus"
            );
            using var rendered = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, "configuration-restored.png"));
            Find<Button>(detection).Single(button => button.Text == "切換螢幕來源").PerformClick();
            form.Close();
        }

        check(
            Read(path).Source.Kind == SourceKind.Screen
            && Read(path).Source.ImagePath == sample.Source.ImagePath,
            "Switching to screen mode remembers the last static image reference"
        );
        using (var form = Host(path))
        {
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            var preview = Find<RoiPictureBox>(detection).Single();
            WaitFor(() => preview.Image is not null);
            var selected = Find<ComboBox>(detection)
                .Single(control => control.Name == "SourceScreen");
            check(
                Screen.AllScreens[selected.SelectedIndex].DeviceName ==
                sample.Source.ScreenDeviceName
                && preview.Image!.Size == Screen.AllScreens[selected.SelectedIndex].Bounds.Size,
                ("Screen mode and the selected monitor restore independently of the "
                + "remembered image path")
            );
            form.Close();
        }
    }

    /// <summary>
    /// 驗證失效來源、樣版與流程參照的警告及安全還原行為。
    /// </summary>
    private static void TestUnavailableReferences(string directory, WorkbenchConfiguration sample,
        Action<bool, string> check)
    {
        string path = Path.Combine(directory, "missing-references", "config.json");
        var unavailable = sample with
        {
            Source = sample.Source with
            {
                ScreenDeviceName = "missing-monitor",
                ImagePath = "missing.png",
                TemplateFolderPath = "missing-folder"
            },
            SkillFlowPath = "missing-flow.json"
        };
        Write(path, unavailable);
        using (var form = Host(path))
        {
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            check(
                !Find<CheckBox>(detection).Single(control => control.Text == "僅偵測選取區").Checked
                && detection.ConfigurationNotice.Contains("不存在"),
                "Missing sources and monitors are reported and unsafe ROI is disabled"
            );
            check(
                !detection.Configuration.HasValue
                && Find<PixelVerificationControl>(detection).Single()
                .GetOptions() == sample.Verification,
                "Unavailable external files do not block restoration of valid parameters"
            );
            Find<NumericUpDown>(detection).Single(control => control.Name == "Threshold")
                .Value = 0.88m;
            form.Close();
        }

        check(
            Read(path).Source == unavailable.Source
            && Read(path).SkillFlowPath == unavailable.SkillFlowPath,
            "Missing external paths stay recorded after unrelated settings are saved"
        );
        foreach (var roi in new[]
        {
            new RoiConfiguration(35, 25, 60, 60, 100, 100),
            new RoiConfiguration(130, 0, 60, 60, 140, 120)
        }
        )
        {
            string mismatch = Path.Combine(directory, Guid.NewGuid().ToString("N"), "config.json");
            Write(
                mismatch,
                sample with
                {
                    Source = sample.Source with
                    {
                        ImagePath = Path.Combine(directory, "..", "pixel-ui.png"),
                        TemplateFolderPath = null
                    },
                    Search = sample.Search with
                    {
                        Roi = roi
                    },
                    SkillFlowPath = null
                }
            );
            using var form = Host(mismatch);
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            check(
                Find<RoiPictureBox>(detection).Single().Selection is null
                && !Find<CheckBox>(detection).Single(control => control.Text == "僅偵測選取區").Checked,
                "Changed source dimensions or out-of-bounds ROI are not restored blindly"
            );
            form.Close();
        }

        string broken = Path.Combine(directory, "broken-ui", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(broken)!);
        File.WriteAllText(broken, "{broken");
        using (var form = Host(broken))
        {
            form.Show();
            var detection = Find<DetectionControl>(form).Single();
            check(
                detection.ConfigurationNotice.Contains("使用預設值"),
                "Malformed configuration warning is visible in the main UI"
            );
            form.Close();
        }

        check(
            File.ReadAllText(broken) == "{broken",
            "Opening and closing with malformed configuration preserves the original file"
        );
    }

    /// <summary>
    /// 建立不顯示於工作列的離屏測試視窗。
    /// </summary>
    private static Form1 Host(string path) => new(path)
    {
        ShowInTaskbar = false,
        Opacity = 0,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
    };

    /// <summary>
    /// 遞迴找出指定型別的子控制項，供介面驗證定位。
    /// </summary>
    private static IEnumerable<T> Find<T>(Control parent)
        where T : Control => parent.Controls.Cast<Control>().SelectMany(child => (child is T match
        ? new[] { match } : []).Concat(Find<T>(child)));

    /// <summary>
    /// 讀取並解析隔離測試使用的資料或編輯器快照。
    /// </summary>
    private static WorkbenchConfiguration Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return WorkbenchConfigurationStore.Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// 將測試配置寫入隔離目錄，不修改正式使用者設定。
    /// </summary>
    private static void Write(string path, WorkbenchConfiguration value)
    {
        using var store = new WorkbenchConfigurationStore(path);
        store.QueueSave(value);
        if (!store.Flush())
            throw new InvalidOperationException(store.LastError);
    }

    /// <summary>
    /// 在有限等待時間內處理介面訊息並等待指定測試條件成立。
    /// </summary>
    private static void WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            if (condition())
                return;
            Thread.Sleep(10);
        }

        throw new InvalidOperationException(
            "Timed out waiting for configuration persistence or UI restoration.");
    }
}
