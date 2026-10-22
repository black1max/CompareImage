using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompareImage.Services;

public class PreprocessOptions
{
    public bool Enabled { get; set; } = false;
    public bool ToGrayscale { get; set; } = true;
    public bool EnhanceContrast { get; set; } = false;
    public bool Binarize { get; set; } = false;
    public int BinarizeThreshold { get; set; } = 128; // 0-255
    public bool Sharpen { get; set; } = false;

    [JsonIgnore]
    public bool HasAnyOperation => Enabled && (ToGrayscale || EnhanceContrast || Binarize || Sharpen);

    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            if (!Enabled) return "なし";
            var ops = new List<string>();
            if (EnhanceContrast) ops.Add("コントラスト");
            if (Binarize) ops.Add($"二値化({BinarizeThreshold})");
            else if (ToGrayscale) ops.Add("グレー");
            if (Sharpen) ops.Add("輪郭");
            return ops.Count > 0 ? string.Join("+", ops) : "なし";
        }
    }

    public PreprocessOptions Clone() => new()
    {
        Enabled = this.Enabled,
        ToGrayscale = this.ToGrayscale,
        EnhanceContrast = this.EnhanceContrast,
        Binarize = this.Binarize,
        BinarizeThreshold = this.BinarizeThreshold,
        Sharpen = this.Sharpen
    };
}

public class ImagePreprocessingService
{
    /// <summary>
    /// 指定された前処理オプションに基づいて BitmapSource を加工する
    /// </summary>
    public static BitmapSource ApplyPreprocessing(BitmapSource source, PreprocessOptions options)
    {
        if (!options.HasAnyOperation) return source;

        int width = source.PixelWidth;
        int height = source.PixelHeight;

        // Bgra32 に統一
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        // 1. コントラスト強調
        if (options.EnhanceContrast)
        {
            ApplyContrastEnhancement(pixels);
        }

        // 2. グレースケール化
        if (options.ToGrayscale || options.Binarize)
        {
            ApplyGrayscale(pixels);
        }

        // 3. 二値化 (白黒しきい値処理)
        if (options.Binarize)
        {
            ApplyBinarization(pixels, (byte)Math.Clamp(options.BinarizeThreshold, 0, 255));
        }

        // 4. シャープネス (輪郭強調)
        if (options.Sharpen)
        {
            pixels = ApplySharpenFilter(pixels, width, height, stride);
        }

        // 処理後のバイト列から BitmapSource を再構成
        var result = BitmapSource.Create(
            width, height,
            source.DpiX, source.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static void ApplyGrayscale(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            // 輝度 Y = 0.299 R + 0.587 G + 0.114 B
            byte gray = (byte)((r * 299 + g * 587 + b * 114) / 1000);

            pixels[i] = gray;     // B
            pixels[i + 1] = gray; // G
            pixels[i + 2] = gray; // R
            // Alpha (i + 3) は維持
        }
    }

    private static void ApplyBinarization(byte[] pixels, byte threshold)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte gray = pixels[i]; // すでにグレースケール化されている前提
            byte val = gray >= threshold ? (byte)255 : (byte)0;

            pixels[i] = val;
            pixels[i + 1] = val;
            pixels[i + 2] = val;
        }
    }

    private static void ApplyContrastEnhancement(byte[] pixels)
    {
        // 最小値と最大値をスキャン
        byte min = 255;
        byte max = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte gray = (byte)((pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000);
            if (gray < min) min = gray;
            if (gray > max) max = gray;
        }

        if (max <= min) return;

        double scale = 255.0 / (max - min);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                int val = (int)((pixels[i + c] - min) * scale);
                pixels[i + c] = (byte)Math.Clamp(val, 0, 255);
            }
        }
    }

    private static byte[] ApplySharpenFilter(byte[] pixels, int width, int height, int stride)
    {
        byte[] output = new byte[pixels.Length];
        Array.Copy(pixels, output, pixels.Length);

        // 3x3 アンシャープマスク カーネル:
        //  0, -1,  0
        // -1,  5, -1
        //  0, -1,  0
        for (int y = 1; y < height - 1; y++)
        {
            int rowPrev = (y - 1) * stride;
            int rowCurr = y * stride;
            int rowNext = (y + 1) * stride;

            for (int x = 1; x < width - 1; x++)
            {
                int idx = rowCurr + x * 4;

                for (int c = 0; c < 3; c++)
                {
                    int top = pixels[rowPrev + x * 4 + c];
                    int bottom = pixels[rowNext + x * 4 + c];
                    int left = pixels[rowCurr + (x - 1) * 4 + c];
                    int right = pixels[rowCurr + (x + 1) * 4 + c];
                    int center = pixels[idx + c];

                    int val = 5 * center - top - bottom - left - right;
                    output[idx + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        }

        return output;
    }
}
