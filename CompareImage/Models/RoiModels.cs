using System.Text.Json.Serialization;
using CompareImage.Services;

namespace CompareImage.Models;

public enum RoiCompareType
{
    Text = 0,   // 文字列 (OCR)
    Image = 1   // 画像 (ピクセル一致)
}

public class RoiItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public RoiCompareType CompareType { get; set; } = RoiCompareType.Text;

    // 領域ごとの画像前処理設定
    public PreprocessOptions Preprocess { get; set; } = new();

    // UI表示用ヘルパープロパティ
    [JsonIgnore]
    public string CompareTypeDisplayName => CompareType switch
    {
        RoiCompareType.Text => "🔤 文字列",
        RoiCompareType.Image => "🖼 画像",
        _ => "🔤 文字列"
    };

    [JsonIgnore]
    public string PreprocessSummary => Preprocess?.SummaryText ?? "なし";

    public RoiItem Clone() => new()
    {
        Id = this.Id,
        Name = this.Name,
        X = this.X,
        Y = this.Y,
        Width = this.Width,
        Height = this.Height,
        CompareType = this.CompareType,
        Preprocess = this.Preprocess.Clone()
    };
}

public class RoiConfig
{
    public List<RoiItem> BaseImageRoi { get; set; } = new();
    public List<RoiItem> TargetImageRoi { get; set; } = new();

    public bool IsValid()
    {
        return BaseImageRoi.Count > 0 && BaseImageRoi.Count == TargetImageRoi.Count;
    }
}
