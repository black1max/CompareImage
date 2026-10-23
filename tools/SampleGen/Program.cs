using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace SampleGen;

class Program
{
    record SampleItem(string Text, string Code, string Num, string FontNameText, string FontNameCode, string FontNameNum, string IconShape);
    record SamplePair(string FileName, string Title, SampleItem Base, SampleItem Target);

    static void Main()
    {
        // ルートの sample_image ディレクトリを確実に特定
        string current = AppContext.BaseDirectory;
        string rootDir = "";
        for (int i = 0; i < 6; i++)
        {
            if (Directory.Exists(Path.Combine(current, "sample_image")))
            {
                rootDir = current;
                break;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        if (string.IsNullOrEmpty(rootDir))
        {
            rootDir = Directory.GetCurrentDirectory();
        }

        string baseDir = Path.Combine(rootDir, "sample_image", "Base");
        string targetDir = Path.Combine(rootDir, "sample_image", "Target");

        Console.WriteLine($"Output Base Dir: {baseDir}");
        Console.WriteLine($"Output Target Dir: {targetDir}");

        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(targetDir);

        // 明朝体 vs メイリオ、Courier New vs Segoe UI など、見た目の違いが明確なフォントを選択
        var pairs = new[]
        {
            new SamplePair(
                "sample_001.png",
                "注文伝票 #001 (標準検証)",
                // Base: 明朝体 / 等幅 / ゴシック
                new SampleItem("プレミアム珈琲豆 500g", "+2026.1023", "¥ 4,980.50", "MS Mincho", "Courier New", "MS Gothic", "Check"),
                // Target: メイリオ / Segoe UI / Segoe UI
                new SampleItem("プレミアム珈琲豆 500g", "+2026.1023", "+4980.50", "Meiryo", "Segoe UI", "Segoe UI", "Check")
            ),
            new SamplePair(
                "sample_002.png",
                "注文伝票 #002 (マイナス符号)",
                new SampleItem("ワイヤレス充電器 Qi2", "-150.00", "32800", "MS Mincho", "Courier New", "MS Gothic", "Star"),
                new SampleItem("ワイヤレス充電器 Qi2", "-150.00", "32800", "Yu Gothic UI", "Arial", "Segoe UI", "Star")
            ),
            new SamplePair(
                "sample_003.png",
                "注文伝票 #003 (差異検知テスト)",
                new SampleItem("USB-C ドッキングステーション", "998877", "¥ 18,900", "MS Gothic", "Courier New", "MS Gothic", "Box"),
                new SampleItem("USB-C ハブ 7in1", "998877", "¥ 22,500", "Meiryo", "Segoe UI", "Segoe UI", "Box")
            ),
            new SamplePair(
                "sample_004.png",
                "注文伝票 #004 (符号・桁テスト)",
                new SampleItem("有機ELモニター 32型 4K", "+100.05", "-12.500", "MS Mincho", "Courier New", "MS Gothic", "Circle"),
                new SampleItem("有機ELモニター 32型 4K", "100.05", "-12.5", "Meiryo", "Segoe UI", "Arial", "Circle")
            ),
            new SamplePair(
                "sample_005.png",
                "注文伝票 #005 (全角半角正規化)",
                new SampleItem("エルゴノミクスマウス MX", "７８９０", "＋１２５．０", "MS Gothic", "MS Gothic", "MS Gothic", "Heart"),
                new SampleItem("エルゴノミクスマウス MX", "7890", "+125.0", "Segoe UI", "Arial", "Segoe UI", "Heart")
            )
        };

        foreach (var p in pairs)
        {
            string bPath = Path.Combine(baseDir, p.FileName);
            string tPath = Path.Combine(targetDir, p.FileName);

            DrawImage(bPath, p.Title, p.Base, isTarget: false);
            DrawImage(tPath, p.Title, p.Target, isTarget: true);

            Console.WriteLine($"Generated: {p.FileName}");
        }

        Console.WriteLine("All sample images generated successfully.");
    }

    static void DrawImage(string path, string title, SampleItem item, bool isTarget)
    {
        using var bmp = new Bitmap(640, 420, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);

        g.Clear(Color.FromArgb(245, 247, 250));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // ヘッダーバー (Base: 青系 #0078D7, Target: 緑系 #107C41)
        Color headerColor = isTarget ? Color.FromArgb(16, 124, 65) : Color.FromArgb(0, 120, 215);
        using (var hBrush = new SolidBrush(headerColor))
        {
            g.FillRectangle(hBrush, 0, 0, 640, 56);
        }

        using (var tFont = new Font("Meiryo UI", 13f, FontStyle.Bold))
        {
            string modeTag = isTarget ? "【Target 実績 (異なるフォント)】" : "【Base 期待値 (標準フォント)】";
            g.DrawString($"{title}  {modeTag}", tFont, Brushes.White, 16f, 16f);
        }

        using var cardPen = new Pen(Color.FromArgb(180, 190, 205), 1.5f);
        using var fieldBg = new SolidBrush(Color.White);
        using var labelFont = new Font("Meiryo UI", 9.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(70, 80, 95));
        using var fontTagBrush = new SolidBrush(Color.FromArgb(110, 120, 135));
        using var fontTagFont = new Font("Meiryo UI", 8.5f, FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(20, 20, 20));

        var sfRight = new StringFormat { Alignment = StringAlignment.Far };

        // 領域 1: 商品名 (X:40, Y:95, W:340, H:55) -> 文字列比較
        g.DrawString("商品名 [文字列比較]", labelFont, labelBrush, 40f, 74f);
        g.DrawString($"フォント: {item.FontNameText}", fontTagFont, fontTagBrush, new RectangleF(40, 75, 340, 20), sfRight);
        g.FillRectangle(fieldBg, 40, 95, 340, 55);
        g.DrawRectangle(cardPen, 40, 95, 340, 55);
        using (var f1 = SafeFont(item.FontNameText, 15f, FontStyle.Regular))
        {
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(item.Text, f1, textBrush, new RectangleF(48, 95, 324, 55), sf);
        }

        // 領域 2: 伝票コード (X:400, Y:95, W:200, H:55) -> 数値比較
        g.DrawString("伝票コード [数値]", labelFont, labelBrush, 400f, 74f);
        g.DrawString(item.FontNameCode, fontTagFont, fontTagBrush, new RectangleF(400, 75, 200, 20), sfRight);
        g.FillRectangle(fieldBg, 400, 95, 200, 55);
        g.DrawRectangle(cardPen, 400, 95, 200, 55);
        using (var f2 = SafeFont(item.FontNameCode, 15f, FontStyle.Bold))
        {
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(item.Code, f2, textBrush, new RectangleF(408, 95, 184, 55), sf);
        }

        // 領域 3: 金額・数量 (X:40, Y:185, W:340, H:55) -> 数値比較
        g.DrawString("金額・数量 [数値比較]", labelFont, labelBrush, 40f, 164f);
        g.DrawString($"フォント: {item.FontNameNum}", fontTagFont, fontTagBrush, new RectangleF(40, 165, 340, 20), sfRight);
        g.FillRectangle(fieldBg, 40, 185, 340, 55);
        g.DrawRectangle(cardPen, 40, 185, 340, 55);
        using (var f3 = SafeFont(item.FontNameNum, 16f, FontStyle.Bold))
        {
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(item.Num, f3, textBrush, new RectangleF(48, 185, 324, 55), sf);
        }

        // 領域 4: 認証アイコン (X:400, Y:185, W:100, H:100) -> 画像比較 (BaseとTargetで完全同一ピクセル)
        g.DrawString("認証アイコン [画像比較]", labelFont, labelBrush, 400f, 164f);
        g.FillRectangle(fieldBg, 400, 185, 100, 100);
        g.DrawRectangle(cardPen, 400, 185, 100, 100);
        DrawIcon(g, item.IconShape);

        // 下部フッター案内
        using var footFont = new Font("Meiryo UI", 9f);
        using var footBrush = new SolidBrush(Color.FromArgb(100, 110, 125));
        g.DrawString("※BaseとTargetでフォントが異なります。文字列・数値比較により論理一致を検証できます。", footFont, footBrush, 40f, 380f);

        bmp.Save(path, ImageFormat.Png);
    }

    static Font SafeFont(string fontName, float size, FontStyle style)
    {
        try
        {
            return new Font(fontName, size, style);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style);
        }
    }

    static void DrawIcon(Graphics g, string shape)
    {
        switch (shape)
        {
            case "Check":
                using (var brush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    g.FillEllipse(brush, 420, 205, 60, 60);
                }
                using (var penW = new Pen(Color.White, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(penW, 432, 235, 446, 250);
                    g.DrawLine(penW, 446, 250, 466, 220);
                }
                break;
            case "Star":
                using (var brush = new SolidBrush(Color.FromArgb(255, 185, 0)))
                {
                    g.FillEllipse(brush, 420, 205, 60, 60);
                }
                using (var font = new Font("Segoe UI Symbol", 24f, FontStyle.Bold))
                {
                    g.DrawString("★", font, Brushes.White, 427f, 212f);
                }
                break;
            case "Box":
                using (var brush = new SolidBrush(Color.FromArgb(232, 17, 35)))
                {
                    g.FillRectangle(brush, 425, 210, 50, 50);
                }
                using (var font = new Font("Segoe UI Symbol", 20f))
                {
                    g.DrawString("■", font, Brushes.White, 433f, 218f);
                }
                break;
            case "Circle":
                using (var brush = new SolidBrush(Color.FromArgb(142, 68, 173)))
                {
                    g.FillEllipse(brush, 420, 205, 60, 60);
                }
                using (var font = new Font("Segoe UI Symbol", 22f))
                {
                    g.DrawString("●", font, Brushes.White, 431f, 215f);
                }
                break;
            case "Heart":
                using (var brush = new SolidBrush(Color.FromArgb(233, 30, 99)))
                {
                    g.FillEllipse(brush, 420, 205, 60, 60);
                }
                using (var font = new Font("Segoe UI Symbol", 22f))
                {
                    g.DrawString("♥", font, Brushes.White, 430f, 216f);
                }
                break;
        }
    }
}
