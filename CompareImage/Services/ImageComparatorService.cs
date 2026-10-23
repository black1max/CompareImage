using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CompareImage.Models;

namespace CompareImage.Services;

public class ImageComparatorService
{
    public IOcrService CurrentOcrService { get; set; }

    public ImageComparatorService(IOcrService ocrService)
    {
        CurrentOcrService = ocrService;
    }

    public static string NormalizeText(string text, bool ignoreWhitespace = true, bool caseSensitive = false, bool numbersOnly = false)
    {
        string result = text.Trim();

        if (numbersOnly)
        {
            var matches = Regex.Matches(result, @"[-+]?\d+(?:\.\d+)?");
            var numbers = new List<string>();
            foreach (Match m in matches)
            {
                numbers.Add(m.Value);
            }
            return string.Join("", numbers);
        }

        if (ignoreWhitespace)
        {
            result = Regex.Replace(result, @"\s+", "");
        }

        if (!caseSensitive)
        {
            result = result.ToLowerInvariant();
        }

        return result;
    }

    /// <summary>
    /// 数値比較用: 全角を半角化し、0～9、小数点、プラス符号、マイナス符号のみを抽出する
    /// </summary>
    public static string NormalizeNumberText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
            else if (c == '＋') sb.Append('+');
            else if (c == 'ー' || c == '−' || c == '―' || c == '‐') sb.Append('-');
            else if (c == '．') sb.Append('.');
            else if (c == '，' || c == ',') continue; // 桁区切りカンマは除外
            else sb.Append(c);
        }

        string converted = sb.ToString();
        var filtered = new System.Text.StringBuilder();
        foreach (char c in converted)
        {
            if ((c >= '0' && c <= '9') || c == '.' || c == '+' || c == '-')
            {
                filtered.Append(c);
            }
        }

        string result = filtered.ToString();

        // ピリオドが複数存在する場合 (OCR が桁区切りカンマ ',' を '.' と誤認した場合の補正)
        // 最後のピリオドを小数点とし、それより前にあるピリオドは桁区切りとして除去
        int lastDotIndex = result.LastIndexOf('.');
        if (lastDotIndex >= 0 && result.IndexOf('.') != lastDotIndex)
        {
            var clean = new System.Text.StringBuilder();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == '.' && i != lastDotIndex)
                {
                    continue; // 最後のピリオド以外は除去
                }
                clean.Append(result[i]);
            }
            result = clean.ToString();
        }

        return result;
    }

    /// <summary>
    /// 数値文字列同士の一致判定 (文字列一致または浮動小数点数値としての一致)
    /// </summary>
    public static bool CompareNumberValues(string numA, string numB)
    {
        // どちらか一方でも空文字の場合は未認識として不一致
        if (string.IsNullOrWhiteSpace(numA) || string.IsNullOrWhiteSpace(numB)) return false;
        if (string.Equals(numA, numB, StringComparison.Ordinal)) return true;

        if (double.TryParse(numA, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double valA) &&
            double.TryParse(numB, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double valB))
        {
            return Math.Abs(valA - valB) < 1e-9;
        }

        return false;
    }

    public async Task<PairCompareResult> CompareSinglePairAsync(
        ImagePair pair,
        RoiConfig roiConfig,
        bool ignoreWhitespace = true,
        bool caseSensitive = false,
        bool numbersOnly = false,
        PreprocessOptions? preprocessOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 画像読み込み
        var baseBmp = LoadBitmapSource(pair.BaseImagePath);
        var targetBmp = LoadBitmapSource(pair.TargetImagePath);

        var regionResults = new List<RegionCompareResult>();
        bool allMatched = true;

        for (int i = 0; i < roiConfig.BaseImageRoi.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bRoi = roiConfig.BaseImageRoi[i];
            var tRoi = roiConfig.TargetImageRoi[i];

            // 1. 各領域をクロップ
            var bCropped = CropBitmap(baseBmp, bRoi.X, bRoi.Y, bRoi.Width, bRoi.Height);
            var tCropped = CropBitmap(targetBmp, tRoi.X, tRoi.Y, tRoi.Width, tRoi.Height);

            // 2. 領域ID毎の前処理を適用 (領域固有設定を優先、未設定時はグローバル前処理)
            var bPrep = (bRoi.Preprocess?.HasAnyOperation == true) ? bRoi.Preprocess : preprocessOptions;
            if (bPrep != null && bPrep.HasAnyOperation)
            {
                bCropped = ImagePreprocessingService.ApplyPreprocessing(bCropped, bPrep);
            }

            var tPrep = (tRoi.Preprocess?.HasAnyOperation == true) ? tRoi.Preprocess : preprocessOptions;
            if (tPrep != null && tPrep.HasAnyOperation)
            {
                tCropped = ImagePreprocessingService.ApplyPreprocessing(tCropped, tPrep);
            }

            if (bRoi.CompareType == RoiCompareType.Image)
            {
                // 画像（ピクセル）比較
                var (isMatch, bDesc, tDesc) = CompareCroppedBitmaps(bCropped, tCropped);
                if (!isMatch)
                {
                    allMatched = false;
                }

                regionResults.Add(new RegionCompareResult
                {
                    RegionId = bRoi.Id,
                    RegionName = bRoi.Name,
                    CompareType = RoiCompareType.Image,
                    BaseTextRaw = bDesc,
                    TargetTextRaw = tDesc,
                    BaseTextNormalized = isMatch ? "MATCH" : "DIFF",
                    TargetTextNormalized = isMatch ? "MATCH" : "DIFF",
                    IsMatch = isMatch,
                    BaseRoi = bRoi,
                    TargetRoi = tRoi
                });
            }
            else if (bRoi.CompareType == RoiCompareType.Number)
            {
                // 数値比較 (0～9, 小数点, +, -) - isNumberMode: true で専用ホワイトリストを適用
                string bRaw = await CurrentOcrService.RecognizeCroppedRegionAsync(bCropped, 0, 0, bCropped.PixelWidth, bCropped.PixelHeight, isNumberMode: true);
                string tRaw = await CurrentOcrService.RecognizeCroppedRegionAsync(tCropped, 0, 0, tCropped.PixelWidth, tCropped.PixelHeight, isNumberMode: true);

                string bNorm = NormalizeNumberText(bRaw);
                string tNorm = NormalizeNumberText(tRaw);

                bool isMatch = CompareNumberValues(bNorm, tNorm);
                if (!isMatch)
                {
                    allMatched = false;
                }

                regionResults.Add(new RegionCompareResult
                {
                    RegionId = bRoi.Id,
                    RegionName = bRoi.Name,
                    CompareType = RoiCompareType.Number,
                    BaseTextRaw = bRaw.Trim(),
                    TargetTextRaw = tRaw.Trim(),
                    BaseTextNormalized = bNorm,
                    TargetTextNormalized = tNorm,
                    IsMatch = isMatch,
                    BaseRoi = bRoi,
                    TargetRoi = tRoi
                });
            }
            else
            {
                // 文字列（OCR）比較 (前処理済みクロップ領域からテキスト認識)
                string bRaw = await CurrentOcrService.RecognizeCroppedRegionAsync(bCropped, 0, 0, bCropped.PixelWidth, bCropped.PixelHeight);
                string tRaw = await CurrentOcrService.RecognizeCroppedRegionAsync(tCropped, 0, 0, tCropped.PixelWidth, tCropped.PixelHeight);

                string bNorm = NormalizeText(bRaw, ignoreWhitespace, caseSensitive, numbersOnly);
                string tNorm = NormalizeText(tRaw, ignoreWhitespace, caseSensitive, numbersOnly);

                // どちらか一方でも空文字の場合は未認識として不一致
                bool isMatch = !string.IsNullOrEmpty(bNorm) && !string.IsNullOrEmpty(tNorm) && string.Equals(bNorm, tNorm, StringComparison.Ordinal);
                if (!isMatch)
                {
                    allMatched = false;
                }

                regionResults.Add(new RegionCompareResult
                {
                    RegionId = bRoi.Id,
                    RegionName = bRoi.Name,
                    CompareType = RoiCompareType.Text,
                    BaseTextRaw = bRaw.Trim(),
                    TargetTextRaw = tRaw.Trim(),
                    BaseTextNormalized = bNorm,
                    TargetTextNormalized = tNorm,
                    IsMatch = isMatch,
                    BaseRoi = bRoi,
                    TargetRoi = tRoi
                });
            }
        }

        return new PairCompareResult
        {
            PairName = pair.PairName,
            BaseImagePath = pair.BaseImagePath,
            TargetImagePath = pair.TargetImagePath,
            AllMatched = allMatched,
            RegionResults = regionResults
        };
    }

    /// <summary>
    /// クロップ領域同士のピクセル比較を行う
    /// </summary>
    public static (bool isMatch, string baseDesc, string targetDesc) CompareCroppedImagePixels(
        BitmapSource baseBmp, RoiItem bRoi, BitmapSource targetBmp, RoiItem tRoi)
    {
        var bCropped = CropBitmap(baseBmp, bRoi.X, bRoi.Y, bRoi.Width, bRoi.Height);
        var tCropped = CropBitmap(targetBmp, tRoi.X, tRoi.Y, tRoi.Width, tRoi.Height);
        return CompareCroppedBitmaps(bCropped, tCropped);
    }

    /// <summary>
    /// 切り抜き済みBitmap同士のピクセル比較を行う
    /// </summary>
    public static (bool isMatch, string baseDesc, string targetDesc) CompareCroppedBitmaps(
        BitmapSource bCropped, BitmapSource tCropped)
    {

        int bW = bCropped.PixelWidth;
        int bH = bCropped.PixelHeight;
        int tW = tCropped.PixelWidth;
        int tH = tCropped.PixelHeight;

        string baseDesc = $"領域サイズ: {bW}×{bH} px";

        if (bW != tW || bH != tH)
        {
            return (false, baseDesc, $"サイズ不一致: {tW}×{tH} px");
        }

        // Bgra32に統一してピクセルバイト列を取得
        var bConverted = new FormatConvertedBitmap(bCropped, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var tConverted = new FormatConvertedBitmap(tCropped, System.Windows.Media.PixelFormats.Bgra32, null, 0);

        int stride = bW * 4;
        byte[] bPixels = new byte[stride * bH];
        byte[] tPixels = new byte[stride * tH];

        bConverted.CopyPixels(bPixels, stride, 0);
        tConverted.CopyPixels(tPixels, stride, 0);

        int totalPixels = bW * bH;
        int diffPixels = 0;

        for (int i = 0; i < bPixels.Length; i += 4)
        {
            // B, G, R, A 比較 (わずかなレンダリング差は誤差吸収するため差分 > 2 で判定)
            int bDiff = Math.Abs(bPixels[i] - tPixels[i]);
            int gDiff = Math.Abs(bPixels[i + 1] - tPixels[i + 1]);
            int rDiff = Math.Abs(bPixels[i + 2] - tPixels[i + 2]);
            int aDiff = Math.Abs(bPixels[i + 3] - tPixels[i + 3]);

            if (bDiff > 2 || gDiff > 2 || rDiff > 2 || aDiff > 2)
            {
                diffPixels++;
            }
        }

        if (diffPixels == 0)
        {
            return (true, baseDesc, "画像完全一致 (差異 0.00% / 0 px)");
        }
        else
        {
            double diffRatio = (double)diffPixels / totalPixels * 100.0;
            return (false, baseDesc, $"画像差異: {diffRatio:F2}% ({diffPixels}/{totalPixels} px)");
        }
    }

    public static BitmapSource CropBitmap(BitmapSource source, int x, int y, int width, int height)
    {
        int srcWidth = source.PixelWidth;
        int srcHeight = source.PixelHeight;

        int safeX = Math.Clamp(x, 0, Math.Max(0, srcWidth - 1));
        int safeY = Math.Clamp(y, 0, Math.Max(0, srcHeight - 1));
        int safeWidth = Math.Clamp(width, 1, Math.Max(1, srcWidth - safeX));
        int safeHeight = Math.Clamp(height, 1, Math.Max(1, srcHeight - safeY));

        var cropped = new CroppedBitmap(source, new System.Windows.Int32Rect(safeX, safeY, safeWidth, safeHeight));
        cropped.Freeze();
        return cropped;
    }

    public static BitmapSource LoadBitmapSource(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path, UriKind.Absolute);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
