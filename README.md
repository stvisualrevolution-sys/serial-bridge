# KartePaste SerialBridge

AtomS3 が USB-CDC で送ってくる SOAP テキストを Windows クリップボードに置くヘルパー。
医師が Ctrl+V でカルテ欄へ貼り付けるだけ。SerialBridge 自身は貼り付けも入力もしない。

## 動作の流れ

```
iPhone アプリ
  ↓ BLE (GATT RX)
AtomS3 (ESP32-S3)
  ↓ USB-CDC (115200 baud)  "SOAP:{本文}\x04"
SerialBridge.exe (このツール)
  ↓ クリップボードへ設定
医師が Ctrl+V でカルテへ貼り付け
  ↓
AtomS3 の物理ボタンで「次の患者へ」
```

---

## 必要環境

| 項目 | 要件 |
|---|---|
| OS | Windows 10 / 11 (64-bit 推奨) |
| .NET Framework | 4.8（Windows 10以降・Windows Update 適用済みなら同梱） |
| USB | AtomS3 を USB-C で接続（USB-CDC ドライバ自動認識） |

---

## exe の取得方法

### A. GitHub Actions (推奨 — ビルド環境不要)

1. このリポジトリの **Actions** タブを開く
2. 「Build SerialBridge」ワークフロー → **Run workflow** をクリック
3. 実行完了後、Artifacts から **SerialBridge-Release.zip** をダウンロード
4. ZIP を展開 → `SerialBridge.exe` を任意のフォルダへ配置

> push するだけで自動ビルドされます（`SerialBridge/` 以下の変更を検知）。

> **モノレポ配置時の注意:** KartePaste リポジトリルートに `SerialBridge/` を置く構成（B 配置）に切り替える場合は、`build.yml` の `env.SLN_PATH`（例: `SerialBridge/SerialBridge.sln`）を変えるだけでなく、`env.OUT_EXE`（成果物パス）も合わせて調整してください。`/p:OutputPath` は `.csproj` からの相対パスで解決されるため、モノレポルートに出力させるには `..\..\build_output\` のように深さに応じた変更が必要です。

### B. Visual Studio 2022 でビルド

1. `SerialBridge.sln` を VS2022 で開く
2. 構成を **Release** にして **ビルド → ソリューションのビルド**
3. `SerialBridge\bin\Release\SerialBridge.exe` が生成される

---

## 使い方

1. **SerialBridge.exe を起動する**
   - タスクトレイにアイコンが現れる（ウィンドウは最小表示）
   - アイコンをダブルクリックするとステータスウィンドウを表示/非表示できる

2. **AtomS3 を USB-C で PC に接続する**
   - 数秒で VID=0x303A の CDC ポートが自動検出され「接続: COMx」と表示される
   - 検出できない場合は「AtomS3 が見つかりません」と表示され 3 秒ごとに再探索する

3. **iPhone の KartePaste アプリで「自動・連続（BLE-USB）」モードを選んで送信する**
   - AtomS3 が BLE で受け取り USB-CDC 経由で PC に転送する

4. **カルテ欄にカーソルを置いて Ctrl+V で貼り付ける**
   - テキストはクリップボードに入るだけ。SerialBridge は自動貼り付けをしない

5. **AtomS3 の物理ボタンを押して次の患者に進む**

6. **終了するにはトレイアイコンを右クリック → 「終了」**

---

## SmartScreen 警告への対処

未署名の exe は初回起動時に Windows SmartScreen が警告を表示します。

**回避手順:**

1. ダウンロードした `SerialBridge.exe` を右クリック → **プロパティ**
2. 「全般」タブ下部の「**セキュリティ: このファイルは…ブロックされています。**」横の
   「**許可する**」チェックボックスをオンにして OK
3. 再度ダブルクリックすると警告なしで起動できる

> 将来的にはコードサイニング証明書で署名することで警告を解消できます。

---

## トラブルシュート

### AtomS3 が検出されない

| 確認事項 | 対処 |
|---|---|
| AtomS3 のUSB-CDC (TinyUSB) が有効か | ArduinoIDE 設定: `USB CDC On Boot = Enabled` と `USB Mode = USB-OTG (TinyUSB)` であること |
| デバイスマネージャーに COM が出るか | 「ポート (COM と LPT)」に「USB Serial Device (COMx)」が出ていれば OK |
| VID が 0x303A か | デバイスマネージャー → 対象 COM を右クリック → プロパティ → 詳細 → ハードウェアID: `USB\VID_303A...` |
| 他のアプリが COM を占有していないか | Arduino IDE や他のターミナルを閉じる |
| WMI サービスが動いているか | `services.msc` で「Windows Management Instrumentation」が実行中か確認 |

### クリップボードに入らない

- STA スレッド制約: `[STAThread]` は `Program.cs` の `Main` に付与済み。別途対処不要。
- 他アプリがクリップボードを占有している場合は内部で 3 回リトライします。
- ログファイル (`%APPDATA%\SerialBridge\serial_bridge.log`) を確認してください。

### ログの場所

```
%APPDATA%\SerialBridge\serial_bridge.log
```

2 MB 超えで `.bak` にローテートされます。

---

## 通信プロトコル（ファーム・iOS・PC 共通仕様）

- 方向: AtomS3 → PC のみ（PC からの送信なし）
- ボーレート: 115200 baud / 8N1
- フレーム: `SOAP:` + UTF-8 本文 + `0x04`(EOT)
- 日本語・改行を含む UTF-8 をそのままクリップボードへ設定

---

## ライセンス

MIT
