namespace CompareImage.Models;

public class ImagePair
{
    public string PairName { get; set; } = string.Empty;
    public string BaseImagePath { get; set; } = string.Empty;
    public string TargetImagePath { get; set; } = string.Empty;

    public string BaseFileName => System.IO.Path.GetFileName(BaseImagePath);
    public string TargetFileName => System.IO.Path.GetFileName(TargetImagePath);
}
