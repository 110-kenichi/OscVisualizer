using OscVisualizer.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI.SourceGenerators;
using Avalonia.Controls;
using System.Threading;

namespace OscVisualizer.ViewModels
{
    internal partial class MandelZoomAbyssViewModel : ViewModelBase, IDisposable
    {
        /// <summary>
        /// </summary>
        /// <remarks></remarks>
        [Reactive]
        public partial float Rotate
        {
            get;
            set;
        } = -25f;

        [Reactive]
        public partial float Threshold
        {
            get;
            set;
        } = 0.5f;

        [Reactive]
        public partial float Epsilon
        {
            get;
            set;
        } = 5f;

        [Reactive]
        public partial int PictureSize
        {
            get;
            set;
        } = 256;

        // 実際に使用する値（遅延適用後）
        [Reactive]
        public partial float AppliedThreshold
        {
            get;
            set;
        } = 0.5f;

        [Reactive]
        public partial float AppliedEpsilon
        {
            get;
            set;
        } = 5f;

        private CancellationTokenSource? _updateCts;
        private const int DelayMs = 500; // ドラッグ終了後の遅延時間

        public MandelZoomAbyssViewModel()
        {
            // Thresholdが変わったときに遅延更新
            this.WhenAnyValue(x => x.Threshold)
                .Subscribe(_ => ScheduleThresholdUpdate());

            // Epsilonが変わったときに遅延更新
            this.WhenAnyValue(x => x.Epsilon)
                .Subscribe(_ => ScheduleEpsilonUpdate());
        }

        private void ScheduleThresholdUpdate()
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var cts = _updateCts;

            Task.Delay(DelayMs, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    AppliedThreshold = Threshold;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ScheduleEpsilonUpdate()
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var cts = _updateCts;

            Task.Delay(DelayMs, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    AppliedEpsilon = Epsilon;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _updateCts?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
