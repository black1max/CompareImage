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

    // マウスドラッグ・対話操作用
    private enum CanvasInteractionMode
    {
        None,
        Create,
        Move,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
        ResizeTop,
        ResizeBottom,
        ResizeLeft,
        ResizeRight
    }

    private CanvasInteractionMode _interactionMode = CanvasInteractionMode.None;
    private Point? _dragStartPoint;
    private Rect _initialRoiRect;
    private RoiItem? _activeRoiItem;
    private System.Windows.Shapes.Rectangle? _dragRect;
    private bool _isUpdatingCoordUi = false;

    // 比較処理キャンセルトークン
    private System.Threading.CancellationTokenSource? _compareCts;

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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // テキストボックス等の入力中はキー操作を邪魔しない
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox)
        {
            return;
        }

        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            if (e.Key == Key.Delete)
            {
                BtnDeleteSelectedRoi_Click(sender, e);
                e.Handled = true;
                return;
            }

            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (isCtrl)
            {
                // Ctrl + 矢印キー: サイズ伸縮 (Shiftで10px)
                if (e.Key == Key.Left) { selected.Width = Math.Max(5, selected.Width - step); e.Handled = true; }
                else if (e.Key == Key.Right) { selected.Width = Math.Max(5, selected.Width + step); e.Handled = true; }
                else if (e.Key == Key.Up) { selected.Height = Math.Max(5, selected.Height - step); e.Handled = true; }
                else if (e.Key == Key.Down) { selected.Height = Math.Max(5, selected.Height + step); e.Handled = true; }
            }
            else
            {
                // 矢印キー: 位置移動 (Shiftで10px)
                if (e.Key == Key.Left) { selected.X = Math.Max(0, selected.X - step); e.Handled = true; }
                else if (e.Key == Key.Right) { selected.X = Math.Max(0, selected.X + step); e.Handled = true; }
                else if (e.Key == Key.Up) { selected.Y = Math.Max(0, selected.Y - step); e.Handled = true; }
                else if (e.Key == Key.Down) { selected.Y = Math.Max(0, selected.Y + step); e.Handled = true; }
            }

            if (e.Handled)
            {
                UpdateCoordUiFromSelected(selected);
                DgRoiList.Items.Refresh();
                RedrawCanvasRois();
            }
        }
    }

    private void DgRoiList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Window_PreviewKeyDown で一括処理するためフォールスルー
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

    private void BtnSetSelectedNumber_Click(object sender, RoutedEventArgs e)
    {
        if (DgRoiList.SelectedItem is RoiItem selected)
        {
            selected.CompareType = RoiCompareType.Number;
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

    private void MenuSetAllNumberMode_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRois.Count == 0) return;
        foreach (var r in CurrentRois)
        {
            r.CompareType = RoiCompareType.Number;
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
            // 位置・サイズ微調整UIの更新
            _isUpdatingCoordUi = true;
            try
            {
                PanelRoiAdjust.IsEnabled = true;
                TxtRoiAdjustTitle.Text = $"【微調整】ID {selected.Id}: {selected.Name}";
                TxtRoiX.Text = selected.X.ToString();
                TxtRoiY.Text = selected.Y.ToString();
                TxtRoiW.Text = selected.Width.ToString();
                TxtRoiH.Text = selected.Height.ToString();
            }
            finally
            {
                _isUpdatingCoordUi = false;
            }

            // 前処理UIの更新
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
            PanelRoiAdjust.IsEnabled = false;
            TxtRoiAdjustTitle.Text = "【選択領域の位置・サイズ微調整】領域を選択してください";
            _isUpdatingCoordUi = true;
            try
            {
                TxtRoiX.Text = "";
                TxtRoiY.Text = "";
                TxtRoiW.Text = "";
                TxtRoiH.Text = "";
            }
            finally
            {
                _isUpdatingCoordUi = false;
            }

            TxtSelectedRoiPreprocessTitle.Text = "【領域ごとの前処理設定】領域を選択してください";
        }

        RedrawCanvasRois();
    }

    private void BtnNudge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (DgRoiList.SelectedItem is not RoiItem selected) return;

        var parts = tag.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int delta)) return;

        switch (parts[0])
        {
            case "X":
                selected.X = Math.Max(0, selected.X + delta);
                break;
            case "Y":
                selected.Y = Math.Max(0, selected.Y + delta);
                break;
            case "W":
                selected.Width = Math.Max(5, selected.Width + delta);
                break;
            case "H":
                selected.Height = Math.Max(5, selected.Height + delta);
                break;
        }

        UpdateCoordUiFromSelected(selected);
        DgRoiList.Items.Refresh();
        RedrawCanvasRois();
    }

    private void TxtRoiCoord_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCoordUi) return;
        if (DgRoiList.SelectedItem is not RoiItem selected) return;

        bool changed = false;
        if (int.TryParse(TxtRoiX.Text, out int x) && x >= 0 && x != selected.X) { selected.X = x; changed = true; }
        if (int.TryParse(TxtRoiY.Text, out int y) && y >= 0 && y != selected.Y) { selected.Y = y; changed = true; }
        if (int.TryParse(TxtRoiW.Text, out int w) && w >= 5 && w != selected.Width) { selected.Width = w; changed = true; }
        if (int.TryParse(TxtRoiH.Text, out int h) && h >= 5 && h != selected.Height) { selected.Height = h; changed = true; }

        if (changed)
        {
            DgRoiList.Items.Refresh();
            RedrawCanvasRois();
        }
    }

    private void UpdateCoordUiFromSelected(RoiItem selected)
    {
        _isUpdatingCoordUi = true;
        try
        {
            TxtRoiX.Text = selected.X.ToString();
            TxtRoiY.Text = selected.Y.ToString();
            TxtRoiW.Text = selected.Width.ToString();
            TxtRoiH.Text = selected.Height.ToString();
        }
        finally
        {
            _isUpdatingCoordUi = false;
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
    // キャンバスマウス操作 (選択・移動・リサイズ・新規描画)
    // -----------------------------------------------------------------
    private const double HandleSize = 8.0;
    private const double HandleHitRadius = 7.0;

    private (CanvasInteractionMode mode, RoiItem? roi) HitTestCanvas(Point pt)
    {
        var selected = DgRoiList.SelectedItem as RoiItem;
        if (selected != null)
        {
            // 選択中ROIのハンドル判定 (四隅と四辺)
            double x = selected.X;
            double y = selected.Y;
            double w = selected.Width;
            double h = selected.Height;

            if (IsNearPoint(pt, new Point(x, y))) return (CanvasInteractionMode.ResizeTopLeft, selected);
            if (IsNearPoint(pt, new Point(x + w, y))) return (CanvasInteractionMode.ResizeTopRight, selected);
            if (IsNearPoint(pt, new Point(x, y + h))) return (CanvasInteractionMode.ResizeBottomLeft, selected);
            if (IsNearPoint(pt, new Point(x + w, y + h))) return (CanvasInteractionMode.ResizeBottomRight, selected);
            if (IsNearPoint(pt, new Point(x + w / 2, y))) return (CanvasInteractionMode.ResizeTop, selected);
            if (IsNearPoint(pt, new Point(x + w / 2, y + h))) return (CanvasInteractionMode.ResizeBottom, selected);
            if (IsNearPoint(pt, new Point(x, y + h / 2))) return (CanvasInteractionMode.ResizeLeft, selected);
            if (IsNearPoint(pt, new Point(x + w, y + h / 2))) return (CanvasInteractionMode.ResizeRight, selected);
        }

        // 内部判定 (逆順: 上に描画されているものを優先)
        var rois = CurrentRois;
        for (int i = rois.Count - 1; i >= 0; i--)
        {
            var r = rois[i];
            if (pt.X >= r.X && pt.X <= r.X + r.Width && pt.Y >= r.Y && pt.Y <= r.Y + r.Height)
            {
                return (CanvasInteractionMode.Move, r);
            }
        }

        return (CanvasInteractionMode.Create, null);
    }

    private static bool IsNearPoint(Point p1, Point p2)
    {
        return Math.Abs(p1.X - p2.X) <= HandleHitRadius && Math.Abs(p1.Y - p2.Y) <= HandleHitRadius;
    }

    private void RoiCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentBitmap == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(RoiCanvas);
        _dragStartPoint = pos;
        RoiCanvas.Focus();

        var (mode, hitRoi) = HitTestCanvas(pos);
        _interactionMode = mode;
        _activeRoiItem = hitRoi;

        if (mode == CanvasInteractionMode.Create)
        {
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
        }
        else if (hitRoi != null)
        {
            if (DgRoiList.SelectedItem != hitRoi)
            {
                DgRoiList.SelectedItem = hitRoi;
            }
            _initialRoiRect = new Rect(hitRoi.X, hitRoi.Y, hitRoi.Width, hitRoi.Height);
        }

        RoiCanvas.CaptureMouse();
    }

    private void RoiCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(RoiCanvas);

        if (_interactionMode == CanvasInteractionMode.None || _dragStartPoint == null)
        {
            // ホバー時のカーソル変更
            var (hoverMode, _) = HitTestCanvas(current);
            RoiCanvas.Cursor = hoverMode switch
            {
                CanvasInteractionMode.ResizeTopLeft or CanvasInteractionMode.ResizeBottomRight => Cursors.SizeNWSE,
                CanvasInteractionMode.ResizeTopRight or CanvasInteractionMode.ResizeBottomLeft => Cursors.SizeNESW,
                CanvasInteractionMode.ResizeTop or CanvasInteractionMode.ResizeBottom => Cursors.SizeNS,
                CanvasInteractionMode.ResizeLeft or CanvasInteractionMode.ResizeRight => Cursors.SizeWE,
                CanvasInteractionMode.Move => Cursors.SizeAll,
                _ => Cursors.Cross
            };
            return;
        }

        double dx = current.X - _dragStartPoint.Value.X;
        double dy = current.Y - _dragStartPoint.Value.Y;

        if (_interactionMode == CanvasInteractionMode.Create)
        {
            if (_dragRect == null) return;
            double x = Math.Min(_dragStartPoint.Value.X, current.X);
            double y = Math.Min(_dragStartPoint.Value.Y, current.Y);
            double w = Math.Abs(_dragStartPoint.Value.X - current.X);
            double h = Math.Abs(_dragStartPoint.Value.Y - current.Y);

            Canvas.SetLeft(_dragRect, x);
            Canvas.SetTop(_dragRect, y);
            _dragRect.Width = w;
            _dragRect.Height = h;
        }
        else if (_activeRoiItem != null)
        {
            var roi = _activeRoiItem;
            switch (_interactionMode)
            {
                case CanvasInteractionMode.Move:
                    roi.X = Math.Max(0, (int)Math.Round(_initialRoiRect.X + dx));
                    roi.Y = Math.Max(0, (int)Math.Round(_initialRoiRect.Y + dy));
                    break;
                case CanvasInteractionMode.ResizeTopLeft:
                    int newX = Math.Max(0, (int)Math.Round(_initialRoiRect.X + dx));
                    int newY = Math.Max(0, (int)Math.Round(_initialRoiRect.Y + dy));
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Right - newX));
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Bottom - newY));
                    roi.X = (int)Math.Round(_initialRoiRect.Right - roi.Width);
                    roi.Y = (int)Math.Round(_initialRoiRect.Bottom - roi.Height);
                    break;
                case CanvasInteractionMode.ResizeTopRight:
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Width + dx));
                    newY = Math.Max(0, (int)Math.Round(_initialRoiRect.Y + dy));
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Bottom - newY));
                    roi.Y = (int)Math.Round(_initialRoiRect.Bottom - roi.Height);
                    break;
                case CanvasInteractionMode.ResizeBottomLeft:
                    newX = Math.Max(0, (int)Math.Round(_initialRoiRect.X + dx));
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Right - newX));
                    roi.X = (int)Math.Round(_initialRoiRect.Right - roi.Width);
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Height + dy));
                    break;
                case CanvasInteractionMode.ResizeBottomRight:
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Width + dx));
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Height + dy));
                    break;
                case CanvasInteractionMode.ResizeTop:
                    newY = Math.Max(0, (int)Math.Round(_initialRoiRect.Y + dy));
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Bottom - newY));
                    roi.Y = (int)Math.Round(_initialRoiRect.Bottom - roi.Height);
                    break;
                case CanvasInteractionMode.ResizeBottom:
                    roi.Height = Math.Max(5, (int)Math.Round(_initialRoiRect.Height + dy));
                    break;
                case CanvasInteractionMode.ResizeLeft:
                    newX = Math.Max(0, (int)Math.Round(_initialRoiRect.X + dx));
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Right - newX));
                    roi.X = (int)Math.Round(_initialRoiRect.Right - roi.Width);
                    break;
                case CanvasInteractionMode.ResizeRight:
                    roi.Width = Math.Max(5, (int)Math.Round(_initialRoiRect.Width + dx));
                    break;
            }

            UpdateCoordUiFromSelected(roi);
            RedrawCanvasRois();
        }
    }

    private void RoiCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartPoint == null) return;

        var current = e.GetPosition(RoiCanvas);
        RoiCanvas.ReleaseMouseCapture();

        if (_interactionMode == CanvasInteractionMode.Create)
        {
            if (_dragRect != null)
            {
                OverlayCanvas.Children.Remove(_dragRect);
                _dragRect = null;
            }

            double x = Math.Max(0, Math.Min(_dragStartPoint.Value.X, current.X));
            double y = Math.Max(0, Math.Min(_dragStartPoint.Value.Y, current.Y));
            double w = Math.Abs(_dragStartPoint.Value.X - current.X);
            double h = Math.Abs(_dragStartPoint.Value.Y - current.Y);

            if (w >= 5 && h >= 5)
            {
                var rois = CurrentRois;
                int newId = rois.Count + 1;
                var newRoi = new RoiItem
                {
                    Id = newId,
                    Name = $"領域_{newId}",
                    CompareType = RoiCompareType.Text, // 初期値は文字列比較
                    X = (int)Math.Round(x),
                    Y = (int)Math.Round(y),
                    Width = (int)Math.Round(w),
                    Height = (int)Math.Round(h)
                };
                rois.Add(newRoi);
                UpdateRoiDataGridSource();
                DgRoiList.SelectedItem = newRoi;
            }
        }
        else if (_activeRoiItem != null)
        {
            DgRoiList.Items.Refresh();
            RedrawCanvasRois();
        }

        _interactionMode = CanvasInteractionMode.None;
        _dragStartPoint = null;
        _activeRoiItem = null;
    }

    private void RedrawCanvasRois()
    {
        OverlayCanvas.Children.Clear();
        var selectedItem = DgRoiList.SelectedItem as RoiItem;

        foreach (var r in CurrentRois)
        {
            bool isSelected = (r == selectedItem);
            Color strokeColor;
            Color fillColor;

            // 比較方式およびベース／ターゲットに応じた色分け
            if (r.CompareType == RoiCompareType.Image)
            {
                // 画像比較: オレンジ / パープル
                strokeColor = IsBaseMode ? Color.FromRgb(255, 145, 0) : Color.FromRgb(224, 64, 251);
                fillColor = IsBaseMode ? Color.FromArgb(40, 255, 145, 0) : Color.FromArgb(40, 224, 64, 251);
            }
            else if (r.CompareType == RoiCompareType.Number)
            {
                // 数値比較: 鮮やかなブルー / シアン
                strokeColor = IsBaseMode ? Color.FromRgb(0, 120, 215) : Color.FromRgb(0, 176, 255);
                fillColor = IsBaseMode ? Color.FromArgb(40, 0, 120, 215) : Color.FromArgb(40, 0, 176, 255);
            }
            else
            {
                // 文字列比較: 明るい緑 / エメラルド
                strokeColor = IsBaseMode ? Color.FromRgb(0, 200, 83) : Color.FromRgb(0, 191, 255);
                fillColor = IsBaseMode ? Color.FromArgb(40, 0, 200, 83) : Color.FromArgb(40, 0, 191, 255);
            }

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = isSelected ? 3.0 : 1.5,
                Fill = new SolidColorBrush(fillColor)
            };
            if (isSelected)
            {
                rect.StrokeDashArray = new DoubleCollection { 3, 2 };
            }

            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            OverlayCanvas.Children.Add(rect);

            string typeTag = r.CompareType switch
            {
                RoiCompareType.Image => "[画像]",
                RoiCompareType.Number => "[数値]",
                _ => "[文字]"
            };

            var label = new Border
            {
                Background = new SolidColorBrush(strokeColor),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = $"#{r.Id} {r.Name} {typeTag}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                }
            };
            Canvas.SetLeft(label, r.X);
            Canvas.SetTop(label, Math.Max(0, r.Y - 18));
            OverlayCanvas.Children.Add(label);

            // 選択中の領域には四隅・四辺にリサイズハンドルを描画
            if (isSelected)
            {
                AddHandle(r.X, r.Y);
                AddHandle(r.X + r.Width, r.Y);
                AddHandle(r.X, r.Y + r.Height);
                AddHandle(r.X + r.Width, r.Y + r.Height);
                AddHandle(r.X + r.Width / 2, r.Y);
                AddHandle(r.X + r.Width / 2, r.Y + r.Height);
                AddHandle(r.X, r.Y + r.Height / 2);
                AddHandle(r.X + r.Width, r.Y + r.Height / 2);
            }
        }
    }

    private void AddHandle(double cx, double cy)
    {
        var h = new System.Windows.Shapes.Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(h, cx - HandleSize / 2);
        Canvas.SetTop(h, cy - HandleSize / 2);
        OverlayCanvas.Children.Add(h);
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

    private void BtnCancelCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_compareCts != null && !_compareCts.IsCancellationRequested)
        {
            _compareCts.Cancel();
            BtnCancelCompare.IsEnabled = false;
            MenuCancelCompare.IsEnabled = false;
            TxtCompareProgress.Text = "中断処理中 (現在のペア完了後に停止します)...";
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

        // 以前の比較結果をクリアして未実行状態にリセット
        foreach (var item in _compareResults)
        {
            item.RegionResults.Clear();
            item.AllMatched = false;
        }
        DgPairs.Items.Refresh();
        DgRegionDetails.ItemsSource = null;

        _compareCts?.Dispose();
        _compareCts = new System.Threading.CancellationTokenSource();

        BtnRunCompare.IsEnabled = false;
        MenuRunCompare.IsEnabled = false;
        BtnCancelCompare.IsEnabled = true;
        MenuCancelCompare.IsEnabled = true;

        CompareProgressBar.Visibility = Visibility.Visible;
        TxtCompareProgress.Visibility = Visibility.Visible;
        CompareProgressBar.Value = 0;
        CompareProgressBar.Maximum = _compareResults.Count;

        int total = _compareResults.Count;
        int processedCount = 0;
        int passCount = 0;
        int failCount = 0;
        string? firstError = null;
        bool isCancelled = false;

        try
        {
            for (int i = 0; i < total; i++)
            {
                if (_compareCts.Token.IsCancellationRequested)
                {
                    isCancelled = true;
                    break;
                }

                var item = _compareResults[i];
                TxtCompareProgress.Text = $"{i + 1} / {total} 件を比較中... ({item.PairName})";

                var pair = new ImagePair
                {
                    PairName = item.PairName,
                    BaseImagePath = item.BaseImagePath,
                    TargetImagePath = item.TargetImagePath
                };

                try
                {
                    var res = await _comparatorService.CompareSinglePairAsync(
                        pair, roiConfig, ignoreWs, caseSens, numOnly, preprocessOptions, _compareCts.Token);
                    item.AllMatched = res.AllMatched;
                    item.RegionResults = res.RegionResults;
                    processedCount++;
                    if (res.AllMatched) passCount++;
                    else failCount++;
                }
                catch (OperationCanceledException)
                {
                    isCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"比較エラー ({item.PairName}): {ex.Message}");
                    if (firstError == null) firstError = ex.Message;
                    processedCount++;
                    failCount++;
                }

                CompareProgressBar.Value = i + 1;
                // 完了したペアの結果をリアルタイムで一覧に反映
                DgPairs.Items.Refresh();

                if (_compareCts.Token.IsCancellationRequested)
                {
                    isCancelled = true;
                    break;
                }
            }
        }
        finally
        {
            DgPairs.Items.Refresh();
            CompareProgressBar.Visibility = Visibility.Collapsed;
            TxtCompareProgress.Visibility = Visibility.Collapsed;

            BtnRunCompare.IsEnabled = true;
            MenuRunCompare.IsEnabled = true;
            BtnCancelCompare.IsEnabled = false;
            MenuCancelCompare.IsEnabled = false;

            _compareCts.Dispose();
            _compareCts = null;
        }

        string errorNotice = firstError != null ? $"\n\n※一部の比較でエラーが発生しました:\n{firstError}" : "";

        if (isCancelled)
        {
            int unexecutedCount = total - processedCount;
            TxtPairSummary.Text = $"比較を中断しました (完了: {processedCount} 件 [PASS: {passCount} / FAIL: {failCount}] / 未実行: {unexecutedCount} 件)";
            MessageBox.Show(
                $"比較処理を途中で中断しました。\n\n" +
                $"✔ 完了件数: {processedCount} / {total} 件\n" +
                $"  ・PASS (完全一致): {passCount} 件\n" +
                $"  ・FAIL (差異あり): {failCount} 件\n" +
                $"⏳ 未実行: {unexecutedCount} 件\n\n" +
                $"※比較したところまでの結果を画面に出力・反映しました。\n「CSVエクスポート」や「詳細エクスポート」から結果を出力できます。{errorNotice}",
                "比較中断", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                $"全 {total} 件の比較が完了しました。\n\n✔ PASS (完全一致): {passCount} 件\n✘ FAIL (差異あり): {failCount} 件{errorNotice}",
                "比較完了", MessageBoxButton.OK, firstError != null ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
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