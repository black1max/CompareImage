using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using CompareImage.Models;
using CompareImage.Services;

namespace CompareImage;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly WindowsOcrService _winRtOcrService;
    private readonly TesseractOcrService _tesseractOcrService;
    private readonly ImageComparatorService _comparatorService;
    private readonly PairMatchingService _matchingService;

    // ROIデータ
    private readonly ObservableCollection<RoiItem> _baseRois = new();
    private readonly ObservableCollection<RoiItem> _targetRois = new();

    // 比較ペア＆結果データ
    private readonly ObservableCollection<PairCompareResult> _compareResults = new();

    // 現在の画像状態
    private string? _baseImagePath;
    private string? _targetImagePath;
    private BitmapImage? _currentBitmap;

    // マウスドラッグ描画用
    private Point? _dragStartPoint;
    private System.Windows.Shapes.Rectangle? _dragRect;

    public MainWindow()
    {
        InitializeComponent();

        _winRtOcrService = new WindowsOcrService();
        _tesseractOcrService = new TesseractOcrService();
        _comparatorService = new ImageComparatorService(_winRtOcrService);
        _matchingService = new PairMatchingService();

        // ペアリングルールのセットアップ
        ComboMatchingRules.ItemsSource = _matchingService.AvailableRules;
        if (_matchingService.AvailableRules.Count > 0)
        {
            ComboMatchingRules.SelectedIndex = 0;
        }

        // OCRエンジンの初期化・検出
        InitializeOcrEngineSelection();

        // 初期バインド
        UpdateRoiDataGridSource();
        DgPairs.ItemsSource = _compareResults;

        // 外部ペアマッチングルール (PairMatchRule.json / PairMatchRurl.json) の自動読み込み
        Loaded += async (_, _) => await AutoLoadExternalPairMatchRulesAsync();
    }

    private async Task AutoLoadExternalPairMatchRulesAsync()
    {
        string[] candidateNames = { "PairMatchRule.json", "PairMatchRurl.json" };
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();

        foreach (var name in candidateNames)
        {
            string? targetPath = null;
            if (File.Exists(System.IO.Path.Combine(currentDir, name))) targetPath = System.IO.Path.Combine(currentDir, name);
            else if (File.Exists(System.IO.Path.Combine(baseDir, name))) targetPath = System.IO.Path.Combine(baseDir, name);

            if (targetPath != null)
            {
                var result = await RoslynRuleLoaderService.LoadRulesFromJsonAsync(targetPath);
                foreach (var rule in result.LoadedRules)
                {
                    _matchingService.RegisterRule(rule);
                }
                if (result.LoadedRules.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Roslyn: {result.LoadedRules.Count} 件の外部ルールを自動読み込みしました ({targetPath})");
                }
                break; // 最初に見つかったファイルを適用
            }
        }
    }

    private void InitializeOcrEngineSelection()
    {
        _winRtOcrService.InitializeEngine();
        _tesseractOcrService.DetectTesseract();

        if (_tesseractOcrService.IsAvailable)
        {
            ComboItemTesseract.Content = "Tesseract OCR";
            MenuEngineTesseract.Header = "Tesseract OCR";
        }
        else
        {
            ComboItemTesseract.Content = "Tesseract OCR (未インストール)";
            ComboItemTesseract.Foreground = Brushes.Gray;
            MenuEngineTesseract.Header = "Tesseract OCR (未インストール)";
        }

        // 初期選択は WinRT
        ComboOcrEngine.SelectedIndex = 0;
        _comparatorService.CurrentOcrService = _winRtOcrService;
        UpdateOcrEngineStatus();
    }

    private void UpdateOcrEngineStatus()
    {
        if (_comparatorService.CurrentOcrService == _tesseractOcrService)
        {
            TxtOcrStatus.Text = _tesseractOcrService.IsAvailable ? "Tesseract 稼働中" : "未インストール";
            TxtOcrStatus.ToolTip = _tesseractOcrService.StatusMessage;
            TxtOcrStatus.Foreground = _tesseractOcrService.IsAvailable ? new SolidColorBrush(Color.FromRgb(16, 124, 65)) : new SolidColorBrush(Color.FromRgb(216, 59, 1));
            MenuEngineWinRT.IsChecked = false;
            MenuEngineTesseract.IsChecked = true;
        }
        else
        {
            TxtOcrStatus.Text = _winRtOcrService.IsAvailable ? "WinRT OCR 稼働中" : "利用不可";
            TxtOcrStatus.ToolTip = _winRtOcrService.StatusMessage;
            TxtOcrStatus.Foreground = _winRtOcrService.IsAvailable ? new SolidColorBrush(Color.FromRgb(16, 124, 65)) : new SolidColorBrush(Color.FromRgb(216, 59, 1));
            MenuEngineWinRT.IsChecked = true;
            MenuEngineTesseract.IsChecked = false;
        }
    }

    private bool IsBaseMode => RbModeBase.IsChecked == true;
    private ObservableCollection<RoiItem> CurrentRois => IsBaseMode ? _baseRois : _targetRois;

    private void UpdateRoiDataGridSource()
    {
        DgRoiList.ItemsSource = null;
        DgRoiList.ItemsSource = CurrentRois;
        TxtRoiStatus.Text = $"表示中: {(IsBaseMode ? "ベース画像" : "ターゲット画像")} ({CurrentRois.Count} 件)";
        TxtRoiStatus.Foreground = IsBaseMode ? new SolidColorBrush(Color.FromRgb(0, 120, 212)) : new SolidColorBrush(Color.FromRgb(0, 150, 136));
        RedrawCanvasRois();
    }

    private void RbMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;

        string? imgPath = IsBaseMode ? _baseImagePath : _targetImagePath;
        LoadImageToCanvas(imgPath);
        UpdateRoiDataGridSource();
    }

    private void LoadImageToCanvas(string? path)
    {
        OverlayCanvas.Children.Clear();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _currentBitmap = null;
            PreviewImage.Source = null;
            RoiCanvas.Width = 0;
            RoiCanvas.Height = 0;
            return;
        }

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();

            _currentBitmap = bi;
            PreviewImage.Source = bi;
            RoiCanvas.Width = bi.PixelWidth;
            RoiCanvas.Height = bi.PixelHeight;

            RedrawCanvasRois();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"画像の読み込みに失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenBaseImage_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "ベース画像を開く",
            Filter = "画像ファイル (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|すべてのファイル (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            _baseImagePath = ofd.FileName;
            RbModeBase.IsChecked = true;
            LoadImageToCanvas(_baseImagePath);
            UpdateRoiDataGridSource();
        }
    }

    private void BtnOpenTargetImage_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "ターゲット画像を開く",
            Filter = "画像ファイル (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|すべてのファイル (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            _targetImagePath = ofd.FileName;
            RbModeTarget.IsChecked = true;
            LoadImageToCanvas(_targetImagePath);
            UpdateRoiDataGridSource();
        }
    }

    private void BtnCopyBaseToTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_baseRois.Count == 0)
        {
            MessageBox.Show("ベース画像の領域が設定されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show("ベース画像の領域設定をターゲット画像へ上書きコピーしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _targetRois.Clear();
            foreach (var r in _baseRois)
            {
                _targetRois.Add(new RoiItem
                {
                    Id = r.Id,
                    Name = r.Name,
                    CompareType = r.CompareType,
                    X = r.X,
                    Y = r.Y,
                    Width = r.Width,
                    Height = r.Height,
                    Preprocess = r.Preprocess.Clone()
                });
            }
            RbModeTarget.IsChecked = true;
            UpdateRoiDataGridSource();
            MessageBox.Show("コピーが完了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnSaveJson_Click(object sender, RoutedEventArgs e)
    {
        if (_baseRois.Count == 0)
        {
            MessageBox.Show("領域が設定されていません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_baseRois.Count != _targetRois.Count)
        {
            MessageBox.Show($"ベース画像（{_baseRois.Count}件）とターゲット画像（{_targetRois.Count}件）の領域数が一致していません。\n両方の領域数を一致させてから保存してください。",
                "領域数不一致", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "ROI設定をJSONとして保存",
            Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            DefaultExt = ".json"
        };
        if (sfd.ShowDialog() == true)
        {
            try
            {
                var config = new RoiConfig
                {
                    BaseImageRoi = _baseRois.ToList(),
                    TargetImageRoi = _targetRois.ToList()
                };
                RoiConfigService.SaveToFile(config, sfd.FileName);
                MessageBox.Show("設定を保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnLoadJson_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "ROI設定JSONを開く",
            Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            try
            {
                var config = RoiConfigService.LoadFromFile(ofd.FileName);
                _baseRois.Clear();
                foreach (var r in config.BaseImageRoi) _baseRois.Add(r);

                _targetRois.Clear();
                foreach (var r in config.TargetImageRoi) _targetRois.Add(r);

                UpdateRoiDataGridSource();
                MessageBox.Show($"{_baseRois.Count} 件の領域設定を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読込に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasScale != null)
        {
            CanvasScale.ScaleX = e.NewValue;
            CanvasScale.ScaleY = e.NewValue;
        }
    }

    private void DgRoiList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            BtnDeleteSelectedRoi_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnDeleteSelectedRoi_Click(object sender, RoutedEventArgs e)
    {
        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            int selectedIdx = DgRoiList.SelectedIndex;
            CurrentRois.Remove(selected);
            ReindexRois();
            UpdateRoiDataGridSource();

            if (CurrentRois.Count > 0)
            {
                DgRoiList.SelectedIndex = Math.Clamp(selectedIdx, 0, CurrentRois.Count - 1);
            }
        }
    }

    private void BtnUpRoi_Click(object sender, RoutedEventArgs e)
    {
        int idx = DgRoiList.SelectedIndex;
        if (idx > 0 && idx < CurrentRois.Count)
        {
            var item = CurrentRois[idx];
            CurrentRois.Move(idx, idx - 1);
            ReindexRois();
            UpdateRoiDataGridSource();
            DgRoiList.SelectedIndex = idx - 1;
        }
    }

    private void BtnDownRoi_Click(object sender, RoutedEventArgs e)
    {
        int idx = DgRoiList.SelectedIndex;
        if (idx >= 0 && idx < CurrentRois.Count - 1)
        {
            var item = CurrentRois[idx];
            CurrentRois.Move(idx, idx + 1);
            ReindexRois();
            UpdateRoiDataGridSource();
            DgRoiList.SelectedIndex = idx + 1;
        }
    }

    private void BtnSortRoisByY_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRois.Count <= 1) return;

        // Y座標順 (行単位)、同じ高さ付近(±10px)ならX座標順にソート
        var sorted = CurrentRois.OrderBy(r => r.Y / 15).ThenBy(r => r.X).ToList();
        CurrentRois.Clear();
        foreach (var r in sorted) CurrentRois.Add(r);

        ReindexRois();
        UpdateRoiDataGridSource();
    }

    private async void BtnAutoDetectRois_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null)
        {
            MessageBox.Show("画像が読み込まれていません。先に「ベース画像を開く」または「ターゲット画像を開く」で画像を開いてください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var detected = await _winRtOcrService.DetectTextRegionsAsync(_currentBitmap);

            if (detected.Count == 0)
            {
                MessageBox.Show("画像から文字列領域を検出できませんでした。", "検出結果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CurrentRois.Count > 0)
            {
                var dr = MessageBox.Show(
                    $"画像から {detected.Count} 件の文字列領域を検出しました。\n\n[はい]: 既存の領域をクリアして上書き\n[いいえ]: 既存の領域の末尾に追加\n[キャンセル]: 中止",
                    "自動検出の反映方法",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (dr == MessageBoxResult.Cancel) return;

                if (dr == MessageBoxResult.Yes)
                {
                    CurrentRois.Clear();
                }
            }

            foreach (var r in detected)
            {
                CurrentRois.Add(r);
            }

            ReindexRois();
            UpdateRoiDataGridSource();
            MessageBox.Show($"{detected.Count} 件の文字列領域を自動登録しました。", "自動検出完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"文字列領域の自動検出中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReindexRois()
    {
        for (int i = 0; i < CurrentRois.Count; i++)
        {
            CurrentRois[i].Id = i + 1;
        }
    }

    private void BtnClearRois_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRois.Count > 0 && MessageBox.Show("すべての領域をクリアしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            CurrentRois.Clear();
            UpdateRoiDataGridSource();
        }
    }

    // -----------------------------------------------------------------
    // 比較方式の変更ハンドラ
    // -----------------------------------------------------------------
    private void BtnSetSelectedText_Click(object sender, RoutedEventArgs e)
    {
        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            selected.CompareType = RoiCompareType.Text;
            UpdateRoiDataGridSource();
        }
    }

    private void BtnSetSelectedImage_Click(object sender, RoutedEventArgs e)
    {
        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            selected.CompareType = RoiCompareType.Image;
            UpdateRoiDataGridSource();
        }
    }

    private void MenuSetAllTextMode_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRois.Count == 0) return;
        foreach (var r in CurrentRois)
        {
            r.CompareType = RoiCompareType.Text;
        }
        UpdateRoiDataGridSource();
    }

    private void MenuSetAllImageMode_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRois.Count == 0) return;
        foreach (var r in CurrentRois)
        {
            r.CompareType = RoiCompareType.Image;
        }
        UpdateRoiDataGridSource();
    }

    private void RoiCompareType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RedrawCanvasRois();
        }));
    }

    // -----------------------------------------------------------------
    // 領域ごとの前処理設定ハンドラ
    // -----------------------------------------------------------------
    private bool _isUpdatingPreprocessUi = false;

    private void DgRoiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            _isUpdatingPreprocessUi = true;
            try
            {
                TxtSelectedRoiPreprocessTitle.Text = $"【領域前処理】ID {selected.Id}: {selected.Name}";
                var prep = selected.Preprocess;
                ChkRoiPreprocessEnabled.IsChecked = prep.Enabled;
                ChkRoiPreprocessContrast.IsChecked = prep.EnhanceContrast;
                ChkRoiPreprocessGrayscale.IsChecked = prep.ToGrayscale;
                ChkRoiPreprocessBinarize.IsChecked = prep.Binarize;
                TxtRoiBinarizeThreshold.Text = prep.BinarizeThreshold.ToString();
                ChkRoiPreprocessSharpen.IsChecked = prep.Sharpen;
            }
            finally
            {
                _isUpdatingPreprocessUi = false;
            }
        }
        else
        {
            TxtSelectedRoiPreprocessTitle.Text = "【領域ごとの前処理設定】領域を選択してください";
        }
    }

    private void RoiPreprocess_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingPreprocessUi) return;
        if (DgRoiList.SelectedItem is not RoiItem selected) return;

        var prep = selected.Preprocess;
        prep.Enabled = ChkRoiPreprocessEnabled.IsChecked == true;
        prep.EnhanceContrast = ChkRoiPreprocessContrast.IsChecked == true;
        prep.ToGrayscale = ChkRoiPreprocessGrayscale.IsChecked == true;
        prep.Binarize = ChkRoiPreprocessBinarize.IsChecked == true;
        if (int.TryParse(TxtRoiBinarizeThreshold?.Text, out int th))
        {
            prep.BinarizeThreshold = Math.Clamp(th, 0, 255);
        }
        prep.Sharpen = ChkRoiPreprocessSharpen.IsChecked == true;

        DgRoiList.Items.Refresh();
    }

    private void BtnCopyPreprocessToAll_Click(object sender, RoutedEventArgs e)
    {
        if (DgRoiList.SelectedItem is not RoiItem selected)
        {
            MessageBox.Show("コピー元の領域を選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"領域 ID {selected.Id} の前処理設定（{selected.PreprocessSummary}）を、現在表示中のすべての領域に適用しますか？",
            "前処理の一括適用", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            foreach (var roi in CurrentRois)
            {
                roi.Preprocess = selected.Preprocess.Clone();
            }
            DgRoiList.Items.Refresh();
            MessageBox.Show("すべての領域に前処理設定を適用しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // -----------------------------------------------------------------
    // メニューバー専用ハンドラ
    // -----------------------------------------------------------------
    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "画像領域OCR / 画像比較ツール (CompareImage)\n\n" +
            "・Windows 11 標準 OCR (WinRT OcrEngine) による高速テキスト比較\n" +
            "・領域ピクセル直接比較による画像一致判定\n" +
            "・メモリ高速パイプライン & CSV レポート出力対応",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // -----------------------------------------------------------------
    // キャンバスマウスドラッグ描画
    // -----------------------------------------------------------------
    private void RoiCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentBitmap == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(RoiCanvas);
        _dragStartPoint = pos;

        _dragRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = Brushes.Yellow,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0))
        };
        Canvas.SetLeft(_dragRect, pos.X);
        Canvas.SetTop(_dragRect, pos.Y);
        OverlayCanvas.Children.Add(_dragRect);

        RoiCanvas.CaptureMouse();
    }

    private void RoiCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint == null || _dragRect == null) return;

        var current = e.GetPosition(RoiCanvas);
        double x = Math.Min(_dragStartPoint.Value.X, current.X);
        double y = Math.Min(_dragStartPoint.Value.Y, current.Y);
        double w = Math.Abs(_dragStartPoint.Value.X - current.X);
        double h = Math.Abs(_dragStartPoint.Value.Y - current.Y);

        Canvas.SetLeft(_dragRect, x);
        Canvas.SetTop(_dragRect, y);
        _dragRect.Width = w;
        _dragRect.Height = h;
    }

    private void RoiCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartPoint == null || _dragRect == null) return;

        var current = e.GetPosition(RoiCanvas);
        RoiCanvas.ReleaseMouseCapture();

        double x = Math.Max(0, Math.Min(_dragStartPoint.Value.X, current.X));
        double y = Math.Max(0, Math.Min(_dragStartPoint.Value.Y, current.Y));
        double w = Math.Abs(_dragStartPoint.Value.X - current.X);
        double h = Math.Abs(_dragStartPoint.Value.Y - current.Y);

        OverlayCanvas.Children.Remove(_dragRect);
        _dragStartPoint = null;
        _dragRect = null;

        if (w >= 5 && h >= 5)
        {
            var rois = CurrentRois;
            int newId = rois.Count + 1;
            rois.Add(new RoiItem
            {
                Id = newId,
                Name = $"領域_{newId}",
                CompareType = RoiCompareType.Text, // 初期値は文字列比較
                X = (int)Math.Round(x),
                Y = (int)Math.Round(y),
                Width = (int)Math.Round(w),
                Height = (int)Math.Round(h)
            });
            UpdateRoiDataGridSource();
        }
    }

    private void RedrawCanvasRois()
    {
        OverlayCanvas.Children.Clear();

        foreach (var r in CurrentRois)
        {
            Color strokeColor;
            Color fillColor;

            // 比較方式およびベース／ターゲットに応じた色分け
            if (r.CompareType == RoiCompareType.Image)
            {
                // 画像比較: オレンジ / パープル
                strokeColor = IsBaseMode ? Color.FromRgb(255, 145, 0) : Color.FromRgb(224, 64, 251);
                fillColor = IsBaseMode ? Color.FromArgb(40, 255, 145, 0) : Color.FromArgb(40, 224, 64, 251);
            }
            else
            {
                // 文字列比較: 明るい緑 / 明るいシアン
                strokeColor = IsBaseMode ? Color.FromRgb(0, 230, 118) : Color.FromRgb(0, 191, 255);
                fillColor = IsBaseMode ? Color.FromArgb(40, 0, 230, 118) : Color.FromArgb(40, 0, 191, 255);
            }

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(fillColor)
            };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            OverlayCanvas.Children.Add(rect);

            string typeTag = r.CompareType == RoiCompareType.Image ? "[画像]" : "[文字]";
            var label = new Border
            {
                Background = new SolidColorBrush(strokeColor),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = $"#{r.Id} {r.Name} {typeTag}",
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                }
            };
            Canvas.SetLeft(label, r.X);
            Canvas.SetTop(label, Math.Max(0, r.Y - 18));
            OverlayCanvas.Children.Add(label);
        }
    }

    // -----------------------------------------------------------------
    // タブ2: フォルダ・ペア設定 ＆ 比較実行
    // -----------------------------------------------------------------
    private void BtnBrowseBaseDir_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFolderDialog { Title = "ベース画像フォルダを選択" };
        if (ofd.ShowDialog() == true)
        {
            TxtBaseDir.Text = ofd.FolderName;
        }
    }

    private void BtnBrowseTargetDir_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFolderDialog { Title = "ターゲット画像フォルダを選択" };
        if (ofd.ShowDialog() == true)
        {
            TxtTargetDir.Text = ofd.FolderName;
        }
    }

    private async void BtnLoadMatchingRulesJson_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "ペアマッチングルールJSONを開く",
            Filter = "ルール設定JSON (*.json)|*.json|すべてのファイル (*.*)|*.*",
            FileName = "PairMatchRule.json"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var result = await RoslynRuleLoaderService.LoadRulesFromJsonAsync(ofd.FileName);

                foreach (var rule in result.LoadedRules)
                {
                    _matchingService.RegisterRule(rule);
                }

                if (result.LoadedRules.Count > 0)
                {
                    // 最後に読み込んだルールを選択状態に
                    ComboMatchingRules.SelectedItem = result.LoadedRules.Last();
                }

                var msg = new StringBuilder();
                if (result.LoadedRules.Count > 0)
                {
                    msg.AppendLine($"✔ {result.LoadedRules.Count} 件のマッチングルールを Roslyn で正常にコンパイル・追加しました。");
                }

                if (result.Errors.Count > 0)
                {
                    msg.AppendLine($"\n【警告・エラー ({result.Errors.Count} 件)】");
                    foreach (var err in result.Errors)
                    {
                        msg.AppendLine(err);
                    }
                }

                MessageBox.Show(
                    msg.ToString(),
                    result.Errors.Count > 0 ? "読込完了 (一部エラーあり)" : "ルール読込完了",
                    MessageBoxButton.OK,
                    result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ルールの読み込み中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnScanPairs_Click(object sender, RoutedEventArgs e)
    {
        string baseDir = TxtBaseDir.Text.Trim();
        string targetDir = TxtTargetDir.Text.Trim();

        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(targetDir))
        {
            MessageBox.Show("ベースフォルダとターゲットフォルダの両方を指定してください。", "入力確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var rule = ComboMatchingRules.SelectedItem as PairMatchingRule;
            var pairs = _matchingService.CreatePairs(baseDir, targetDir, rule?.MatcherFunc);

            _compareResults.Clear();
            foreach (var p in pairs)
            {
                _compareResults.Add(new PairCompareResult
                {
                    PairName = p.PairName,
                    BaseImagePath = p.BaseImagePath,
                    TargetImagePath = p.TargetImagePath,
                    AllMatched = false
                });
            }

            DgRegionDetails.ItemsSource = null;
            TxtPairSummary.Text = $"{pairs.Count} 件の画像ペアを検出・登録しました。";
            MessageBox.Show($"{pairs.Count} 件の画像ペアを検出しました。", "スキャン完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"スキャン中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRunCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_baseRois.Count == 0)
        {
            MessageBox.Show("タブ1で領域（ROI）が設定されていません。\n先に比較したい領域を指定してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_baseRois.Count != _targetRois.Count)
        {
            MessageBox.Show("ベース側とターゲット側の領域数が一致していません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (_compareResults.Count == 0)
        {
            MessageBox.Show("比較する画像ペアがありません。「🔄 スキャン」を実行してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var roiConfig = new RoiConfig
        {
            BaseImageRoi = _baseRois.ToList(),
            TargetImageRoi = _targetRois.ToList()
        };

        bool ignoreWs = ChkIgnoreWhitespace.IsChecked == true;
        bool caseSens = ChkCaseSensitive.IsChecked == true;
        bool numOnly = ChkNumbersOnly.IsChecked == true;

        var preprocessOptions = new PreprocessOptions
        {
            Enabled = ChkEnablePreprocess.IsChecked == true,
            ToGrayscale = ChkPreprocessGrayscale.IsChecked == true,
            EnhanceContrast = ChkPreprocessContrast.IsChecked == true,
            Binarize = ChkPreprocessBinarize.IsChecked == true,
            Sharpen = ChkPreprocessSharpen.IsChecked == true
        };

        BtnRunCompare.IsEnabled = false;
        CompareProgressBar.Visibility = Visibility.Visible;
        CompareProgressBar.Value = 0;
        CompareProgressBar.Maximum = _compareResults.Count;

        int total = _compareResults.Count;
        int passCount = 0;
        string? firstError = null;

        for (int i = 0; i < total; i++)
        {
            var item = _compareResults[i];
            var pair = new ImagePair
            {
                PairName = item.PairName,
                BaseImagePath = item.BaseImagePath,
                TargetImagePath = item.TargetImagePath
            };

            try
            {
                var res = await _comparatorService.CompareSinglePairAsync(pair, roiConfig, ignoreWs, caseSens, numOnly, preprocessOptions);
                item.AllMatched = res.AllMatched;
                item.RegionResults = res.RegionResults;
                if (res.AllMatched) passCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"比較エラー ({item.PairName}): {ex.Message}");
                if (firstError == null) firstError = ex.Message;
            }

            CompareProgressBar.Value = i + 1;
        }

        // 一覧をリフレッシュ
        DgPairs.Items.Refresh();
        CompareProgressBar.Visibility = Visibility.Collapsed;
        BtnRunCompare.IsEnabled = true;

        int failCount = total - passCount;
        string errorNotice = firstError != null ? $"\n\n※一部の比較でエラーが発生しました:\n{firstError}" : "";
        MessageBox.Show($"全 {total} 件の比較が完了しました。\n\n✔ PASS (完全一致): {passCount} 件\n✘ FAIL (差異あり): {failCount} 件{errorNotice}",
            "比較完了", MessageBoxButton.OK, firstError != null ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void DgPairs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgPairs.SelectedItem is PairCompareResult selected)
        {
            DgRegionDetails.ItemsSource = selected.RegionResults;
            int pass = selected.RegionResults.Count(r => r.IsMatch);
            int total = selected.RegionResults.Count;
            TxtPairSummary.Text = $"選択ペア: {selected.PairName} | 結果: {selected.StatusText} ({pass}/{total} 領域一致)";
        }
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_compareResults.Count == 0 || _compareResults.All(r => r.RegionResults.Count == 0))
        {
            MessageBox.Show("エクスポートする結果がありません。先に比較を実行してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "比較結果をCSVとして保存",
            Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            DefaultExt = ".csv"
        };
        if (sfd.ShowDialog() == true)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("ペア名,全体判定,領域ID,領域名,比較方式,領域判定,Base内容/サイズ,Target内容/差異,Base画像パス,Target画像パス");

                foreach (var pair in _compareResults)
                {
                    foreach (var r in pair.RegionResults)
                    {
                        sb.AppendLine($"\"{EscapeCsv(pair.PairName)}\",\"{pair.StatusText}\",{r.RegionId},\"{EscapeCsv(r.RegionName)}\",\"{r.CompareTypeDisplayName}\",\"{r.StatusText}\",\"{EscapeCsv(r.BaseTextRaw)}\",\"{EscapeCsv(r.TargetTextRaw)}\",\"{EscapeCsv(pair.BaseImagePath)}\",\"{EscapeCsv(pair.TargetImagePath)}\"");
                    }
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("CSVファイルを保存しました。", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CSV保存に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnExportDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_compareResults.Count == 0 || _compareResults.All(r => r.RegionResults.Count == 0))
        {
            MessageBox.Show("エクスポートする結果がありません。先に比較を実行してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "比較結果の詳細エクスポート (形式を選択してください)",
            Filter = "Excelブック (*.xlsx)|*.xlsx|Markdownドキュメント (*.md)|*.md",
            FilterIndex = 1,
            FileName = $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}_CompareDetail"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                string ext = System.IO.Path.GetExtension(sfd.FileName).ToLowerInvariant();

                if (ext == ".xlsx")
                {
                    var results = _compareResults.ToList();
                    await Task.Run(() => DetailExportService.ExportToExcel(results, sfd.FileName));
                    MessageBox.Show($"Excelファイルを出力しました:\n{sfd.FileName}", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string outputDir = string.Empty;
                    var results = _compareResults.ToList();
                    await Task.Run(() => outputDir = DetailExportService.ExportToMarkdown(results, sfd.FileName));
                    MessageBox.Show($"Markdownレポート用フォルダーを出力しました:\n{outputDir}", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"詳細エクスポート中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
    }

    // -----------------------------------------------------------------
    // OCRエンジン選択ハンドラ
    // -----------------------------------------------------------------
    private bool _isSwitchingOcr = false;

    private void SwitchOcrEngine(bool useTesseract)
    {
        if (_isSwitchingOcr) return;
        _isSwitchingOcr = true;

        try
        {
            if (useTesseract)
            {
                _tesseractOcrService.DetectTesseract();
                if (!_tesseractOcrService.IsAvailable)
                {
                    MessageBox.Show(
                        "Tesseract OCR がインストールされていないか、実行ファイルが見つかりません。\n\n" +
                        "※CompareImage.exe と同じフォルダー下の「Tesseract」フォルダーに配置するか、\n" +
                        "システムに Tesseract-OCR をインストールしてください。\n\n" +
                        "「Windows 11 標準 OCR (WinRT)」を引き続き使用します。",
                        "OCR エンジン切り替え",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    _comparatorService.CurrentOcrService = _winRtOcrService;
                    ComboOcrEngine.SelectedIndex = 0;
                }
                else
                {
                    _comparatorService.CurrentOcrService = _tesseractOcrService;
                    ComboOcrEngine.SelectedIndex = 1;
                }
            }
            else
            {
                _winRtOcrService.InitializeEngine();
                if (!_winRtOcrService.IsAvailable)
                {
                    MessageBox.Show(
                        "Windows 11 標準 OCR (WinRT) の初期化に失敗しました。\n" +
                        _winRtOcrService.StatusMessage,
                        "OCR 警告",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                _comparatorService.CurrentOcrService = _winRtOcrService;
                ComboOcrEngine.SelectedIndex = 0;
            }

            UpdateOcrEngineStatus();
        }
        finally
        {
            _isSwitchingOcr = false;
        }
    }

    private void ComboOcrEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _isSwitchingOcr) return;

        if (ComboOcrEngine.SelectedItem is ComboBoxItem item)
        {
            string? tag = item.Tag as string;
            SwitchOcrEngine(tag == "Tesseract");
        }
    }

    private void MenuEngineWinRT_Click(object sender, RoutedEventArgs e)
    {
        SwitchOcrEngine(false);
    }

    private void MenuEngineTesseract_Click(object sender, RoutedEventArgs e)
    {
        SwitchOcrEngine(true);
    }

    private static string EscapeCsv(string text)
    {
        return text.Replace("\"", "\"\"");
    }
}