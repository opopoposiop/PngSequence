# windowsアプリ作成

## アプリの機能

連番PNGファイルからAVIファイルを作成する。

AVIファイル作成には、無圧縮またはインストール済みUT Video Codecを使用する。

## 動作OS

Windows 11

## 実行ファイル

exeファイル単体起動

## ユーザーインターフェイス

- 画像ファイルのドロップエリア
- AVI出力先フォルダ
- AVI圧縮方式選択
- FPS入力
- 動作状況表示
- 停止ボタン
- 変換実行ボタン
- 変換進捗表示バー

## 動作の概略

1. 連番PNGファイルをドロップ
2. AVIファイルの圧縮方法を選択
   - 無圧縮
   - Windowsにインストール済みのUT Video Codec
3. AVIファイル作成

## UT Video Codec

64bit版UT Video Codecを使用する。

通常のインストール場所:

```text
C:\Program Files\utvideo
```
