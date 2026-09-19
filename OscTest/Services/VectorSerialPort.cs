using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{

    public enum VectorCommand : byte
    {
        FrameEnd = 0b00,  // 00: flag==0 → バッファスワップ（フレーム終端）。Teensy は rx_points を num_points に切り替える
        NormalLine = 0b01,  // 01: 通常輝度ラインを X,Y へ描画
        BrightLine = 0b10,  // 10: Z輝度ラインを X,Y へ描画
    }

    public class VectorSerialPort : IDisposable
    {
        private readonly SerialPort? _port;
        private readonly byte[] _buf = new byte[4];

        // フレーム内の全コマンドをバッファに豌め、一度の Write() でまとめて送信する。
        // 1フレームの最大サイズ = MAX_PTS(3000) * 2コマンド * 4バイト = 24000 バイト
        private readonly byte[] _frameBuffer = new byte[32768];
        private int _frameBufferLen = 0;
        private bool _needResync = true; // 初回接続時のみ再同期を送る

        public VectorSerialPort(string portName, int baudRate = 56000)
        {
            try
            {
                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _port.Open();
            }
            catch { }
        }

        /// <summary>
        /// 4バイトコマンドを構築して送信する。
        /// </summary>
        /// <param name="cmd">コマンド種別 (2bit)</param>
        /// <param name="brightness">輝度 0～63 (6bit)</param>
        /// <param name="x">X座標 または ライン数 0～4095 (12bit)</param>
        /// <param name="y">Y座標 0～4095 (12bit)</param>
        public void Send(VectorCommand cmd, int brightness, int x, int y)
        {
            // 範囲クランプ
            brightness = Math.Clamp(brightness, 0, 63);
            x = Math.Clamp(x, 0, 4095);
            y = Math.Clamp(y, 0, 4095);

            uint word = ((uint)cmd << 30)
                      | ((uint)brightness << 24)
                      | ((uint)x << 12)
                      | ((uint)y);

            // フレームバッファに豌める（Write() は FlushFrame() でまとめて行う）
            if (_frameBufferLen + 4 <= _frameBuffer.Length)
            {
                _frameBuffer[_frameBufferLen++] = (byte)(word >> 24);
                _frameBuffer[_frameBufferLen++] = (byte)(word >> 16);
                _frameBuffer[_frameBufferLen++] = (byte)(word >> 8);
                _frameBuffer[_frameBufferLen++] = (byte)(word);
            }
        }

        /// <summary>
        /// フレームバッファの全コマンドを 1回の Write() で送信する。
        /// SendFrameEnd() の後に必ず呼ぶこと。
        /// </summary>
        public void FlushFrame()
        {
            if (_frameBufferLen > 0 && _port != null)
            {
                _port.Write(_frameBuffer, 0, _frameBufferLen);
            }
            _frameBufferLen = 0;
        }

        /// <summary>
        /// 再同期シーケンスを送信する。
        /// Teensy は 4バイトすべてゼロを受信すると do_resync=1 にセットし、
        /// 次の非ゼロバイトまでデータを破棄して境界を再取得する。
        /// 接続開始時や通信異常回復時に呼ぶこと。
        /// </summary>
        public void SendResync()
        {
            if (!_needResync) return;  // 初回接続時のみ実行
            _needResync = false;
            // 8バイトゼロを送信。
            // Teensy は 4バイト蓄積で cmd==0 を検出 → do_resync=1。
            // 2回送ることで受信バッファ内の残留バイト（最大3バイト）を確実に飲み込ませる。
            byte[] zero = new byte[8];
            _port?.Write(zero, 0, 8);
        }

        /// <summary>フレーム終端（バッファスワップ）をバッファに積み、フレーム内全コマンドを一括送信する。</summary>
        public void SendFrameEnd()
        {
            // flag==0, bright==0, x==1, y==0
            // cmd全体が 0x00000000 になると Teensy は do_resync=1 (resync トリガー) に
            // なってしまい flag==0 のバッファスワップ処理に到達しない。
            // cmd != 0 でかつ flag==0 になるよう x に 1 をセットする。
            Send(VectorCommand.FrameEnd, 0, 1, 0);
            FlushFrame(); // フレーム内の全コマンドをまとめて1回のWrite()で送信
        }

        /// <summary>描画せずに X,Y へ移動する。</summary>
        public void SendPenUp(int x, int y) =>
            Send(VectorCommand.NormalLine, 0, x, y);

        /// <summary>通常輝度で X,Y まで線を引く。</summary>
        public void SendNormalLine(int x, int y, int brightness) =>
            Send(VectorCommand.NormalLine, (int)Math.Clamp(brightness, 0, 63), x, y);

        /// <summary>Z輝度で X,Y まで線を引く。</summary>
        public void SendBrightLine(int x, int y, int brightness) =>
            Send(VectorCommand.BrightLine, (int)Math.Clamp(brightness, 0, 63), x, y);

        public void Dispose()
        {
            _port?.Close();
            _port?.Dispose();
        }
    }
}
