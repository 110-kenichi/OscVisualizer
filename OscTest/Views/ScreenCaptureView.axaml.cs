using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace OscVisualizer.Views;

public partial class ScreenCaptureView : UserControl
{
    public ScreenCaptureView()
    {
        InitializeComponent();
    }
}