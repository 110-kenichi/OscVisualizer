using OscVisualizer.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI.SourceGenerators;
using Avalonia.Controls;

namespace OscVisualizer.ViewModels
{
    internal partial class ProcessingViewModel : ViewModelBase, IDisposable
    {
        /// <summary>
        /// </summary>
        /// <remarks></remarks>
        [Reactive]
        public partial String Text
        {
            get;
            set;
        } = "t = time * (pi / 4); \r\nfor(i = 500; i--; )\r\n{\r\n    x = i*20;\r\n    y = (i*20) / 235;\r\n\r\n    k = 4 * cos(x / 21);\r\n    e = y / 8 - 20;\r\n    d = mag(k, e);\r\n\r\n    q = 3 * sin(k * 2)\r\n        + 0.3 / k\r\n        + sin(y / 19) * k * (9 + 2 * sin(e * 14 - d * 3 + t * 2));\r\n\r\n    c = d - t;\r\n\r\n    cx = (q + 50 * cos(c)) / 200;\r\n    cy = (q * sin(c) + d * 39 - 675) / 200;\r\n\r\n    diam = ((k * k > 15) ? 2 : 1) / 200;\r\n\r\n    circle(cx, cy, diam, 1, 0);\r\n}";

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: マネージド状態を破棄します (マネージド オブジェクト)
                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~WaveCircleViewModel()
        // {
        //     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
