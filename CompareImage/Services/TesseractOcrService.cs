using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CompareImage.Services;

public class TesseractOcrService : IOcrService
{
    public string EngineName => "Tesseract OCR";
    public string? ExePath { get; private set; }
    public bool IsAvailable => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);
    public string StatusMessage => IsAvailable
        ? $"利用可能 ({ExePath})"
        : "未インストール (tesseract.exe が見つかりません)";

    public TesseractOcrService()
    {
        DetectTesseract();
    }

    /// <summary>
    /// システム上にインストールされている tesseract.exe を探索する
    /// </summary>
    public bool DetectTesseract()
    {
        ExePath = FindTesseractPath();
        return IsAvailable;
    }

    private static string? FindTesseractPath()
    {
        // 0. アプリケーション実行ファイル (exe) のフォルダー配下を最優先で探索 (ポータブル配置)
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] localCandidates =
        {
            Path.Combine(appDir, "Tesseract", "tesseract.exe"),
            Path.Combine(appDir, "Tesseract-OCR", "tesseract.exe"),
            Path.Combine(appDir, "tesseract.exe")
        };

        foreach (var c in localCandidates)
        {
            if (File.Exists(c)) return c;
        }

        // 1. 環境変数 PATH から検索
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paths)
            {
                try
                {
                    var full = Path.Combine(p.Trim(), "tesseract.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }

        // 2. 代表的な標準インストール先
        string[] candidates =
        {
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Tesseract-OCR\tesseract.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public async Task<string> RecognizeCroppedRegionAsync(BitmapSource source, int x, int y, int width, int height, bool isNumberMode = false)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Tesseract OCR がインストールされていないか、見つかりません。");
        }

        // 範囲クリッピング
        int srcWidth = source.PixelWidth;
        int srcHeight = source.PixelHeight;

        int safeX = Math.Clamp(x, 0, Math.Max(0, srcWidth - 1));
        int safeY = Math.Clamp(y, 0, Math.Max(0, srcHeight - 1));
        int safeWidth = Math.Clamp(width, 1, Math.Max(1, srcWidth - safeX));
        int safeHeight = Math.Clamp(height, 1, Math.Max(1, srcHeight - safeY));

        var cropRect = new Int32Rect(safeX, safeY, safeWidth, safeHeight);
        var cropped = new CroppedBitmap(source, cropRect);

        // OCR認識精度向上のための超解像拡大・大津二値化・自動反転・太線化パイプライン
        var optimized = ImagePreprocessingService.OptimizeForOcr(cropped);

        // 一時 PNG ファイルとして保存
        string tempInput = Path.Combine(Path.GetTempPath(), $"tess_in_{Guid.NewGuid():N}.png");

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(optimized));
            using (var fs = new FileStream(tempInput, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }

            // tesseract.exe を非同期で起動 (PSM 7: 単一行 / PSM 6: 複数行 の2段階試行)
            return await Task.Run(() =>
            {
                // 1. まず単一行モード (PSM 7) で試行 (画面キャプチャの項目・数値に最も適している)
                string result = ExecuteTesseract(tempInput, psm: 7, isNumberMode: isNumberMode);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }

                // 2. 認識できなかった場合は複数行ブロックモード (PSM 6) で再試行
                result = ExecuteTesseract(tempInput, psm: 6, isNumberMode: isNumberMode);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }

                // 3. それでも空で数値モードなら PSM 8 (単一単語) で試行
                if (isNumberMode)
                {
                    result = ExecuteTesseract(tempInput, psm: 8, isNumberMode: true);
                }

                return result;
            });
        }
        finally
        {
            if (File.Exists(tempInput))
            {
                try { File.Delete(tempInput); } catch { }
            }
        }
    }

    private string ExecuteTesseract(string tempInput, int psm, bool isNumberMode)
    {
        string whitelistArg = isNumberMode ? " -c tessedit_char_whitelist=0123456789.+-" : "";
        string lang = "jpn+eng";

        var psi = new ProcessStartInfo
        {
            FileName = ExePath!,
            Arguments = $"\"{tempInput}\" stdout -l {lang} --psm {psm}{whitelistArg}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        ConfigureTessdataPrefix(psi);

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();

        bool finished = process.WaitForExit(5000);
        if (!finished)
        {
            try { process.Kill(); } catch { }
            return string.Empty;
        }

        if (process.ExitCode != 0)
        {
            // 日本語言語データが無い場合は eng 単体で再試行
            string err = process.StandardError.ReadToEnd();
            if (err.Contains("jpn") || err.Contains("traineddata"))
            {
                return RunTesseractEngOnly(tempInput, psm, isNumberMode);
            }
        }

        return outputBuilder.ToString().Trim();
    }

    private string RunTesseractEngOnly(string tempInput, int psm = 7, bool isNumberMode = false)
    {
        string whitelistArg = isNumberMode ? " -c tessedit_char_whitelist=0123456789.+-" : "";
        var psi = new ProcessStartInfo
        {
            FileName = ExePath!,
            Arguments = $"\"{tempInput}\" stdout -l eng --psm {psm}{whitelistArg}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        ConfigureTessdataPrefix(psi);

        using var process = Process.Start(psi);
        if (process == null) return string.Empty;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        return output.Trim();
    }

    private void ConfigureTessdataPrefix(ProcessStartInfo psi)
    {
        if (string.IsNullOrEmpty(ExePath)) return;
        string? exeDir = Path.GetDirectoryName(ExePath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            string tessdataDir = Path.Combine(exeDir, "tessdata");
            if (Directory.Exists(tessdataDir))
            {
                psi.EnvironmentVariables["TESSDATA_PREFIX"] = exeDir;
            }
        }
    }
}
