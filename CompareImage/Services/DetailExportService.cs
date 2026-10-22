using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using ClosedXML.Excel;
using CompareImage.Models;

namespace CompareImage.Services;

public class DetailExportService
{
    /// <summary>
    /// Excel形式 (.xlsx) で詳細結果を出力する
    /// </summary>
    public static void ExportToExcel(IReadOnlyList<PairCompareResult> results, string filePath)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("比較結果詳細");

        // ヘッダー定義
        string[] headers =
        {
            "ペア名", "全体判定", "領域ID", "領域名", "比較方式", "領域判定",
            "Base画像", "Target画像", "Base画像パス", "Target画像パス"
        };

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 240, 240);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(200, 200, 200);
        }
        ws.Row(1).Height = 26;

        int currentRow = 2;
        int picIndex = 1;

        // ペアごとの処理
        foreach (var pair in results)
        {
            BitmapSource? baseFullBmp = null;
            BitmapSource? targetFullBmp = null;

            try
            {
                if (File.Exists(pair.BaseImagePath))
                {
                    baseFullBmp = ImageComparatorService.LoadBitmapSource(pair.BaseImagePath);
                }
            }
            catch { /* ロード失敗時はスキップ */ }

            try
            {
                if (File.Exists(pair.TargetImagePath))
                {
                    targetFullBmp = ImageComparatorService.LoadBitmapSource(pair.TargetImagePath);
                }
            }
            catch { /* ロード失敗時はスキップ */ }

            foreach (var region in pair.RegionResults)
            {
                // 元画像のクロップ
                BitmapSource? bCropped = (baseFullBmp != null && region.BaseRoi != null)
                    ? ImageComparatorService.CropBitmap(baseFullBmp, region.BaseRoi.X, region.BaseRoi.Y, region.BaseRoi.Width, region.BaseRoi.Height)
                    : null;
                BitmapSource? tCropped = (targetFullBmp != null && region.TargetRoi != null)
                    ? ImageComparatorService.CropBitmap(targetFullBmp, region.TargetRoi.X, region.TargetRoi.Y, region.TargetRoi.Width, region.TargetRoi.Height)
                    : null;

                // 前処理の有無判定
                var bPrep = region.BaseRoi?.Preprocess;
                bool hasBPrep = bPrep != null && bPrep.HasAnyOperation && bCropped != null;

                var tPrep = region.TargetRoi?.Preprocess;
                bool hasTPrep = tPrep != null && tPrep.HasAnyOperation && tCropped != null;

                bool hasPreprocess = hasBPrep || hasTPrep;

                // 1行目: 基本情報 & 元画像行
                int imageRow = currentRow;
                ws.Row(imageRow).Height = 75;

                ws.Cell(imageRow, 1).Value = pair.PairName;
                ws.Cell(imageRow, 2).Value = pair.StatusText;
                ws.Cell(imageRow, 3).Value = region.RegionId;
                ws.Cell(imageRow, 4).Value = region.RegionName;
                ws.Cell(imageRow, 5).Value = region.CompareTypeSimpleName;
                ws.Cell(imageRow, 6).Value = region.StatusSimpleText;
                ws.Cell(imageRow, 9).Value = pair.BaseImagePath;
                ws.Cell(imageRow, 10).Value = pair.TargetImagePath;

                // スタイル
                ws.Cell(imageRow, 2).Style.Font.Bold = true;
                ws.Cell(imageRow, 2).Style.Font.FontColor = pair.AllMatched ? XLColor.FromHtml("#107C41") : XLColor.FromHtml("#D83B01");
                ws.Cell(imageRow, 6).Style.Font.Bold = true;
                ws.Cell(imageRow, 6).Style.Font.FontColor = region.IsMatch ? XLColor.FromHtml("#107C41") : XLColor.FromHtml("#D83B01");

                // Base元画像をセルに配置
                if (bCropped != null)
                {
                    AddPictureToCell(ws, bCropped, imageRow, 7, picIndex++);
                }

                // Target元画像をセルに配置
                if (tCropped != null)
                {
                    AddPictureToCell(ws, tCropped, imageRow, 8, picIndex++);
                }

                currentRow++;

                // ※ 画像前処理がある場合、改行して処理後の画像も同じ列に出力
                if (hasPreprocess)
                {
                    int prepRow = currentRow;
                    ws.Row(prepRow).Height = 75;

                    ws.Cell(prepRow, 6).Value = "[前処理後]";
                    ws.Cell(prepRow, 6).Style.Font.FontSize = 9;
                    ws.Cell(prepRow, 6).Style.Font.FontColor = XLColor.FromHtml("#8E24AA");
                    ws.Cell(prepRow, 6).Style.Font.Bold = true;
                    ws.Cell(prepRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(prepRow, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    if (bCropped != null)
                    {
                        var bCroppedPrep = hasBPrep ? ImagePreprocessingService.ApplyPreprocessing(bCropped, bPrep!) : bCropped;
                        AddPictureToCell(ws, bCroppedPrep, prepRow, 7, picIndex++);
                    }

                    if (tCropped != null)
                    {
                        var tCroppedPrep = hasTPrep ? ImagePreprocessingService.ApplyPreprocessing(tCropped, tPrep!) : tCropped;
                        AddPictureToCell(ws, tCroppedPrep, prepRow, 8, picIndex++);
                    }

                    currentRow++;
                }

                // ※）文字列比較の場合は次の行にBase画像のOCR結果文字列,Target画像のOCR結果文字列を画像と同じ桁に追加する。
                if (region.CompareType == RoiCompareType.Text)
                {
                    int textRow = currentRow;
                    ws.Row(textRow).Height = 35;

                    ws.Cell(textRow, 7).Value = region.BaseTextRaw;
                    ws.Cell(textRow, 8).Value = region.TargetTextRaw;

                    ws.Cell(textRow, 7).Style.Alignment.WrapText = true;
                    ws.Cell(textRow, 8).Style.Alignment.WrapText = true;
                    ws.Cell(textRow, 7).Style.Font.FontSize = 10;
                    ws.Cell(textRow, 8).Style.Font.FontSize = 10;
                    ws.Cell(textRow, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(249, 249, 249);
                    ws.Cell(textRow, 8).Style.Fill.BackgroundColor = XLColor.FromArgb(249, 249, 249);

                    currentRow++;
                }
            }
        }

        // 列幅の設定
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 8;
        ws.Column(4).Width = 18;
        ws.Column(5).Width = 12;
        ws.Column(6).Width = 10;
        ws.Column(7).Width = 24; // Base画像
        ws.Column(8).Width = 24; // Target画像
        ws.Column(9).Width = 32;
        ws.Column(10).Width = 32;

        // 全セル垂直中央・罫線設定
        var dataRange = ws.Range(1, 1, currentRow - 1, 10);
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorderColor = XLColor.FromArgb(220, 220, 220);
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.OutsideBorderColor = XLColor.FromArgb(180, 180, 180);

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Markdown形式 (.md) で専用フォルダーを作成して詳細結果を出力する
    /// </summary>
    /// <returns>作成した出力フォルダーのパス</returns>
    public static string ExportToMarkdown(IReadOnlyList<PairCompareResult> results, string selectedPath)
    {
        // 出力フォルダーの決定: 指定されたパスの名前に基づいてフォルダーを作成
        string baseDir = Path.GetDirectoryName(selectedPath) ?? Environment.CurrentDirectory;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(selectedPath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExt)) fileNameWithoutExt = "CompareReport";

        string outputDir = Path.Combine(baseDir, fileNameWithoutExt);
        if (Directory.Exists(outputDir))
        {
            outputDir = Path.Combine(baseDir, $"{fileNameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}");
        }
        Directory.CreateDirectory(outputDir);

        string imagesDir = Path.Combine(outputDir, "images");
        Directory.CreateDirectory(imagesDir);

        string mdFilePath = Path.Combine(outputDir, $"{fileNameWithoutExt}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# 画像比較 詳細結果レポート");
        sb.AppendLine();
        sb.AppendLine($"- **出力日時**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        int totalPairs = results.Count;
        int passPairs = results.Count(r => r.AllMatched);
        int failPairs = totalPairs - passPairs;
        sb.AppendLine($"- **総ペア数**: {totalPairs} (PASS: {passPairs} / FAIL: {failPairs})");
        sb.AppendLine();

        // テーブルヘッダー
        sb.AppendLine("| ペア名 | 全体判定 | 領域ID | 領域名 | 比較方式 | 領域判定 | Base画像 | Target画像 | Base画像パス | Target画像パス |");
        sb.AppendLine("|:---|:---:|:---:|:---|:---:|:---:|:---:|:---:|:---|:---|");

        int imgCounter = 1;

        foreach (var pair in results)
        {
            BitmapSource? baseFullBmp = null;
            BitmapSource? targetFullBmp = null;

            try
            {
                if (File.Exists(pair.BaseImagePath))
                {
                    baseFullBmp = ImageComparatorService.LoadBitmapSource(pair.BaseImagePath);
                }
            }
            catch { /* 無視 */ }

            try
            {
                if (File.Exists(pair.TargetImagePath))
                {
                    targetFullBmp = ImageComparatorService.LoadBitmapSource(pair.TargetImagePath);
                }
            }
            catch { /* 無視 */ }

            foreach (var region in pair.RegionResults)
            {
                // 元画像のクロップ
                BitmapSource? bCropped = (baseFullBmp != null && region.BaseRoi != null)
                    ? ImageComparatorService.CropBitmap(baseFullBmp, region.BaseRoi.X, region.BaseRoi.Y, region.BaseRoi.Width, region.BaseRoi.Height)
                    : null;
                BitmapSource? tCropped = (targetFullBmp != null && region.TargetRoi != null)
                    ? ImageComparatorService.CropBitmap(targetFullBmp, region.TargetRoi.X, region.TargetRoi.Y, region.TargetRoi.Width, region.TargetRoi.Height)
                    : null;

                // 前処理の有無判定
                var bPrep = region.BaseRoi?.Preprocess;
                bool hasBPrep = bPrep != null && bPrep.HasAnyOperation && bCropped != null;

                var tPrep = region.TargetRoi?.Preprocess;
                bool hasTPrep = tPrep != null && tPrep.HasAnyOperation && tCropped != null;

                bool hasPreprocess = hasBPrep || hasTPrep;

                string baseImgMd = "(なし)";
                string targetImgMd = "(なし)";

                // Base元画像保存
                if (bCropped != null)
                {
                    try
                    {
                        string imgName = $"img_{imgCounter:D4}_base.png";
                        SaveBitmapToPngFile(bCropped, Path.Combine(imagesDir, imgName));
                        baseImgMd = $"<img src=\"images/{imgName}\" height=\"60\" alt=\"Base\" />";
                    }
                    catch { }
                }

                // Target元画像保存
                if (tCropped != null)
                {
                    try
                    {
                        string imgName = $"img_{imgCounter:D4}_target.png";
                        SaveBitmapToPngFile(tCropped, Path.Combine(imagesDir, imgName));
                        targetImgMd = $"<img src=\"images/{imgName}\" height=\"60\" alt=\"Target\" />";
                    }
                    catch { }
                }

                string allJudge = pair.AllMatched ? "**PASS**" : "<span style=\"color:red\">**FAIL**</span>";
                string regionJudge = region.IsMatch ? "OK" : "<span style=\"color:red\">**NG**</span>";

                // 1行目 (元画像行)
                sb.AppendLine($"| {EscapeMd(pair.PairName)} | {allJudge} | {region.RegionId} | {EscapeMd(region.RegionName)} | {region.CompareTypeSimpleName} | {regionJudge} | {baseImgMd} | {targetImgMd} | `{EscapeMd(pair.BaseImagePath)}` | `{EscapeMd(pair.TargetImagePath)}` |");

                // ※ 画像前処理がある場合、改行して処理後の画像も同じ列に出力
                if (hasPreprocess)
                {
                    string basePrepImgMd = "(前処理なし)";
                    string targetPrepImgMd = "(前処理なし)";

                    if (bCropped != null && hasBPrep)
                    {
                        try
                        {
                            var bCroppedPrep = ImagePreprocessingService.ApplyPreprocessing(bCropped, bPrep!);
                            string prepName = $"img_{imgCounter:D4}_base_prep.png";
                            SaveBitmapToPngFile(bCroppedPrep, Path.Combine(imagesDir, prepName));
                            basePrepImgMd = $"<img src=\"images/{prepName}\" height=\"60\" alt=\"Base前処理後\" />";
                        }
                        catch { }
                    }
                    else if (bCropped != null)
                    {
                        basePrepImgMd = baseImgMd;
                    }

                    if (tCropped != null && hasTPrep)
                    {
                        try
                        {
                            var tCroppedPrep = ImagePreprocessingService.ApplyPreprocessing(tCropped, tPrep!);
                            string prepName = $"img_{imgCounter:D4}_target_prep.png";
                            SaveBitmapToPngFile(tCroppedPrep, Path.Combine(imagesDir, prepName));
                            targetPrepImgMd = $"<img src=\"images/{prepName}\" height=\"60\" alt=\"Target前処理後\" />";
                        }
                        catch { }
                    }
                    else if (tCropped != null)
                    {
                        targetPrepImgMd = targetImgMd;
                    }

                    sb.AppendLine($"| | | | | | *(前処理後)* | {basePrepImgMd} | {targetPrepImgMd} | | |");
                }

                imgCounter++;

                // ※）文字列比較の場合は次の行にBase画像のOCR結果文字列,Target画像のOCR結果文字列を画像と同じ桁に追加する。
                if (region.CompareType == RoiCompareType.Text)
                {
                    string bText = EscapeMdMultiline(region.BaseTextRaw);
                    string tText = EscapeMdMultiline(region.TargetTextRaw);
                    sb.AppendLine($"| | | | | | | {bText} | {tText} | | |");
                }
            }
        }

        File.WriteAllText(mdFilePath, sb.ToString(), Encoding.UTF8);
        return outputDir;
    }

    private static void AddPictureToCell(IXLWorksheet ws, BitmapSource bmp, int row, int col, int picIndex)
    {
        try
        {
            using var ms = new MemoryStream();
            SaveBitmapToPngStream(bmp, ms);
            ms.Position = 0;

            var pic = ws.AddPicture(ms, $"pic_{picIndex}_{row}_{col}");
            pic.MoveTo(ws.Cell(row, col), 4, 4);
            FitPictureToSize(pic, 120, 90);
        }
        catch { /* 画像追加エラー時はスキップ */ }
    }

    private static void FitPictureToSize(ClosedXML.Excel.Drawings.IXLPicture pic, int maxWidth, int maxHeight)
    {
        if (pic.OriginalWidth <= 0 || pic.OriginalHeight <= 0) return;

        double wRatio = (double)maxWidth / pic.OriginalWidth;
        double hRatio = (double)maxHeight / pic.OriginalHeight;
        double ratio = Math.Min(wRatio, hRatio);

        if (ratio < 1.0)
        {
            pic.Width = (int)(pic.OriginalWidth * ratio);
            pic.Height = (int)(pic.OriginalHeight * ratio);
        }
        else
        {
            pic.Width = pic.OriginalWidth;
            pic.Height = pic.OriginalHeight;
        }
    }

    public static void SaveBitmapToPngStream(BitmapSource source, Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    public static void SaveBitmapToPngFile(BitmapSource source, string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        SaveBitmapToPngStream(source, fs);
    }

    private static string EscapeMd(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
    }

    private static string EscapeMdMultiline(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
    }
}
