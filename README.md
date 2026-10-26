# WOWCV

Windows 影像辨識與技能流程工作台：切割樣版、編輯流程、比對畫面，並依辨識結果輸出鍵盤按鍵。

A Windows workbench for template slicing, workflow editing, screen recognition, and recognition-driven keyboard output.

## 快速使用（繁體中文）

介面目前使用繁體中文。第一次使用可依下列順序操作；只需影像辨識時可跳過第 4、6 步。

1. **啟動**：在儲存庫根目錄執行 `dotnet run --project WOWCV.csproj`。環境需求見下方「執行方式」。
2. **準備樣版**：到「建立技能樣版」按「載入技能欄圖片」，拖曳框選要切割的範圍，設定「列數／欄數」，確認格線後按「匯出 PNG」。記下輸出資料夾；已有樣版圖片可跳過切圖。
3. **選擇來源與樣版**：回「主頁 · 影像辨識」，選擇螢幕並按「切換螢幕來源」，或按「靜態圖片」開啟圖片；再按「載入樣版資料夾」選擇第 2 步的資料夾。
4. **建立流程**：到「技能流程設定」，按「新增按鍵組」設定觸發鍵，再按「新增技能」填寫名稱。按列內「選圖」綁定主頁樣版資料夾中的圖片，填寫「施放鍵」，按「儲存 JSON / TXT」。例如觸發鍵 `E`、施放鍵 `1`，兩者不可相同。
5. **開始辨識**：若使用流程，回主頁按「匯入 TXT / JSON 暫存」載入剛儲存的檔案。保持「五點驗證」勾選，按「開始偵測」，查看「辨識結果」；有完整流程綁定時，也可查看「已定位技能」。
6. **啟用按鍵輸出（選用）**：使用螢幕來源，確認已定位結果與流程綁定，選好「觸發模式」後勾選「啟用技能施放」。將目標視窗切到前景，再按住或切換觸發鍵；輸出會送到**當前前景視窗**。靜態圖片不能啟用實際施放。
7. **停止或重新定位**：取消「啟用技能施放」只停用按鍵輸出；按「停止偵測」會一併停用施放。圖示或畫面布局移動後，按「重新定位技能」，等候再次命中，並重新勾選施放開關。

## Quick Start (English)

The interface currently uses Traditional Chinese. The labels below match the buttons on screen. For image recognition only, skip steps 4 and 6.

1. **Launch:** Run `dotnet run --project WOWCV.csproj` from the repository root. See “Running” below for requirements.
2. **Prepare templates:** Open **建立技能樣版** (Create templates), click **載入技能欄圖片** (Load image), and drag to select the area to slice. Set **列數／欄數** (rows/columns), check the grid, then click **匯出 PNG** (Export PNG). Keep the output folder path. Skip slicing if you already have template images.
3. **Select a source and templates:** On **主頁 · 影像辨識** (Recognition), select a monitor and click **切換螢幕來源** (Switch to screen), or use **靜態圖片** (Static image). Click **載入樣版資料夾** (Load template folder) and select the folder from step 2.
4. **Create a workflow:** Open **技能流程設定** (Workflow settings). Click **新增按鍵組** (Add key group), set a trigger key, and click **新增技能** (Add skill). Enter a name, use the row’s **選圖** (Select image) button to bind an image from the main page’s template folder, and enter a **施放鍵** (Output key). Click **儲存 JSON / TXT** (Save). For example, use trigger `E` and output `1`; they must be different.
5. **Start recognition:** If using a workflow, return to the main page and use **匯入 TXT / JSON 暫存** (Import workflow snapshot) to load the saved file. Keep **五點驗證** (Five-point verification) enabled and click **開始偵測** (Start detection). Inspect **辨識結果** (Detection results); fully bound workflow entries also appear under **已定位技能** (Located skills) after successful localization.
6. **Enable keyboard output (optional):** Use a screen source, verify localization and bindings, select **觸發模式** (Trigger mode), and check **啟用技能施放** (Enable casting). Focus the intended window, then hold or toggle the trigger key. Output goes to the **current foreground window**. Static images cannot enable actual casting.
7. **Stop or relocate:** Uncheck **啟用技能施放** to stop keyboard output only. **停止偵測** (Stop detection) also disables casting. After icons or the layout move, click **重新定位技能** (Relocate skills), wait for a new match, and enable casting again.

## 執行方式 / Running

### 環境需求 / Requirements

- Windows x64，使用 .NET 10 SDK（x64）從原始碼建置及執行。
  Windows x64 with the .NET 10 SDK (x64) to build and run from source.
- 安裝 [Visual C++ 2022 Redistributable（x64）](https://support.microsoft.com/help/2977003/)，供 OpenCvSharp 原生元件使用。
  Install the [Visual C++ 2022 Redistributable (x64)](https://support.microsoft.com/help/2977003/) for OpenCvSharp's native components.
- 首次執行需還原 NuGet 套件；`dotnet run` 會自動還原及建置。
  The first run needs NuGet package restore; `dotnet run` restores and builds automatically.

在包含 `WOWCV.csproj` 的儲存庫根目錄執行：
Run from the repository root containing `WOWCV.csproj`:

```powershell
dotnet run --project WOWCV.csproj
```

只建置而不啟動 / Build without launching:

```powershell
dotnet build WOWCV.csproj
```

## 操作提醒與常見問題 / Usage Notes and Troubleshooting

| 情況 / Situation | 操作 / What to do |
|---|---|
| 編輯後主頁仍用舊流程 / The main page still uses the old workflow | 編輯器先儲存，再回主頁重新匯入。兩處資料不即時連動。 / Save in the editor, then import again on the main page; editor changes are not applied live. |
| 樣版資料夾加入新圖 / New images were added to the template folder | 再按「載入樣版資料夾」選同一資料夾，更新快取。 / Load the same template folder again to refresh its cache. |
| 無法啟用施放 / Casting cannot be enabled | 確認螢幕來源、五點驗證、開始偵測、流程匯入、樣版檔案及至少一份完整綁定；查看施放狀態的原因。 / Check the screen source, five-point verification, active detection, imported workflow, template files, and at least one complete binding; read the casting status message. |
| 未出現已定位技能 / No located skills | 先確認流程名稱、樣版與施放鍵綁定完整。首次定位須同時通過樣版搜尋與五點驗證；再檢查下方參數表。 / Check complete name/image/output-key bindings. Initial localization requires both template search and five-point verification; consult the parameter tables below. |
| 「重新偵測」與「重新定位技能」 / Reanalyze vs. relocate | 「重新偵測」重算畫面並保留技能位置；「重新定位技能」清除記住位置，再等候成功命中。 / Reanalysis keeps remembered positions; relocation clears them and waits for a new successful match. |
| 修改來源、ROI、流程或施放設定 / Source, ROI, workflow, or casting settings changed | 施放會停用；確認新結果後需重新勾選開關。 / Casting is disabled; review the new results and enable it again. |

每輪依「優先序小 → 權重大 → 文件原始順序」挑選第一個通過辨識與冷卻條件的技能，最多輸出一組按鍵，並非輪流施放。
Each cycle selects the first eligible skill by lower priority number, higher weight, then original document order. It outputs at most one key combination, rather than rotating through entries.

首次定位後，程式會記住原圖位置並對新畫面直接做五點驗證；位置不會自動跟隨新的搜尋候選。
After initial localization, the program remembers the original-image position and verifies its five points on new frames. It does not automatically follow later search candidates.

### 檔案與設定保存 / Files and Saved Settings

- `config.json` 自動儲存在執行檔旁，記錄來源、樣版／流程路徑及操作參數；啟動時不會自動開始偵測或啟用施放。
  `config.json` is saved beside the executable and records source, template/workflow paths, and settings. Startup does not automatically start detection or enable casting.
- 流程可用 `.json` 或 `.txt` 儲存，兩者內容皆為 JSON。設定檔只記住流程路徑，不取代流程原檔或樣版圖片。
  Workflow files may use `.json` or `.txt`; both contain JSON. The configuration stores the workflow path, not a replacement for workflow files or images.
- 切圖預設建立 `Skills_yyMMdd_HHmmss` 資料夾，每張 PNG 以 GUID 命名；也可選「加入既有資料夾」。既有檔案不會被清空或覆寫。
  Slicing creates a `Skills_yyMMdd_HHmmss` folder by default, with GUID-named PNG files. “加入既有資料夾” adds images to an existing folder without clearing or overwriting existing files.
- 五點設定可獨立匯入／匯出 JSON；僅包含五點驗證設定，不包含處理倍率等其他搜尋參數。
  Five-point settings can be imported/exported separately as JSON; this does not include other search settings such as processing scale.

## 授權 / License

自有程式碼採 [MIT License](LICENSE)。第三方元件保留各自授權，詳見[第三方套件聲明](THIRD-PARTY-NOTICES.md)；發布執行檔時須另核對實際隨附元件。

Original project code is licensed under the [MIT License](LICENSE). Third-party components retain their respective licenses; see [Third-Party Notices](THIRD-PARTY-NOTICES.md). Binary distributions require checking the components actually included.

## 參數調整 / Parameter Reference

以下為沒有既存配置時的程式預設值；已保存的設定會在下次啟動恢復。先保留預設值，依畫面與結果逐項調整。

These are program defaults when no saved configuration exists. Saved settings are restored on the next launch. Start with defaults and adjust one setting at a time while checking results.

### 辨識與範圍 / Recognition and Region

| 參數 / Parameter | 預設／範圍 / Default / Range | 用途與調整 / Purpose and Adjustment |
|---|---|---|
| 門檻 / Threshold | `0.80`；`0–1` | 越高越嚴格；過高可能漏掉候選，過低可能誤判。 / Higher is stricter; too high may miss candidates, too low may accept false matches. |
| 處理倍率 / Processing scale | `1.00`；`0.10–1.00` | 降低可減少搜尋影像尺寸，但可能失去細節；五點仍取原圖像素。 / Lower values reduce search-image size but may lose detail; five-point checks still use original pixels. |
| 多尺度 / Multi-scale | 關 / Off；`0.8–1.2` | 嘗試不同樣版大小，可能增加搜尋成本；五點不會強制縮放不同尺寸來比較。 / Tries different template sizes at extra search cost; five-point checks do not resize mismatched dimensions to force a comparison. |
| 僅偵測選取區 / Use selected region only | 關 / Off | 按「框選偵測範圍」後拖曳框選，再勾選使用；縮小搜尋區域可減少無關候選。 / Drag a region using “框選偵測範圍”, then enable it to limit the search area. |

### 五點驗證 / Five-Point Verification

| 參數 / Parameter | 預設／範圍 / Default / Range | 用途與調整 / Purpose and Adjustment |
|---|---|---|
| 五點驗證 / Enabled | 開 / On | 比較原圖與樣版的五個 RGB 取樣點；實際施放必須啟用。 / Compares five original-image/template RGB samples; required for casting. |
| RGB 容差 ± / RGB tolerance | `0`；`0–255` | 每個色彩通道允許的差值，0 代表完全一致。提高可容忍色差，也可能增加誤判。 / Allowed difference per channel; 0 requires exact equality. Increasing it tolerates color variation but may increase false matches. |
| 至少通過點數 / Minimum matching points | `5`；`1–5` | 始終讀取完整五點，設定 4 表示任意至少 4/5 通過，不是忽略中心點。 / Always samples all five points; 4 means any four must pass, not that the center is ignored. |
| 五點 X／Y 偏移 / Sample offsets | 中心 / Center `(0,0)`；上 / Up `(0,-10)`；下 / Down `(0,10)`；左 / Left `(-10,0)`；右 / Right `(10,0)` | 相對樣版中心的原始像素整數偏移，所有技能共用。取樣越界會拒絕命中；小樣版須調整偏移。 / Integer pixel offsets from the template center, shared by all skills. Out-of-bounds samples reject the match; adjust offsets for small templates. |

### 流程決策與按鍵輸出 / Workflow Decisions and Keyboard Output

| 參數 / Parameter | 預設／範圍 / Default / Range | 用途與調整 / Purpose and Adjustment |
|---|---|---|
| 觸發鍵 / Trigger key (`press`) | 單一鍵盤按鍵 / Single keyboard key | 選擇作用中的按鍵組；不得出現在任何施放組合中。 / Activates a key group; must not occur in any output combination. |
| 樣版 / Template (`template_file`) | 新技能未綁定 / Unbound for new skills | 從主頁樣版資料夾選圖，只儲存檔名；沒有完整名稱／圖片／施放鍵的技能會略過。 / Select from the main page’s template folder; stores a filename. Entries missing a name, image, or output key are skipped. |
| 施放鍵 / Output key (`cast_key`) | 新技能未綁定 / Unbound for new skills | 支援單鍵及 Ctrl／Alt／Shift 組合，例如 `1`、`Ctrl+1`。 / Supports a single key or Ctrl/Alt/Shift combinations, such as `1` or `Ctrl+1`. |
| 優先序 / Priority (`priority`) | `1`；整數 / Integer `1–10` | 數字越小越優先。 / Lower numbers are considered first. |
| 權重 / Weight (`skillweight`) | `100`；整數 / Integer `1–100` | 同優先序時，數字越大越優先；仍相同則依文件原始順序。 / Higher weights win within a priority; ties use original document order. |
| 冷卻秒數 / Cooldown (`cd_sec`) | `0`；整數秒 / Integer seconds `0–600` | 完整送鍵後開始計算；同名技能跨流程共用冷卻。 / Starts after key output completes; identical skill names share cooldowns across workflows. |
| 冷卻檢查 / Cooldown check (`cooldown_check`) | 空白 / Empty | 填另一技能名稱時，還須等待該技能的程式冷卻結束；引用不存在名稱會阻止啟用。 / An optional skill name whose program cooldown must also expire; missing references prevent enabling casting. |
| 狀態 / Status (`status`) | `Y`；`Y` / `A` | 保留欄位，目前不改變辨識或施放決策。 / Reserved field; currently does not alter recognition or casting decisions. |
| 觸發模式 / Trigger mode | 按住 / Hold；另可選切換 / Toggle | 按住模式後按生效，放開恢復仍按住的前一組；切換模式再按目前觸發鍵停止。 / Hold mode gives the latest held key precedence, restoring a previously held group on release; toggle mode stops when the active trigger is pressed again. |
| 共用間隔 ms / Shared interval | `150`；`0–60000 ms` | 一次完整送鍵後，所有技能共同等待；即使為 0，也須等候送鍵後才開始擷取的新畫面。 / Shared wait after output completes; even at 0, the next decision waits for a frame whose capture began after output completed. |
| 按鍵持續 ms / Key hold duration | `30`；`1–5000 ms` | 輸出鍵按下至放開的時間，與觸發鍵的按住操作不同。 / Time between pressing and releasing the output key; separate from holding a workflow trigger. |

### 樣版切圖 / Template Slicing

| 參數 / Parameter | 預設／範圍 / Default / Range | 用途與調整 / Purpose and Adjustment |
|---|---|---|
| 列數 / Rows | `1`；`1–100` | 將選取區分成多少橫列；每格至少需 1 像素高。 / Number of horizontal rows; each cell must be at least one pixel high. |
| 欄數 / Columns | `12`；`1–100` | 將選取區分成多少直欄；每格至少需 1 像素寬。 / Number of vertical columns; each cell must be at least one pixel wide. |
| 放大鏡 / Magnifier | 開 / On；`4×`，可調 / Range `2–12×` | 協助確認邊界與像素，不改變原圖框選座標。 / Helps inspect boundaries and pixels without changing selection coordinates. |
| 匯出方式 / Export mode | 建立新資料夾 / New folder | 可改為「加入既有資料夾」；新增檔案後需在主頁重新載入樣版。 / Can add to an existing folder; reload templates on the main page after adding files. |
