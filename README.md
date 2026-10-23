# CompareImage - 画像領域 OCR / 画像比較ツール

Windows 11 標準搭載の OCR 機能（`Windows.Media.Ocr`）および **Tesseract OCR** を活用し、2 つの画像フォルダ内の対応する画像同士の指定領域（ROI）の文字列やピクセル差分を高速に比較判定する WPF アプリケーションです。

---

## 🌟 主な特徴

1. **上部メニューバー＆タブ統合 UI**
   - ファイル操作（画像読込・JSON保存読込・終了）、編集操作（領域コピー・一括比較方式切替・クリア）、比較実行、OCRエンジン切替、バージョン情報を集約した標準メニューバーを搭載。
2. **3種類の比較方式（🔤 文字列 / 🔢 数値 / 🖼 画像）の個別指定**
   - 領域（ROI）ごとに、「🔤 文字列比較 (OCR)」「🔢 数値比較 (OCR+正規化)」「🖼 画像比較 (ピクセル完全一致)」を自由に選択可能。
   - **数値比較**: 通貨記号やカンマを除去し、全角半角正規化、符号（`+`/`-`）や小数点を含む数値として等価判定（例: `+4980.50` == `4980.5`）。ピリオド誤読補正や空文字（未検出）の厳格判定を搭載。
   - キャンバス上でも色分け（文字列: 緑/シアン、数値: ブルー/ティール、画像: オレンジ/パープル）とラベル表示で一目瞭然。
3. **直感的なキャンバス操作 ＆ スリムバーUI**
   - キャンバス上のROIをクリックで直接選択し、ドラッグ移動や四隅・四辺のハンドルによるマウスリサイズが可能。
   - 矢印キー（1px / Shiftで10px）や Ctrl+矢印キー（伸縮）による精密なキーボード調整に対応。
   - 座標・サイズ入力欄を1行スリムバー化し、ROI一覧（DataGrid）を常時広々表示。
4. **デュアル OCR エンジン対応 ＆ 超最適化パイプライン**
   - **Windows 11 標準 OCR（WinRT）**: 追加導入不要で日本語・英語を高速にインメモリ認識（デフォルト）。
   - **Tesseract OCR**: システムインストール不要、**アプリケーション配下に `Tesseract/` を置くだけでポータブル動作**。
   - **OCR 超最適化**: 3.5〜5倍超解像拡大、大津の自動二値化、白抜き文字自動反転、モルフォロジー細線補強、PSM7単一行優先、数値ホワイトリスト制限を完備。
5. **安全な比較中断（途中キャンセル）と中途結果の保持・エクスポート**
   - 大量のペア比較中でも、進捗バー横の「キャンセル」ボタンでいつでも安全に中断可能。
   - 中断時点までに完了した結果を即座にUI表示し、そのまま「CSVエクスポート」や「詳細エクスポート」が可能。
6. **領域 ID ごとの個別前処理 ＆ 全体前処理（オーバーライド方式）**
   - 領域ごとに「コントラスト強調」「グレースケール」「二値化 (白黒化・閾値指定)」「輪郭強調」を個別設定可能。
   - 個別設定がない領域には、比較画面の「画像前処理オプション」が全体デフォルトとして適用されます。
7. **文字列領域（ROI）の自動検出 (OCR) ＆ 柔軟な順序編集**
   - 画像内の全テキスト行をボタン 1 つで自動検出・登録。
   - 「▲ 上へ」「▼ 下へ」「↕ Y座標順ソート」、および右クリックメニューや `Delete` キーでの直感的な順序入替・削除に対応。
8. **Roslyn による外部 C# ペアマッチングルール動的読み込み (JSON)**
   - `PairMatchRule.json` を外部から編集するだけで、再ビルド不要で高度なペアリングロジック（接尾辞置換、正規表現など）を即座に動的コンパイル・実行可能。
9. **レイアウト最適化された詳細レポート出力（Excel / Markdown）**
   - **CSV エクスポート**: 全ペア・全領域の判定結果一覧を出力。
   - **詳細エクスポート (Excel/Markdown)**: 切り出し画像と、直下の行に抽出された文字列・変換数値を同一列に整列して出力。画像が文字の上に覆い被さるのを防止し、`[文字列]` / `[数値]` ラベル付きで見やすく出力。
   - 出力ファイル名は自動的に `yyyy_MM_dd_HH_mm_ss_CompareDetail`（24時間表記タイムスタンプ付き）で生成。
10. **サンプルテストデータ (`sample_image`) の同梱**
    - フォント違いによるピクセル不一致を、文字列・数値比較で論理一致検証できるサンプル画像 5 組と設定 JSON を同梱。
11. **ワンクリック配信用セットアップ・パッケージ生成 (`CompareImage.Setup`)**
    - PowerShell スクリプト（`BuildInstaller.ps1`）を実行するだけで、Tesseract-OCR（日本語・英語データ含む）を自動同梱したポータブル ZIP パッケージおよび Inno Setup によるインストーラー（`Setup.exe`）を自動生成可能。

---

## 💻 動作環境

- **OS**: Windows 11（バージョン 21H2 以降推奨）
- **開発 / 実行ランタイム**: .NET 10.0 SDK（`net10.0-windows10.0.19041.0` 以上）
- **推奨開発環境**: Visual Studio 2026 / Visual Studio 2022（17.12 以降）

---

## 🚀 クイックスタート

### 1. ビルド
リポジトリ直下で以下のコマンドを実行します。

```powershell
dotnet build CompareImage.slnx
```

### 2. アプリケーション起動
Visual Studio から `F5` キーでデバッグ起動するか、以下のコマンドを実行します。

```powershell
dotnet run --project CompareImage/CompareImage.csproj
```

### 3. 配布用パッケージ（インストーラー）の作成
Tesseract-OCR を同梱した配布用 ZIP パッケージやインストーラーを作成するには、以下のスクリプトを実行します：

```powershell
pwsh -ExecutionPolicy Bypass -File .\CompareImage.Setup\BuildInstaller.ps1
```
※ `Output/` フォルダーに配布用パッケージが出力されます。

---

## 📂 プロジェクト構成

```text
<任意のフォルダーパス>\CompareImage\
│
├── .gitignore                   # セキュリティ・プライバシー除外設定済み
├── CompareImage.slnx            # 新世代ソリューション形式
├── README.md                    # 本書（簡易説明書）
├── USER_MANUAL.md               # 操作マニュアル（使用説明書）
├── Specification.md             # 詳細設計・仕様書
├── PairMatchRule.json           # 外部ペアマッチングルール定義
├── sample_image/                # サンプル検証用画像・設定
│   ├── Base/                    # 期待値画像 (sample_001.png〜005.png)
│   ├── Target/                  # 実績画像 (異なるフォント)
│   └── sample_roi_config.json   # サンプル用ROI定義
│
├── CompareImage/                # メインアプリケーション
│   ├── CompareImage.csproj      # プロジェクト定義
│   ├── App.xaml / App.xaml.cs   # アプリケーションエントリ
│   ├── MainWindow.xaml          # メイン UI（タブ統合レイアウト）
│   ├── MainWindow.xaml.cs       # UI ロジック・イベントハンドラ
│   ├── Models/                  # データ構造定義 (ROI, ペア, 比較結果)
│   └── Services/                # コアロジック
│       ├── WindowsOcrService.cs      # WinRT OcrEngine によるインメモリ OCR
│       ├── TesseractOcrService.cs    # Tesseract OCR 連携 (ポータブル対応)
│       ├── ImagePreprocessingService.cs # 画像前処理 (二値化・コントラスト・輪郭等)
│       ├── ImageComparatorService.cs # クロップ・正規化・比較判定
│       ├── PairMatchingService.cs    # 画像ペアリングロジック
│       ├── RoslynRuleLoaderService.cs# Roslyn 動的ルールコンパイラ
│       ├── RoiConfigService.cs       # ROI 設定の JSON 永続化
│       └── DetailExportService.cs    # 詳細レポート (Excel/Markdown) 出力
│
└── CompareImage.Setup/          # 配布・セットアップ作成プロジェクト
    ├── README.md                # セットアップ作成マニュアル
    ├── BuildInstaller.ps1       # ワンクリック自動パッケージングスクリプト
    └── CompareImageSetup.iss    # Inno Setup 用インストーラー定義スクリプト
```

---

## 📖 関連ドキュメント

- [使用説明書 (USER_MANUAL.md)](USER_MANUAL.md) - 各種機能のステップ別操作手順、前処理の使い分け、エクスポート手順
- [詳細仕様書 (Specification.md)](Specification.md) - アーキテクチャ設計、各クラス・メソッド仕様、データ構造
- [セットアップ作成ガイド (CompareImage.Setup/README.md)](CompareImage.Setup/README.md) - インストーラー作成と Tesseract 同梱手順
