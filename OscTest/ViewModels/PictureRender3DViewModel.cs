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
    internal partial class PictureRender3DViewModel : ViewModelBase, IDisposable
    {
        /// <summary>
        /// </summary>
        /// <remarks></remarks>
        [Reactive]
        public partial float ThetaX
        {
            get;
            set;
        } = 0f;

        [Reactive]
        public partial float ThetaY
        {
            get;
            set;
        } = 0;

        [Reactive]
        public partial float ThetaZ
        {
            get;
            set;
        } = 25f;

        [Reactive]
        public partial float Epsilon
        {
            get;
            set;
        } = 1.2f;

        /// <summary>
        /// </summary>
        /// <remarks></remarks>
        [Reactive]
        public partial String Path
        {
            get;
            set;
        } = "Please input displaying picture path here";

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
