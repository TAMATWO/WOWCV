using WOWCV.Models;
using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly SkillCastingService casting;
    private readonly CheckBox castingEnabled = new()
    {
        Name = "CastingEnabled",
        Text = "啟用技能施放",
        AutoSize = true
    };
    private readonly ComboBox triggerMode = new()
    {
        Name = "TriggerMode",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 145
    };
    private readonly NumericUpDown castInterval = new()
    {
        Name = "CastingInterval",
        Minimum = 0,
        Maximum = 60000,
        Value = 150,
        Width = 80
    };
    private readonly NumericUpDown keyHold = new()
    {
        Name = "CastingKeyHold",
        Minimum = 1,
        Maximum = 5000,
        Value = 30,
        Width = 70
    };
    private readonly Label castingStatus = new()
    {
        Name = "CastingStatus",
        Dock = DockStyle.Bottom,
        Height = 42,
        Text = "技能施放未啟用"
    };
    private bool updatingCasting, inputReady;

    /// <summary>
    /// 建立施放參數控制項，連接啟用條件檢查及參數變更事件。
    /// </summary>
    private void InitializeCastingControls(TableLayoutPanel host)
    {
        triggerMode.Items.AddRange(["按住（後按生效）", "切換啟動／停止"]);
        triggerMode.SelectedIndex = 0;
        host.Controls.Add(SettingField("觸發模式", triggerMode));
        host.Controls.Add(SettingField("共用間隔 ms", castInterval));
        host.Controls.Add(SettingField("按鍵持續 ms", keyHold));
        castingEnabled.CheckedChanged += (_, _) =>
        {
            if (updatingCasting)
                return;
            if (!castingEnabled.Checked)
            {
                casting.Disable();
                RefreshCastingStatus();
                return;
            }

            try
            {
                if (!inputReady || latestInputError.Length > 0)
                    throw new InvalidOperationException("Raw Input 尚未就緒，無法啟用施放。");
                if (!detecting || imagePath is not null || !pixelSettings.GetOptions().Enabled)
                    throw new InvalidOperationException("請使用螢幕來源、啟用五點驗證並開始偵測。");
                var document = Configuration.Read() ?? throw new InvalidOperationException(
                    "請先匯入技能流程 TXT／JSON。");
                var plan = SkillCastingPlan.Compile(document, Configuration.Bindings.Revision);
                if (templateFolder is null || !Directory.Exists(templateFolder)
                    || Configuration.Bindings.Items.Where(binding => binding.Issue is null)
                    .Any(binding => !File.Exists(Path.Combine(templateFolder,
                    binding.TemplateFile))))
                    throw new InvalidOperationException("樣版圖片遺失，請先載入正確的樣版資料夾。");
                casting.Enable(plan, GetCastingOptions(), version);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                or IOException or UnauthorizedAccessException)
            {
                casting.Disable(ex.Message);
            }

            RefreshCastingStatus();
        };
        triggerMode.SelectedIndexChanged += (_, _) => CastingOptionsChanged();
        castInterval.ValueChanged += (_, _) => CastingOptionsChanged();
        keyHold.ValueChanged += (_, _) => CastingOptionsChanged();
    }

    /// <summary>
    /// 從介面讀取觸發模式、共用間隔與按鍵持續時間。
    /// </summary>
    private CastingConfiguration GetCastingOptions() => new()
    {
        Mode = triggerMode.SelectedIndex == 1 ? TriggerMode.Toggle : TriggerMode.Hold,
        MinimumIntervalMs = (int)castInterval.Value,
        KeyHoldMs = (int)keyHold.Value
    };

    /// <summary>
    /// 將已保存的施放參數還原至介面。
    /// </summary>
    private void RestoreCastingOptions()
    {
        triggerMode.SelectedIndex = appConfiguration.Casting.Mode == TriggerMode.Toggle ? 1 : 0;
        castInterval.Value = appConfiguration.Casting.MinimumIntervalMs;
        keyHold.Value = appConfiguration.Casting.KeyHoldMs;
    }

    /// <summary>
    /// 施放參數改變時停用施放，並排程保存設定。
    /// </summary>
    private void CastingOptionsChanged()
    {
        if (applyingConfiguration || updatingCasting)
            return;
        casting.Disable("施放設定已變更，請重新啟用技能施放");
        RefreshCastingStatus();
        QueueConfigurationSave();
    }

    /// <summary>
    /// 同步目前辨識版本與施放可用條件，讓舊辨識結果失效。
    /// </summary>
    private void PrepareCastingContext()
    {
        casting.ConfigureContext(version, detecting && imagePath is null
            && pixelSettings.GetOptions().Enabled);
        RefreshCastingStatus();
    }

    /// <summary>
    /// 同步施放開關與狀態文字，避免介面更新再次觸發啟用事件。
    /// </summary>
    private void RefreshCastingStatus()
    {
        var value = casting.Status;
        updatingCasting = true;
        try
        {
            castingEnabled.Checked = value.Enabled;
        }
        finally
        {
            updatingCasting = false;
        }

        castingStatus.Text = ($"技能施放：{(value.Enabled ? "已啟用" : "已停用")} · 作用中流程："
            + $"{value.ActivePress ?? "無"} · 最近施放：{value.LastSkill ?? "無"}\n" + $"{value.Reason}");
    }
}
