# CompareImage セットアッププロジェクト (インストーラー作成)

本フォルダーには、**CompareImage** のインストーラー（Setup）および配布用パッケージを作成するための各種構成ファイルが含まれています。

Tesseract-OCR ランタイム（`tesseract.exe`、依存DLL群、`tessdata` 日本語/英語言語データ）が自動同梱される構成となっています。

---

## 構成ファイル一覧

| ファイル名 | 形式 | 説明 |
| :--- | :--- | :--- |
| **`BuildInstaller.ps1`** | PowerShell | **【推奨】ワンクリック自動ビルドスクリプト**。<br>`dotnet publish` の実行、Tesseract-OCR の自動同梱、ポータブル ZIP 作成、および Inno Setup による `Setup.exe` 作成を全自動で実行します。 |
| **`CompareImageSetup.iss`** | Inno Setup | 単一のインストーラー実行ファイル（`.exe`）を生成するための定義スクリプト。<br>日本語対応、デスクトップ/スタートメニューアイコン作成、アンインストーラーを自動生成。 |
| **`CompareImage.Setup.wixproj`** | WiX v5 | Visual Studio / MSBuild 用の MSI インストーラープロジェクト。 |
| **`Package.wxs`** | WiX XML | WiX Toolset 用のインストーラー構成・ディレクトリ定義ファイル。 |

---

## 使い方（インストーラーの作成手順）

### 方法 1: PowerShell スクリプトで全自動作成（最も簡単）

PowerShell（またはターミナル）を開き、以下のコマンドを実行します：

```powershell
# セットアップパッケージの自動作成
pwsh -ExecutionPolicy Bypass -File .\CompareImage.Setup\BuildInstaller.ps1
```

#### 生成物（`Output/` フォルダーに出力）
1. **`Output/publish/`**: Tesseract-OCR を含む発行済みアプリケーション一式
2. **`Output/CompareImage_Portable_v1.0.0.zip`**: 解凍後すぐに動かせるポータブル配布パッケージ
3. **`Output/CompareImage_Setup_v1.0.0.exe`**: （※Inno Setup 導入時）単一の Windows セットアップインストーラー

---

### 方法 2: Inno Setup で `Setup.exe` を作成する場合

1. 無料の [Inno Setup 6](https://jrsoftware.org/isinfo.php) をインストールします。
2. `CompareImage.Setup\CompareImageSetup.iss` を右クリックして **「Compile」** を選択します。
3. `Output\` フォルダーに `CompareImage_Setup_v1.0.0.exe` が生成されます。

---

### 方法 3: Visual Studio 上でビルドする場合

ソリューション（`CompareImage.slnx`）に `CompareImage.Setup` が登録されています。
Visual Studio からソリューション全体のビルドを行うことができます。
