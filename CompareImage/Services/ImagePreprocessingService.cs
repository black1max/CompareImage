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

    /// <summary>
    /// OCR認識に特化した超解像拡大・大津二値化・自動白黒反転・太線化パイプライン
    /// </summary>
    public static BitmapSource OptimizeForOcr(BitmapSource cropped)
    {
        int srcW = cropped.PixelWidth;
        int srcH = cropped.PixelHeight;

        // 1. 3.5〜4倍アップスケール (文字高を最低 100px 以上に拡大)
        double scale = Math.Clamp(Math.Max(120.0 / srcH, 3.5), 1.0, 5.0);
        BitmapSource scaled = (Math.Abs(scale - 1.0) > 0.01)
            ? new TransformedBitmap(cropped, new ScaleTransform(scale, scale))
            : cropped;

        // 2. 白パディング追加 (周囲 20px)
        int pad = 20;
        int padW = scaled.PixelWidth + pad * 2;
        int padH = scaled.PixelHeight + pad * 2;

        var rtb = new RenderTargetBitmap(padW, padH, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, padW, padH));
            dc.DrawImage(scaled, new System.Windows.Rect(pad, pad, scaled.PixelWidth, scaled.PixelHeight));
        }
        rtb.Render(dv);
        rtb.Freeze();

        // 3. ピクセル配列 (Bgra32) の取得
        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        int stride = padW * 4;
        byte[] bgraPixels = new byte[stride * padH];
        converted.CopyPixels(bgraPixels, stride, 0);

        // 4. グレースケール配列の作成
        int totalPixels = padW * padH;
        byte[] gray = new byte[totalPixels];
        for (int i = 0; i < totalPixels; i++)
        {
            int idx = i * 4;
            gray[i] = (byte)((bgraPixels[idx + 2] * 299 + bgraPixels[idx + 1] * 587 + bgraPixels[idx] * 114) / 1000);
        }

        // 5. 外周の平均輝度から「白抜き文字 (暗い背景に明るい文字)」を自動判定
        long borderSum = 0;
        int borderCount = 0;
        for (int x = 0; x < padW; x++)
        {
            borderSum += gray[x];                       // 上端
            borderSum += gray[(padH - 1) * padW + x];  // 下端
            borderCount += 2;
        }
        for (int y = 0; y < padH; y++)
        {
            borderSum += gray[y * padW];               // 左端
            borderSum += gray[y * padW + padW - 1];    // 右端
            borderCount += 2;
        }
        bool isDarkBackground = (borderSum / Math.Max(1, borderCount)) < 128;

        // 6. 大津の二値化閾値を自動計算
        byte threshold = CalculateOtsuThreshold(gray);

        // 7. 二値化 (必ず「文字=黒(0)」「背景=白(255)」に正規化)
        byte[] binary = new byte[totalPixels];
        for (int i = 0; i < totalPixels; i++)
        {
            if (isDarkBackground)
            {
                // 暗い背景に明るい文字 -> 閾値以上が文字(黒)
                binary[i] = gray[i] >= threshold ? (byte)0 : (byte)255;
            }
            else
            {
                // 明るい背景に暗い文字 -> 閾値未満が文字(黒)
                binary[i] = gray[i] < threshold ? (byte)0 : (byte)255;
            }
        }

        // 8. 文字の細線補強 (アンチエイリアスによるかすれ・途切れ解消のモルフォロジー膨張)
        byte[] dilated = new byte[totalPixels];
        Array.Copy(binary, dilated, totalPixels);
        for (int y = 1; y < padH - 1; y++)
        {
            int row = y * padW;
            for (int x = 1; x < padW - 1; x++)
            {
                if (binary[row + x] == 0 ||
                    binary[row - padW + x] == 0 ||
                    binary[row + padW + x] == 0 ||
                    binary[row + x - 1] == 0 ||
                    binary[row + x + 1] == 0)
                {
                    dilated[row + x] = 0;
                }
            }
        }

        // 9. Bgra32 ピクセル配列へ反映
        for (int i = 0; i < totalPixels; i++)
        {
            int idx = i * 4;
            byte v = dilated[i];
            bgraPixels[idx] = v;     // B
            bgraPixels[idx + 1] = v; // G
            bgraPixels[idx + 2] = v; // R
            bgraPixels[idx + 3] = 255; // A
        }

        var result = BitmapSource.Create(padW, padH, 96, 96, PixelFormats.Bgra32, null, bgraPixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// 大津の二値化 (クラス間分散を最大化する最適閾値を自動計算)
    /// </summary>
    public static byte CalculateOtsuThreshold(byte[] grayscalePixels)
    {
        int[] hist = new int[256];
        int total = grayscalePixels.Length;

        for (int i = 0; i < total; i++)
        {
            hist[grayscalePixels[i]]++;
        }

        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * hist[i];

        double sumB = 0;
        int wB = 0;
        double maxVariance = 0;
        byte bestThreshold = 128;

        for (int t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            int wF = total - wB;
            if (wF == 0) break;

            sumB += t * hist[t];
            double mB = sumB / wB;
            double mF = (sum - sumB) / wF;

            double betweenVariance = (double)wB * wF * (mB - mF) * (mB - mF);
            if (betweenVariance > maxVariance)
            {
                maxVariance = betweenVariance;
                bestThreshold = (byte)t;
            }
        }

        return bestThreshold;
    }
}
