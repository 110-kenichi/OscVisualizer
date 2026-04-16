using Avalonia;
using Avalonia.Controls;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using OpenTK.Audio.OpenAL;
using OpenTK.Compute.OpenCL;
using OscVisualizer.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq; // 追加: IObservable<T>.Subscribe の Action オーバーロードを解決するため
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OscVisualizer.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private WasapiLoopbackCapture? _capture;

    private object alLockObject = new object();
    private ALDevice _alDevice;
    private int _alSource;
    private ALContext _alContext;
    private int[]? _sampleBufferIds;
    private XYProcessor? _xyProcessor;

    public ObservableCollection<string> PlaybackDevices { get; } = new();

    private string? _selectedDevice;

    /// <summary>
    /// Gets or sets the currently selected device.
    /// </summary>
    /// <remarks>This property can be set to null to indicate that no device is selected.</remarks>
    public string? SelectedDevice
    {
        get => _selectedDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedDevice, value);
    }

    private double _speedScale = 1.0;
    /// <summary>
    /// Gets or sets the scaling factor applied to speed calculations.
    /// </summary>
    /// <remarks>Changing this property raises a property change notification and updates the speed scale of
    /// the associated XY processor if it is initialized.</remarks>
    public double SpeedScale
    {
        get
        {
            return _speedScale;
        }
        set
        {
            if (_speedScale != value)
            {
                _speedScale = value;
                this.RaisePropertyChanged(nameof(SpeedScale));
                if (_xyProcessor != null)
                    _xyProcessor.SpeedScale = _speedScale;
            }
        }
    }

    private int _phaseShift = 1;
    /// <summary>
    /// Gets or sets the scaling factor applied to speed calculations.
    /// </summary>
    /// <remarks>Changing this property raises a property change notification and updates the speed scale of
    /// the associated XY processor if it is initialized.</remarks>
    public int PhaseShift
    {
        get
        {
            return _phaseShift;
        }
        set
        {
            if (_phaseShift != value)
            {
                _phaseShift = value;
                this.RaisePropertyChanged(nameof(PhaseShift));
                if (_xyProcessor != null)
                    _xyProcessor.PhaseShift = _phaseShift;
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [Reactive]
    public partial bool InvertX { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [Reactive]
    public partial bool InvertY { get; set; }

    public List<IAudioVisualizer> VisualizerTypes { get; } =
    [
        new SpectrumAnalyzer(),
        new BandLevelMeter(),
        new WaveFlow3D(),
        new WaveCircle(),
        new WavePolarCircle(),
        new WaveFlame(),
        new WaveTwistedWarp(),
        new RetroCarStereo(),
        new DiscoBall(),
        new Synthwave(),
        new LaserDance(),
        new MexicanHat(),
    ];

    [Reactive]
    public partial IAudioVisualizer? SelectedVisualizer { get; set; }

    /// <summary>
    /// Gets the view control associated with the currently selected visualizer.
    /// </summary>
    /// <remarks>This property returns the visualizer view for the selected visualizer. If no visualizer is
    /// selected, the property returns null.</remarks>
    [Reactive]
    public partial Avalonia.Controls.Control? SelectedVisualizerView
    {
        set;
        get;
    }

    /// <summary>
    /// Initializes a new instance of the MainViewModel class, setting up audio processing and playback using default
    /// parameters.
    /// </summary>
    /// <remarks>This constructor configures the audio device and context, initializes the XYProcessor with a
    /// default set of points, generates initial audio buffers, and starts audio playback. It also sets up a timer to
    /// periodically update audio processing. The initial state includes a trigger for a specific audio event. The
    /// caller does not need to perform additional setup after instantiation.</remarks>
    public MainViewModel()
    {
        UpdateOutputDeviceList();

        LoadSettings();

        var TimerObservable = Observable.Interval(TimeSpan.FromMilliseconds(5));
        TimerObservable.Subscribe(x =>
        {
            // バッファが空になって止まった
            lock (alLockObject)
            {
                if (_alSource != 0)
                {
                    var state = (ALSourceState)AL.GetSource(_alSource, ALGetSourcei.SourceState);
                    if (state == ALSourceState.Stopped)
                        AL.SourcePlay(_alSource);
                }
            }

            _xyProcessor?.Update();
        }).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.InvertX).Subscribe(v =>
        {
            if (_xyProcessor != null)
                _xyProcessor.InvertX = v;
        }).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.InvertY).Subscribe(v =>
        {
            if (_xyProcessor != null)
                _xyProcessor.InvertY = v;
        }).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SelectedVisualizer).Subscribe(v =>
        {
            SelectedVisualizerView = SelectedVisualizer!.VisualizerView;
        }).DisposeWith(_disposables);
    }

    private readonly CompositeDisposable _disposables = new();

    public void Dispose()
    {
        _disposables.Dispose();

        try
        {
            SaveSettings();

            Stop();
        }
        catch
        {
            // 破棄時の例外は握りつぶす
        }
    }



    // UTF-8ポインタをC#のstringに変換する補助メソッド
    public static string PointerToUtf8String(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;

        // 終端のヌル文字を探して長さを取得
        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0)
        {
            length++;
        }

        // バイト配列にコピー
        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);

        // UTF-8としてデコード
        return Encoding.UTF8.GetString(buffer);
    }


    /// <summary>
    /// Used to convert a OpenAL string list to a C# List.
    /// </summary>
    /// <param name="alList">A pointer to the AL list. Usually returned from GetStringList like AL functions.</param>
    /// <returns>The string list.</returns>
    internal static unsafe List<string> ALStringListToList(byte* alList)
    {
        if (alList == (byte*)0)
        {
            return new List<string>();
        }

        var strings = new List<string>();

        byte* currentPos = alList;
        while (true)
        {
            var currentString = PointerToUtf8String(new IntPtr(currentPos));
            if (string.IsNullOrEmpty(currentString))
            {
                break;
            }

            strings.Add(currentString);
            currentPos += currentString.Length + 1;
        }

        return strings;
    }

    public static string? FindOpenALDeviceName(string waveOutName)
    {
        // OpenAL Soft のデバイス一覧を取得
        unsafe
        {
            byte* result = ALC.GetStringPtr(new ALDevice(IntPtr.Zero), AlcGetString.AllDevicesSpecifier);
            // C#のデフォルトのマーシャリングを使わず、手動でUTF-8として読み込む
            var devices = ALStringListToList(result);
            // WaveOut のデバイス名を含む OpenAL デバイス名を探す
            return devices.FirstOrDefault(d => d.Contains(waveOutName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>This function opens a device by name.</summary>
    /// <param name="devicename">A null-terminated string describing a device.</param>
    /// <returns>Returns a pointer to the opened device. The return value will be NULL if there is an error.</returns>
    [DllImport("OpenAL32.dll", EntryPoint = "alcOpenDevice", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern ALDevice OpenDevice([MarshalAs(UnmanagedType.LPUTF8Str)] string devicename);

    [ReactiveCommand]
    public void Play()
    {
        Stop();

        if (SelectedDevice == null)
            return;

        lock (alLockObject)
        {
            var dn = FindOpenALDeviceName(SelectedDevice);

            //_alDevice = ALC.OpenDevice(dn);
            _alDevice = OpenDevice(dn!);

            //// 拡張が使えるかチェック
            //bool hasFreq = ALC.IsExtensionPresent(_alDevice, "ALC_SOFT_device_clock");
            //if (hasFreq)
            //{
            //    int freq;
            //    ALC.GetInteger(_alDevice, (AlcGetInteger)0x1007, 1, out freq);
            //    Console.WriteLine("Device frequency: " + freq);
            //}
            //else
            //{
            //    Console.WriteLine("Device does not support ALC_SOFT_device_clock");
            //}

            ALContextAttributes attrib = new ALContextAttributes();
            _alContext = ALC.CreateContext(_alDevice, attrib);
            ALC.MakeContextCurrent(_alContext);

            _alSource = AL.GenSource();
            _sampleBufferIds = AL.GenBuffers(8);

            var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(device);
            var mixFormat = device.AudioClient.MixFormat;
            int sampleRate = mixFormat.SampleRate;

            _xyProcessor = new XYProcessor(_alSource, sampleRate, sampleRate / 60);
            _xyProcessor.SpeedScale = _speedScale;
            _xyProcessor.PhaseShift = _phaseShift;
            _xyProcessor.InvertX = InvertX;
            _xyProcessor.InvertY = InvertY;

            // 初期バッファを埋める
            foreach (var b in _sampleBufferIds)
            {
                var pcm = _xyProcessor.GenerateXYBuffer();
                AL.BufferData(b, ALFormat.StereoFloat32Ext, pcm, sampleRate);
                AL.SourceQueueBuffer(_alSource, b);
            }

            AL.SourcePlay(_alSource);
        }

        //var fmt = _capture.WaveFormat;
        _capture.DataAvailable += (s, e) =>
            {
                _xyProcessor.SetPoints(SelectedVisualizer!.ProcessAudio((WasapiCapture)s!, e));
            };
        _capture?.StartRecording();
    }


    [ReactiveCommand]
    public void Stop()
    {
        _capture?.StopRecording();
        _capture?.RecordingStopped -= (s, e) => _capture?.Dispose();

        lock (alLockObject)
        {
            if (_alSource != 0)
            {
                AL.SourceStop(_alSource);
                AL.DeleteSource(_alSource);
                _alSource = 0;
            }

            if (_sampleBufferIds != null)
            {
                AL.DeleteBuffers(_sampleBufferIds);
            }

            if (_alContext != ALContext.Null)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_alContext);
                _alContext = ALContext.Null;
            }

            if (_alDevice != ALDevice.Null)
            {
                ALC.CloseDevice(_alDevice);
                _alDevice = ALDevice.Null;
            }
        }
    }

    [ReactiveCommand]
    public void UpdateOutputDeviceList()
    {
        PlaybackDevices.Clear();

        int count = WaveOut.DeviceCount;

        for (int i = 0; i < count; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            PlaybackDevices.Add($"{caps.ProductName}");
        }

        if (PlaybackDevices.Count > 0)
            SelectedDevice = PlaybackDevices[0];
    }

    //https://github.com/reactiveui/ReactiveUI.SourceGenerators

    [ReactiveCommand]
    public void StartVisualyzer()
    {
        Play();
    }

    private static string GetSettingsPath()
    {
        var appName = "OscVisualizer";
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appName
        );
        Directory.CreateDirectory(configDir);

        var settingsPath = Path.Combine(configDir, "settings.json");
        return settingsPath;
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { SelectedDevice, SpeedScale, PhaseShift, SelectedVisualizer!.VisualizerName, InvertX, InvertY });

            string settingsPath = GetSettingsPath();

            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // 保存失敗は握りつぶす
        }
        foreach (var vis in VisualizerTypes)
            vis.SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            string settingsPath = GetSettingsPath();

            SelectedVisualizer = VisualizerTypes[0];

            if (!File.Exists(settingsPath))
                return;

            var json = File.ReadAllText(settingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);

            if (data != null)
            {
                if (data.SelectedDevice != null && PlaybackDevices.Contains(data.SelectedDevice))
                    SelectedDevice = data.SelectedDevice;
                SpeedScale = data.SpeedScale;
                PhaseShift = data.PhaseShift;
                SelectedVisualizer = VisualizerTypes.FirstOrDefault(v => v.VisualizerName == data.VisualizerName) ?? VisualizerTypes[0];
                InvertX = data.InvertX;
                InvertY = data.InvertY;
            }
        }
        catch
        {
            // 読み込み失敗は握りつぶす
        }
        foreach (var vis in VisualizerTypes)
            vis.LoadSettings();
    }

    private class SettingsData
    {
        public string? SelectedDevice { get; set; }
        public double SpeedScale { get; set; }
        public int PhaseShift { get; set; }
        public string? VisualizerName { get; set; }
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }
    }

}
