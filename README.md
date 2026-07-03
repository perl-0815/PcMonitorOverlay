# PC Monitor Overlay

Windows 向けの軽量な PC 監視オーバーレイです。CPU、メモリ、GPU、VRAM の使用率を、小さな常時表示ウィンドウで確認できます。

## 主な機能

- CPU / メモリ / GPU / VRAM 使用率のリアルタイム表示
- 直近 60 秒のミニグラフ
- 常に手前表示、移動ロック、透明度調整
- タスクトレイ常駐メニュー
- ウィンドウ位置、サイズ、透明度などの自動保存

## 必要環境

- Windows 10 / 11
- インストールスクリプトから導入する場合: .NET 8 SDK

.NET 8 SDK が入っていない場合、`scripts\install.ps1` は `winget` を使って導入を試みます。`publish-portable.ps1` で作成した self-contained exe を配布する場合、実行するだけなら .NET Runtime / SDK は不要です。
SDK の自動導入には `winget` とインターネット接続が必要です。

GPU と VRAM は `LibreHardwareMonitorLib` のセンサー値を使用します。GPU、ドライバー、権限、センサー対応状況によっては `N/A` と表示される場合があります。

## インストール

通常は次のコマンドでインストールできます。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

インストール先は既定で次の場所です。

```text
%LOCALAPPDATA%\Programs\PcMonitorOverlay
```

インストール時に行うこと:

- .NET 8 SDK が不足している場合は `winget` で導入を試みる
- self-contained の `PcMonitorOverlay.exe` を作成する
- `%LOCALAPPDATA%\Programs\PcMonitorOverlay` に配置する
- スタートメニューとデスクトップにショートカットを作成する
- インストール後にアプリを起動する

Windows 起動時にも自動起動したい場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -StartWithWindows
```

ショートカットを作成したくない場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -NoDesktopShortcut -NoStartMenuShortcut
```

.NET SDK の自動導入を行わず、不足していたら停止したい場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -SkipDependencyInstall
```

既に `dist-portable\PcMonitorOverlay.exe` がある場合、それを使ってインストールできます。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -UseExistingBuild
```

## アンインストール

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

設定ファイルも削除したい場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1 -RemoveSettings
```

## 使い方

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

## 画面上のボタン

画面右上のボタンは、現在のオーバーレイ表示や移動操作を切り替えるためのものです。
有効な状態は濃い色、無効な状態は薄い色で表示されます。

| 表示 | 意味 | 動作 |
| --- | --- | --- |
| `PIN` | 常に手前表示が有効 | 他のウィンドウより前面に表示されます。クリックすると `FREE` に切り替わります。 |
| `FREE` | 常に手前表示が無効 | 通常のウィンドウと同じように、他のウィンドウの後ろに隠れることがあります。クリックすると `PIN` に切り替わります。 |
| `LOCK` | 移動ロックが無効 | オーバーレイをドラッグで移動できます。クリックすると `LOCKED` になります。 |
| `LOCKED` | 移動ロックが有効 | 誤操作で位置がずれないよう、ドラッグ移動を受け付けません。クリックすると `LOCK` に戻ります。 |

補足:

- `PIN` / `FREE` は「手前に固定するかどうか」の切り替えです。
- `LOCK` / `LOCKED` は「ドラッグ移動を許可するかどうか」の切り替えです。
- 透明度は `Opacity` スライダーで調整できます。
- タスクトレイメニューの `Always on top` は `PIN` / `FREE` と同じ設定です。
- タスクトレイメニューの `Lock movement` は `LOCK` / `LOCKED` と同じ設定です。
- 位置を戻したい場合は、タスクトレイメニューの `Reset position` を使ってください。

## ポータブル exe の作成

単体で起動できる Windows x64 向けの exe を作成します。

```powershell
.\scripts\publish-portable.ps1
```

実行ポリシーで止まる場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

出力先は既定で `dist-portable\PcMonitorOverlay.exe` です。`dist`、`dist-portable`、`bin`、`obj` は生成物なので Git 管理外にしています。

## ディレクトリ構成

```text
.
|-- PcMonitorOverlay.sln
|-- PcMonitorOverlay/
|   |-- Controls/              # custom WPF graph control
|   |-- Models/                # metric snapshot records
|   |-- Services/              # system and hardware sensor readers
|   |-- Settings/              # persisted overlay settings
|   |-- App.xaml
|   |-- MainWindow.xaml
|   `-- PcMonitorOverlay.csproj
|-- scripts/
|   |-- build.ps1
|   |-- install.ps1
|   |-- publish-portable.ps1
|   `-- uninstall.ps1
`-- README.md
```

## 設定ファイル

設定ファイルは次の場所に保存されます。

```text
%LOCALAPPDATA%\PcMonitorOverlay\settings.json
```

起動位置がおかしくなった場合は、タスクトレイメニューの `Reset position` を使うか、この設定ファイルを削除してください。

## トラブルシューティング

- 起動直後に落ちる場合は、最新のソースでビルドし直してください。
- GPU / VRAM が `N/A` の場合は、センサーが取得できない環境の可能性があります。
- `winget` が使えない環境では、.NET 8 SDK を手動で入れてから `install.ps1` を再実行してください。
- フレームワーク依存ビルドを使う場合は、実行環境に .NET Desktop Runtime が必要です。`publish-portable.ps1` で作成した exe は self-contained です。
