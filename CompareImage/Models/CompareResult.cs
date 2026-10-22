namespace CompareImage.Models;

public class RegionCompareResult
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public RoiCompareType CompareType { get; set; } = RoiCompareType.Text;
    public string BaseTextRaw { get; set; } = string.Empty;
    public string TargetTextRaw { get; set; } = string.Empty;
    public string BaseTextNormalized { get; set; } = string.Empty;
    public string TargetTextNormalized { get; set; } = string.Empty;
    public bool IsMatch { get; set; }

    // 切り抜き・詳細エクスポート用のROI座標情報
    public RoiItem? BaseRoi { get; set; }
    public RoiItem? TargetRoi { get; set; }

    public string CompareTypeDisplayName => CompareType switch
    {
        RoiCompareType.Text => "🔤 文字列",
        RoiCompareType.Image => "🖼 画像",
        _ => "🔤 文字列"
    };

    public string CompareTypeSimpleName => CompareType switch
    {
        RoiCompareType.Text => "文字列",
        RoiCompareType.Image => "画像",
        _ => "文字列"
    };

    public string StatusText => IsMatch ? "一致 (OK)" : "不一致 (NG)";
    public string StatusSimpleText => IsMatch ? "OK" : "NG";
}

public class PairCompareResult
{
    public string PairName { get; set; } = string.Empty;
    public string BaseImagePath { get; set; } = string.Empty;
    public string TargetImagePath { get; set; } = string.Empty;
    public bool AllMatched { get; set; }
    public List<RegionCompareResult> RegionResults { get; set; } = new();

    public string BaseFileName => System.IO.Path.GetFileName(BaseImagePath);
    public string TargetFileName => System.IO.Path.GetFileName(TargetImagePath);
    public string StatusText => AllMatched ? "PASS" : "FAIL";
}
