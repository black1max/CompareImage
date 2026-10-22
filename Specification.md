# CompareImage - 詳細設計仕様書 (Specification)

## 1. システム概要

**CompareImage** は、Windows 11 標準の WinRT OCR 機能（`Windows.Media.Ocr`）および **Tesseract OCR** を活用し、2つの画像フォルダ内に格納された対応画像同士の指定領域（ROI: Region of Interest）を高速かつ高精度に比較判定するデスクトップアプリケーション（WPF）です。

各領域ごとに「**🔤 文字列比較 (OCR)**」と「**🖼 画像比較 (ピクセル一致)**」を選択でき、領域ごとの個別前処理（二値化、コントラスト調整、輪郭強調など）や、Roslyn による外部 C# ペアマッチングルールの動的実行、Excel / Markdown による詳細レポート出力を備えています。

---

## 2. システムアーキテクチャ

```text
[プレゼンテーション層 (UI)]
   - MainWindow.xaml / MainWindow.xaml.cs (メニューバー、2タブ統合UI、ROI描画キャンバス、結果グリッド)
         │
         ▼
[ビジネスロジック層 (Services)]
   - PairMatchingService       : 画像ペアリング・Roslyn動的ルール実行
   - RoslynRuleLoaderService   : PairMatchRule.json の動的コンパイル
   - RoiConfigService          : ROI設定 (JSON) の永続化・復元
   - ImagePreprocessingService : 画像加工 (二値化、コントラスト、輪郭強調、グレースケール)
   - ImageComparatorService    : クロップ・前処理適用・テキスト正規化・ピクセル画像比較
   - DetailExportService       : Excel (ClosedXML) / Markdown レポート生成
   - IOcrService               : OCRエンジンの抽象化インターフェース
       ├─ WindowsOcrService    : WinRT OcrEngine (多段階フォールバック・インメモリ高速処理)
       └─ TesseractOcrService  : Tesseract-OCR (ポータブル配置・TESSDATA_PREFIX自動構成)
         │
         ▼
[データモデル層 (Models)]
   - RoiItem / RoiConfig / RoiCompareType / PreprocessOptions
   - ImagePair / PairMatchingRule / CustomPairRuleModel
   - PairCompareResult / RegionCompareResult
```

---

## 3. モジュール詳細仕様

### 3.1 サービス層 (`Services/`)

#### 1) `IOcrService` インターフェース
OCR エンジンを抽象化する統一インターフェース。
- `string EngineName { get; }`: エンジン表示名
- `bool IsAvailable { get; }`: 現在のシステムで利用可能かどうか
- `string StatusMessage { get; }`: ステータス・詳細情報（使用中の認識言語やパスなど）
- `Task<string> RecognizeCroppedRegionAsync(BitmapSource source, int x, int y, int width, int height)`: クロップ領域のテキスト認識

#### 2) `WindowsOcrService` (`IOcrService` 実装)
Windows 11 の標準 OCR 機能（`Windows.Media.Ocr`）をインメモリで直接駆動するクラス。
- **多段階言語フォールバック仕様**:
  1. `OcrEngine.TryCreateFromUserProfileLanguages()`（ユーザープロファイル優先言語）
  2. 失敗時は `AvailableRecognizerLanguages` から日本語（`ja`）を優先して探索・生成
  3. それでも利用できない場合は、インストールされている任意の言語パックを採用
- **ピクセルフォーマット変換**:
  - `BitmapDecoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)` を明示指定し、あらゆる画像フォーマットから OcrEngine が要求する Bgra8 形式へ安全に変換。
- **認識率向上補正**:
  - 文字の境界欠落を防ぐため、常に外周に適切な白余白（パディング）を付与。文字の高さが 80px 未満の場合は自動スケーリング拡大。
- `public async Task<List<RoiItem>> DetectTextRegionsAsync(BitmapSource source, int padding = 4)`:
  - 画像全体の単語矩形群（`BoundingRect`）を解析し、テキストが存在する行矩形を自動検出して `RoiItem` リストとして返却。

#### 3) `TesseractOcrService` (`IOcrService` 実装)
Tesseract-OCR を探索・実行するクラス。
- **探索順序（ポータブル優先）**:
  1. **`AppDomain.CurrentDomain.BaseDirectory\Tesseract\tesseract.exe`** (ポータブル配置最優先)
  2. システム環境変数 `PATH` 内
  3. `C:\Program Files\Tesseract-OCR\tesseract.exe`
  4. `C:\Program Files (x86)\Tesseract-OCR\tesseract.exe`
  5. `%LOCALAPPDATA%\Programs\Tesseract-OCR\tesseract.exe`
- **ポータブル自動構成仕様**:
  - `tesseract.exe` と同階層に `tessdata\` が存在する場合、内部でプロセス実行環境変数 `TESSDATA_PREFIX` を自動設定するため、OS環境変数を汚さずポータブルに動作。
- **自動フォールバック仕様**:
  - 未インストール環境で選択された場合、警告通知を表示した上で自動的に `Windows 11 標準 OCR (WinRT)` にフォールバック。

#### 4) `ImagePreprocessingService`
画像加工を担当する静的サービス。
- `public static BitmapSource ApplyPreprocessing(BitmapSource source, PreprocessOptions options)`:
  - **コントラスト強調**: ヒストグラム伸長によるダイナミックレンジ拡大
  - **グレースケール変換**: `Y = 0.299 R + 0.587 G + 0.114 B`
  - **二値化**: 指定しきい値（0〜255、初期値 128）による黒白二値化
  - **輪郭強調 (シャープネス)**: 3x3 アンシャープマスク畳み込みフィルター

#### 5) `ImageComparatorService`
画像のクロップ、前処理適用、テキスト正規化、ピクセル画像比較、一致判定を一括処理するクラス。
- **前処理の二段階適用仕様（個別優先オーバーライド）**:
  - 各領域（ROI）の評価時、領域固有設定 `bRoi.Preprocess.HasAnyOperation` が `true` であれば**領域固有設定**を適用。
  - 未設定または無効の場合は、比較画面（タブ2）の全体設定 `preprocessOptions` をフォールバックとして適用。
- **比較処理の分岐**:
  - `RoiCompareType.Text`: クロップ（＋前処理後）画像を OCR 認識し、テキスト正規化（空白無視、大文字小文字区別、数値抽出）を経て文字列比較。
  - `RoiCompareType.Image`: クロップ（＋前処理後）画像の 32bit ピクセルバイト列を生成し、ピクセル単位の一致・差分率（%）を算出。

#### 6) `DetailExportService`
詳細結果レポートの生成・エクスポートを担当するサービス。
- **デフォルト出力ファイル名**: `yyyy_MM_dd_HH_mm_ss_CompareDetail`（24時間表記タイムスタンプ付き）
- **Excel 出力 (`ExportToExcel`)**:
  - ClosedXML を使用し、ワークシートに切り出しサムネイル画像、前処理適用後画像、OCR文字列を行ごとに整列して配置。
- **Markdown 出力 (`ExportToMarkdown`)**:
  - 指定ファイル名と同名の専用フォルダー（`yyyy_MM_dd_HH_mm_ss_CompareDetail/`）を生成し、直下に `.md` ファイルと `images/` サブフォルダーを出力。

---

## 4. データ構造仕様 (`Models/`)

### 4.1 `PreprocessOptions`
```csharp
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
    public string SummaryText { get; }
}
```

### 4.2 `RoiItem`
```csharp
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
}
```

---

## 5. セットアップ・配布パッケージ構成 (`CompareImage.Setup`)

### 5.1 構成概要
アプリケーションおよび Tesseract-OCR ランタイム一式を同梱し、ターゲット環境で追加設定なしに即座に実行できる配布パッケージを作成するためのモジュール群。

- **`BuildInstaller.ps1`**:
  - `dotnet publish` による Release 発行
  - システムまたはローカルの Tesseract-OCR（`tesseract.exe`, 依存DLL, `tessdata` 言語データ）の自動収集・同梱
  - 配布用ポータブル ZIP パッケージ（`Output/CompareImage_Portable_v1.0.0.zip`）の自動生成
  - Inno Setup（`ISCC.exe`）が存在する場合の `Setup.exe` 自動コンパイル
- **`CompareImageSetup.iss`**:
  - Inno Setup 6 形式のインストーラー定義スクリプト。日本語UI、Program Files への配置、デスクトップ/スタートメニューショートカット、アンインストーラーを生成。
- **`CompareImage.csproj` の連動設定**:
  - ソリューション直下に `Tesseract/` フォルダーが存在する場合、通常のデバッグ/リリースビルド時にも出力先（exe 直下）へ自動コピー（`CopyToOutputDirectory="PreserveNewest"`）されるよう構成。
