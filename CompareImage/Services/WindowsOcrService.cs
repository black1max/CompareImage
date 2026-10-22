using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CompareImage.Services;

public class WindowsOcrService : IOcrService
{
    private OcrEngine? _engine;

    public string EngineName => "Windows 11 標準 OCR (WinRT)";
    public bool IsAvailable => _engine != null;
    public string StatusMessage
    {
        get
        {
            if (_engine != null)
            {
                return $"利用可能 (言語: {_engine.RecognizerLanguage.DisplayName} [{_engine.RecognizerLanguage.LanguageTag}])";
            }
            return "WinRT OCRエンジンが初期化できませんでした (OCR言語パックが未導入の可能性があります)";
        }
    }

    public WindowsOcrService()
    {
        InitializeEngine();
    }

    /// <summary>
    /// OCR エンジンを初期化する (ユーザー言語 -> 日本語 -> インストール済み言語の順にフォールバック)
    /// </summary>
    public bool InitializeEngine()
    {
        try
        {
            // 1. ユーザープロファイル言語から生成
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();

            // 2. 失敗した場合は、日本語 (ja) を優先して探索
            if (_engine == null)
            {
                var jaLang = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(l => l.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
                if (jaLang != null)
                {
                    _engine = OcrEngine.TryCreateFromLanguage(jaLang);
                }
            }

            // 3. それでも null の場合は利用可能な任意の言語を採用
            if (_engine == null)
            {
                var anyLang = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (anyLang != null)
                {
                    _engine = OcrEngine.TryCreateFromLanguage(anyLang);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OcrEngine初期化エラー: {ex.Message}");
            _engine = null;
        }

        return IsAvailable;
    }

    /// <summary>
    /// 画像ファイル全体からテキストをOCR認識する
    /// </summary>
    public async Task<string> RecognizeFileAsync(string filePath)
    {
        EnsureEngineAvailable();

        using var fileStream = File.OpenRead(filePath);
        using var memStream = new MemoryStream();
        await fileStream.CopyToAsync(memStream);
        return await RecognizeBytesAsync(memStream.ToArray());
    }

    /// <summary>
    /// BitmapSource の指定範囲(X, Y, Width, Height)をクロップし、OCR認識する
    /// </summary>
    public async Task<string> RecognizeCroppedRegionAsync(BitmapSource source, int x, int y, int width, int height)
    {
        EnsureEngineAvailable();

        // 範囲クリッピング
        int srcWidth = source.PixelWidth;
        int srcHeight = source.PixelHeight;

        int safeX = Math.Clamp(x, 0, Math.Max(0, srcWidth - 1));
        int safeY = Math.Clamp(y, 0, Math.Max(0, srcHeight - 1));
        int safeWidth = Math.Clamp(width, 1, Math.Max(1, srcWidth - safeX));
        int safeHeight = Math.Clamp(height, 1, Math.Max(1, srcHeight - safeY));

        var cropRect = new Int32Rect(safeX, safeY, safeWidth, safeHeight);
        var cropped = new CroppedBitmap(source, cropRect);

        // OCR認識精度向上のためのパディングと拡大処理
        // WinRT OCR は境界ギリギリに文字があると文字セグメンテーションが失敗しやすいため、
        // 解像度を確保しつつ白背景の余白を必ず設ける
        int pad = 12;
        double scale = 1.0;
        if (safeHeight < 80 || safeWidth < 80)
        {
            scale = Math.Max(80.0 / safeHeight, Math.Max(80.0 / safeWidth, 2.0));
        }

        BitmapSource scaledBitmap = (Math.Abs(scale - 1.0) > 0.01)
            ? new TransformedBitmap(cropped, new ScaleTransform(scale, scale))
            : cropped;

        int paddedW = scaledBitmap.PixelWidth + pad * 2;
        int paddedH = scaledBitmap.PixelHeight + pad * 2;

        var rtb = new RenderTargetBitmap(paddedW, paddedH, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 白背景描画
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, paddedW, paddedH));
            // スケールしたクロップ画像を中央に配置
            dc.DrawImage(scaledBitmap, new Rect(pad, pad, scaledBitmap.PixelWidth, scaledBitmap.PixelHeight));
        }
        rtb.Render(dv);
        rtb.Freeze();

        // PNGバイト列に変換
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        return await RecognizeBytesAsync(ms.ToArray());
    }

    /// <summary>
    /// 画像全体からテキスト領域を自動検出し、ROIリストとして返す
    /// </summary>
    public async Task<List<CompareImage.Models.RoiItem>> DetectTextRegionsAsync(BitmapSource source, int padding = 4)
    {
        EnsureEngineAvailable();

        // PNGバイト列に変換
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        using var randomAccessStream = new InMemoryRandomAccessStream();
        await randomAccessStream.WriteAsync(ms.ToArray().AsBuffer());
        randomAccessStream.Seek(0);

        // OcrEngine に適合する Bgra8 / Premultiplied に明示指定してデコード
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var ocrResult = await _engine!.RecognizeAsync(softwareBitmap);
        var rois = new List<CompareImage.Models.RoiItem>();

        int id = 1;
        int imgW = source.PixelWidth;
        int imgH = source.PixelHeight;

        foreach (var line in ocrResult.Lines)
        {
            if (line.Words.Count == 0) continue;

            // 行内の全単語矩形を包含する外接矩形を計算
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                if (rect.X < minX) minX = rect.X;
                if (rect.Y < minY) minY = rect.Y;
                if (rect.X + rect.Width > maxX) maxX = rect.X + rect.Width;
                if (rect.Y + rect.Height > maxY) maxY = rect.Y + rect.Height;
            }

            // 余白パディングの付与とクリッピング
            int x = Math.Clamp((int)Math.Floor(minX - padding), 0, imgW - 1);
            int y = Math.Clamp((int)Math.Floor(minY - padding), 0, imgH - 1);
            int w = Math.Clamp((int)Math.Ceiling(maxX - minX + padding * 2), 1, imgW - x);
            int h = Math.Clamp((int)Math.Ceiling(maxY - minY + padding * 2), 1, imgH - y);

            string name = line.Text.Trim();
            if (name.Length > 20) name = name.Substring(0, 20) + "...";
            if (string.IsNullOrEmpty(name)) name = $"自動検出_{id}";

            rois.Add(new CompareImage.Models.RoiItem
            {
                Id = id++,
                Name = name,
                CompareType = CompareImage.Models.RoiCompareType.Text,
                X = x,
                Y = y,
                Width = w,
                Height = h
            });
        }

        return rois;
    }

    private async Task<string> RecognizeBytesAsync(byte[] imageBytes)
    {
        EnsureEngineAvailable();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        await randomAccessStream.WriteAsync(imageBytes.AsBuffer());
        randomAccessStream.Seek(0);

        // OcrEngine に適合する Bgra8 / Premultiplied に明示指定してデコード
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await _engine!.RecognizeAsync(softwareBitmap);
        return result.Text;
    }

    private void EnsureEngineAvailable()
    {
        if (_engine == null)
        {
            InitializeEngine();
            if (_engine == null)
            {
                int count = OcrEngine.AvailableRecognizerLanguages.Count;
                throw new InvalidOperationException(
                    $"Windows 11 OCR エンジンを初期化できませんでした。\n" +
                    $"インストールされている認識言語数: {count}\n" +
                    "Windowsの設定「時刻と言語」>「言語」で日本語または英語の言語パック（OCR）が導入されているか確認してください。");
            }
        }
    }
}
