# 第三方套件與授權聲明

本文件記錄本儲存庫原始碼所直接引用的第三方套件及其授權來源，
以 [WOWCV.csproj](WOWCV.csproj) 的套件宣告為準。
套件名稱、版本及授權資訊用於識別依賴與保留來源聲明。

本文件與附帶的授權全文適用於所列第三方元件，不替本專案自行撰寫的程式碼選定授權，
也不代表第三方作者或組織對本專案提供背書。

## 直接引用的 NuGet 套件

| 套件 | 版本 | 用途 | 套件宣告的授權 |
|---|---|---|---|
| [OpenCvSharp4](https://www.nuget.org/packages/OpenCvSharp4/4.13.0.20260330) | 4.13.0.20260330 | OpenCV 的 .NET 包裝與受控介面 | Apache-2.0 |
| [OpenCvSharp4.Extensions](https://www.nuget.org/packages/OpenCvSharp4.Extensions/4.13.0.20260330) | 4.13.0.20260330 | GDI+ 圖片轉換等擴充功能 | Apache-2.0 |
| [OpenCvSharp4.runtime.win](https://www.nuget.org/packages/OpenCvSharp4.runtime.win/4.13.0.20260302) | 4.13.0.20260302 | Windows 原生綁定與執行元件 | Apache-2.0 |

以上三個套件的 NuGet 中繼資料均列出：

- 作者（authors）：`shimat`
- 著作權欄位原文（copyright）：`Copyright 2008-2026`
- 上游專案：[shimat/opencvsharp](https://github.com/shimat/opencvsharp)
- 授權識別碼：`Apache-2.0`

本專案透過 NuGet 引用這些套件；目前 Git 追蹤內容未包含這些套件的 DLL 或套件封裝。

### 版本來源與 NOTICE

依各套件 `.nuspec` 的 repository 欄位，對應上游原始碼為：

- `OpenCvSharp4`、`OpenCvSharp4.Extensions`：
  [526049e7c34885b8f1da852692314b5a4aeb8bed](https://github.com/shimat/opencvsharp/tree/526049e7c34885b8f1da852692314b5a4aeb8bed)，
  [該版本 LICENSE](https://github.com/shimat/opencvsharp/blob/526049e7c34885b8f1da852692314b5a4aeb8bed/LICENSE)。
- `OpenCvSharp4.runtime.win`：
  [4f661855e0bc5455eab5744ee8b05c7b2ed12af0](https://github.com/shimat/opencvsharp/tree/4f661855e0bc5455eab5744ee8b05c7b2ed12af0)，
  [該版本 LICENSE](https://github.com/shimat/opencvsharp/blob/4f661855e0bc5455eab5744ee8b05c7b2ed12af0/LICENSE)。

核對上述兩份上游原始碼樹及三個版本的 NuGet 封裝時，未找到獨立的 NOTICE 檔案。
此結果只涵蓋上述 OpenCvSharp 來源與封裝，不表示其原生相依元件均無額外聲明要求。

## OpenCV 與原生相依元件

OpenCvSharp 是 OpenCV 的 .NET 包裝，與底層 OpenCV 為不同專案。
所引用的套件系列以 OpenCV 4.13.0 為目標；OpenCV 該版本採 Apache-2.0：

- [OpenCV 官方授權說明](https://opencv.org/license/)
- [OpenCV 4.13.0 LICENSE](https://github.com/opencv/opencv/blob/4.13.0/LICENSE)
- [OpenCV 原始碼與元件資訊](https://github.com/opencv/opencv/tree/4.13.0)

原生執行套件可能包含其他獨立授權的元件；套件層級的 Apache-2.0 宣告不代表
所有內含第三方內容都改採相同授權。所列 Windows 執行套件另含
`opencv_videoio_ffmpeg4130_64.dll`；若散布該檔，須依實際建置核對 FFmpeg
與相關相依元件的授權、來源提供及聲明要求，不能只依本文件判定已完成。

## 授權全文與散布範圍

Apache License, Version 2.0 的官方英文全文附於
[licenses/Apache-2.0.txt](licenses/Apache-2.0.txt)，
來源為 [Apache Software Foundation](https://www.apache.org/licenses/LICENSE-2.0.txt)。
本文件的中文說明不取代授權條款。

散布適用 Apache-2.0 的第三方內容時，須遵守其第 4 條，包括提供授權副本、
保留適用聲明、標示對第三方檔案的修改，以及在適用時保留上游 NOTICE。

本清單限於目前原始碼直接宣告的 NuGet 依賴，不是完整的相依元件清單或二進位發布包聲明。
NuGet 的間接依賴與 .NET 執行環境須依實際還原及發布結果另行核對。
未來透過 GitHub Releases、ZIP、安裝包、DLL 或單一執行檔散布程式時，
應清點實際內含的第三方元件，補齊各自要求的授權、著作權、NOTICE 與來源資訊，
並將必要文件隨發布包一同提供。
