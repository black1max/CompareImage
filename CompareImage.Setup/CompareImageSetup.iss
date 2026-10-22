; =====================================================================
; CompareImage インストーラー定義スクリプト (Inno Setup 6用)
; =====================================================================
#define MyAppName "CompareImage"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CompareImage Project"
#define MyAppExeName "CompareImage.exe"
#define SourceOutputDir "..\CompareImage\bin\Debug\net10.0-windows10.0.19041.0"

[Setup]
; アプリケーション基本情報
AppId={{A3E5F8B1-7C2D-4F9A-9E1B-5D3C7E9A2F4B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; 出力設定
OutputDir=..\Output
OutputBaseFilename=CompareImage_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; 権限設定 (管理者権限で Program Files へインストール)
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; メインアプリケーション実行バイナリおよび依存ライブラリ
Source: "{#SourceOutputDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

; Tesseract-OCR ランタイム・言語データ (Tesseract フォルダー下へ丸ごと配置)
; 出力先または C:\Program Files\Tesseract-OCR から自動取得
#if DirExists(SourceOutputDir + "\Tesseract")
Source: "{#SourceOutputDir}\Tesseract\*"; DestDir: "{app}\Tesseract"; Flags: ignoreversion recursesubdirs createallsubdirs
#elif DirExists("..\Tesseract")
Source: "..\Tesseract\*"; DestDir: "{app}\Tesseract"; Flags: ignoreversion recursesubdirs createallsubdirs
#elif DirExists("C:\Program Files\Tesseract-OCR")
Source: "C:\Program Files\Tesseract-OCR\*"; DestDir: "{app}\Tesseract"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "doc\*,*.html,tesseract-uninstall.exe"
#endif

[Icons]
; スタートメニューショートカット
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; デスクトップショートカット (タスクでチェックされた場合)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; インストール完了後に起動可能
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
