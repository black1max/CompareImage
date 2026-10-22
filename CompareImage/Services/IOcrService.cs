using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CompareImage.Services;

public interface IOcrService
{
    string EngineName { get; }
    bool IsAvailable { get; }
    string StatusMessage { get; }

    Task<string> RecognizeCroppedRegionAsync(BitmapSource source, int x, int y, int width, int height);
}
