# PC Monitor Overlay

Windows 向けの軽量な PC 監視オーバーレイです。CPU、メモリ、GPU、VRAM の使用率を小さな常時表示ウィンドウで確認できます。

## Features

- CPU / メモリ / GPU / VRAM 使用率のリアルタイム表示
- 直近 60 秒のミニグラフ
- 常に手前表示、移動ロック、透明度調整
- タスクトレイ常駐メニュー
- ウィンドウ位置、サイズ、透明度などの自動保存

## Requirements

- Windows 10 / 11
- .NET 8 SDK

GPU と VRAM は `LibreHardwareMonitorLib` のセンサー値を使用します。GPU、ドライバー、権限、センサー対応状況によっては `N/A` と表示される場合があります。

## Quick Start

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

& $dotnet run --project .\PcMonitorOverlay\PcMonitorOverlay.csproj
```

または、用意しているスクリプトを使います。

```powershell
.\scripts\build.ps1
```

PowerShell の実行ポリシーで止まる場合は、次のように実行してください。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## Create Portable EXE

単体で起動できる Windows x64 向けの exe を作成します。

```powershell
.\scripts\publish-portable.ps1
```

実行ポリシーで止まる場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

出力先は既定で `dist-portable\PcMonitorOverlay.exe` です。`dist`、`dist-portable`、`bin`、`obj` は生成物なので Git 管理外にしています。

## Directory Structure

```text
.
├── PcMonitorOverlay.sln
├── PcMonitorOverlay/
│   ├── Controls/              # custom WPF graph control
│   ├── Models/                # metric snapshot records
│   ├── Services/              # system and hardware sensor readers
│   ├── Settings/              # persisted overlay settings
│   ├── App.xaml
│   ├── MainWindow.xaml
│   └── PcMonitorOverlay.csproj
├── scripts/
│   ├── build.ps1
│   └── publish-portable.ps1
└── README.md
```

## Settings

設定ファイルは次の場所に保存されます。

```text
%LOCALAPPDATA%\PcMonitorOverlay\settings.json
```

起動位置がおかしくなった場合は、タスクトレイメニューの `Reset position` を使うか、この設定ファイルを削除してください。

## Troubleshooting

- 起動直後に落ちる場合は、最新のソースでビルドし直してください。
- GPU / VRAM が `N/A` の場合は、センサーが取得できない環境の可能性があります。
- フレームワーク依存ビルドを使う場合は、実行環境に .NET Desktop Runtime が必要です。`publish-portable.ps1` で作成した exe は self-contained です。
