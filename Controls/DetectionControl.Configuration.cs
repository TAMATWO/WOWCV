using System.Text.Json;
using OpenCvSharp;
using WOWCV.Models;
using WOWCV.Services;
using Size = System.Drawing.Size;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly WorkbenchConfigurationStore configurationStore;
    private WorkbenchConfiguration appConfiguration = new();
    private bool applyingConfiguration, configurationReady;
    private readonly List<string> configurationWarnings = [];
    private readonly Label persistenceStatus = new()
    {
        Name = "PersistenceStatus", Dock = DockStyle.Bottom, Height = 38, AutoEllipsis = true
    };

    public string ConfigurationFilePath => configurationStore.FilePath;
    public string ConfigurationNotice => persistenceStatus.Text;

    private void RestoreConfiguration()
    {
        applyingConfiguration = true;
        configurationWarnings.Clear();
        try
        {
            appConfiguration = configurationStore.Load(out var warning);
            if (warning is not null) configurationWarnings.Add(warning);
            var source = appConfiguration.Source;
            var allScreens = Screen.AllScreens;
            int index = Array.FindIndex(allScreens, screen => screen.DeviceName == source.ScreenDeviceName);
            bool trustedSource = source.ScreenDeviceName is null || index >= 0;
            if (index < 0)
            {
                index = 0;
                if (source.ScreenDeviceName is not null && source.Kind == SourceKind.Screen)
                    configurationWarnings.Add("找不到原螢幕，已使用可用螢幕並停用原 ROI");
            }
            screens.SelectedIndex = allScreens.Length > 0 ? index : -1;
            Size sourceSize = allScreens.ElementAtOrDefault(index)?.Bounds.Size ?? Size.Empty;
            if (sourceSize.IsEmpty && source.Kind == SourceKind.Screen) configurationWarnings.Add("找不到可用螢幕");
            imagePath = null;
            if (source.Kind == SourceKind.Image)
            {
                trustedSource = false;
                string? path = ResolveExistingReference(source.ImagePath, directory: false, "靜態圖片");
                if (path is not null)
                {
                    try
                    {
                        using var image = Cv2.ImRead(path, ImreadModes.Color);
                        if (image.Empty()) configurationWarnings.Add("靜態圖片無法解碼，暫用螢幕預覽");
                        else { imagePath = path; sourceSize = new Size(image.Width, image.Height); trustedSource = true; }
                    }
                    catch (Exception ex) when (ex is OpenCVException or IOException or UnauthorizedAccessException)
                    { configurationWarnings.Add($"靜態圖片無法載入：{ex.Message}"); }
                }
            }
            templateFolder = ResolveExistingReference(source.TemplateFolderPath, directory: true, "樣版資料夾");
            threshold.Value = (decimal)appConfiguration.Search.Threshold;
            processScale.Value = (decimal)appConfiguration.Search.ProcessScale;
            multiScale.Checked = appConfiguration.Search.MultiScale;
            pixelSettings.ApplyOptions(appConfiguration.Verification);
            RestoreCastingOptions();
            preview.RestoreSelection(null);
            useRoi.Checked = appConfiguration.Search.UseRoi;
            if (appConfiguration.Search.Roi is { } roi)
            {
                if (trustedSource && roi.Fits(sourceSize)) preview.RestoreSelection(new Rectangle(roi.X, roi.Y, roi.Width, roi.Height));
                else
                {
                    useRoi.Checked = false;
                    configurationWarnings.Add("原 ROI 與目前來源尺寸不符或越界，請重新框選");
                }
            }
            if (appConfiguration.SkillFlowPath is { Length: > 0 } flowPath)
            {
                string? path = ResolveExistingReference(flowPath, directory: false, "技能流程檔");
                if (path is not null)
                {
                    try
                    {
                        Configuration.Import(File.ReadAllText(path), out var adjustments);
                        var document = Configuration.Read()!;
                        configurationPreview.Text = SkillDocumentService.Serialize(document);
                        configStatus.Text = $"已恢復：{Path.GetFileName(path)} · {document.Data.Count} 組按鍵";
                        if (adjustments.Count > 0)
                        {
                            configStatus.Text += $" · {adjustments.Count} 個 cd_sec 已四捨五入（原檔未修改）";
                            configurationWarnings.Add($"技能流程檔：{adjustments.Count} 個 cd_sec 已四捨五入，僅套用至暫存，原檔未修改");
                        }
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                    { configurationWarnings.Add($"技能流程檔未載入：{ex.Message}"); }
                }
            }
            detecting = false; toggle.Text = "開始偵測";
        }
        finally { applyingConfiguration = false; configurationReady = true; }
        TemplateFolderChanged?.Invoke(this, EventArgs.Empty);
        UpdatePreviewViewport(); RefreshPersistenceStatus();
    }

    private string? ResolveExistingReference(string? reference, bool directory, string label)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            if (label == "靜態圖片") configurationWarnings.Add("未指定靜態圖片，暫用螢幕預覽");
            return null;
        }
        try
        {
            string path = configurationStore.ResolveReference(reference);
            if (directory ? Directory.Exists(path) : File.Exists(path)) return path;
            configurationWarnings.Add($"{label}不存在，請重新選擇：{reference}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { configurationWarnings.Add($"{label}路徑無效：{ex.Message}"); }
        return null;
    }

    private void QueueConfigurationSave()
    {
        if (!configurationReady || applyingConfiguration || stopped) return;
        appConfiguration = appConfiguration with
        {
            Source = appConfiguration.Source with
            {
                ScreenDeviceName = appConfiguration.Source.ScreenDeviceName
                    ?? Screen.AllScreens.ElementAtOrDefault(screens.SelectedIndex)?.DeviceName
            },
            Search = appConfiguration.Search with
            {
                Threshold = (double)threshold.Value, ProcessScale = (double)processScale.Value,
                MultiScale = multiScale.Checked, UseRoi = useRoi.Checked
            },
            Verification = pixelSettings.GetOptions(),
            Casting = GetCastingOptions()
        };
        configurationStore.QueueSave(appConfiguration);
        RefreshPersistenceStatus();
    }

    private void RefreshPersistenceStatus()
    {
        if (configurationStore.HasSaved) configurationWarnings.RemoveAll(warning => warning.StartsWith("配置未載入"));
        var error = configurationStore.LastError;
        string notice = error ?? (configurationWarnings.Count > 0 ? string.Join("；", configurationWarnings) : "修改設定後自動保存；下次啟動恢復，不自動開始偵測");
        persistenceStatus.ForeColor = error is not null || configurationWarnings.Count > 0 ? Color.DarkRed : Color.DarkSlateGray;
        persistenceStatus.Text = $"配置：{configurationStore.FilePath}\n{notice}";
    }

    private void ClearConfigurationWarnings(params string[] prefixes) => configurationWarnings.RemoveAll(warning => prefixes.Any(prefix => warning.StartsWith(prefix)));

    /// <summary>Does not queue anything on startup; only flushes edits already made by the user.</summary>
    public string? FlushConfiguration()
    {
        configurationStore.Flush(); RefreshPersistenceStatus();
        return configurationStore.LastError;
    }
}
