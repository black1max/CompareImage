# CompareImage - 画像領域 OCR / 画像比較ツール

Windows 11 標準搭載の OCR 機能（`Windows.Media.Ocr`）および **Tesseract OCR** を活用し、2 つの画像フォルダ内の対応する画像同士の指定領域（ROI）の文字列やピクセル差分を高速に比較判定する WPF アプリケーションです。

---

## 🌟 主な特徴

1. **上部メニューバー＆タブ統合 UI**
   - ファイル操作（画像読込・JSON保存読込・終了）、編集操作（領域コピー・一括比較方式切替・クリア）、比較実行、OCRエンジン切替、バージョン情報を集約した標準メニューバーを搭載。
2. **比較場所ごとの「文字列比較 (OCR)」／「画像比較 (ピクセル)」の個別指定**
   - 領域（ROI）ごとに、「🔤 文字列比較」または「🖼 画像比較 (ピクセル完全一致・差分率判定)」を自由に選択可能。
   - キャンバス上でも色分け（文字列: 緑/シアン、画像: オレンジ/パープル）とラベル表示で一目瞭然。
3. **デュアル OCR エンジン対応（自動フォールバック・ポータブル配置対応）**
   - **Windows 11 標準 OCR（WinRT）**: 追加導入不要で日本語・英語を高速にインメモリ認識（デフォルト）。多段階言語フォールバック（ユーザー言語 ➜ 日本語 `ja` ➜ インストール済み言語）により安定稼働。
   - **Tesseract OCR**: システム上の Tesseract だけでなく、**アプリケーションのフォルダー配下に `Tesseract/` を配置するだけで、OSの環境変数設定なしにポータブル動作**。
   - **自動フォールバック**: Tesseract 未導入時は安全に「Windows 11 標準 OCR」へ自動選択。
4. **領域 ID ごとの個別前処理 ＆ 全体前処理（オーバーライド方式）**
   - 領域ごとに「コントラスト強調」「グレースケール」「二値化 (白黒化・閾値指定)」「輪郭強調」を個別設定可能。
   - 個別設定がない領域には、比較画面の「画像前処理オプション」が全体デフォルトとして適用されます。
5. **文字列領域（ROI）の自動検出 (OCR) ＆ 柔軟な順序編集**
   - 画像内の全テキスト行をボタン 1 つで自動検出・登録。
   - 「▲ 上へ」「▼ 下へ」「↕ Y座標順ソート」、および右クリックメニューや `Delete` キーでの直感的な順序入替・削除に対応。
6. **Roslyn による外部 C# ペアマッチングルール動的読み込み (JSON)**
   - `PairMatchRule.json` を外部から編集するだけで、再ビルド不要で高度なペアリングロジック（接尾辞置換、正規表現など）を即座に動的コンパイル・実行可能。
7. **多彩なレポート出力（CSV / 詳細Excel / 詳細Markdown）**
   - **CSV エクスポート**: 全ペア・全領域の判定結果一覧を出力。
   - **詳細エクスポート (Excel/Markdown)**: 切り出し画像（元画像および前処理適用後画像）と OCR 認識結果を同一セル・同一桁に整列して出力。
   - 出力ファイル名は自動的に `yyyy_MM_dd_HH_mm_ss_CompareDetail`（24時間表記タイムスタンプ付き）で生成。
8. **ワンクリック配信用セットアップ・パッケージ生成 (`CompareImage.Setup`)**
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
