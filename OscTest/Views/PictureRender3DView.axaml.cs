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

public partial class PictureRender3DView : UserControl
{
    private TextBox? _pathTextBox;

    public PictureRender3DView()
    {
        InitializeComponent();
        this.Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PictureRender3DView] Loaded event fired");
        
        _pathTextBox = this.FindControl<TextBox>("PathTextBox");
        var browseButton = this.FindControl<Button>("BrowseButton");

        if (browseButton != null)
        {
            Debug.WriteLine("[PictureRender3DView] BrowseButton found");
            browseButton.Click += OnBrowseButtonClick;
        }

        // ドラッグ&ドロップハンドラーを最後に登録
        SetupDragDrop();
    }

    private void SetupDragDrop()
    {
        try
        {
            // DragOverイベント
            this.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                Debug.WriteLine($"[SetupDragDrop.DragOver] Called");
                if (e.Data.Contains(DataFormats.Files))
                {
                    e.DragEffects = DragDropEffects.Copy;
                    e.Handled = true;
                    Debug.WriteLine("[SetupDragDrop.DragOver] Files detected - Copy");
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            // DropEvent
            this.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                Debug.WriteLine($"[SetupDragDrop.Drop] Called");
                if (e.Data.Contains(DataFormats.Files))
                {
                    var files = e.Data.GetFiles();
                    if (files != null && files.Any())
                    {
                        var filePath = files.First().Path.LocalPath;
                        Debug.WriteLine($"[SetupDragDrop.Drop] File: {filePath}");

                        if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
                        {
                            viewModel.Path = filePath;
                        }
                        e.Handled = true;
                    }
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            Debug.WriteLine("[SetupDragDrop] Handlers registered");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetupDragDrop] Exception: {ex}");
        }
    }

    private async void OnBrowseButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Debug.WriteLine("[OnBrowseButtonClick] Starting file dialog");

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null)
            {
                Debug.WriteLine("[OnBrowseButtonClick] Window not found");
                return;
            }

            var options = new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files") 
                    { 
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" }
                    },
                    new FilePickerFileType("All Files") 
                    { 
                        Patterns = new[] { "*.*" }
                    }
                }
            };

            var result = await window.StorageProvider.OpenFilePickerAsync(options);

            if (result != null && result.Count > 0)
            {
                var filePath = result[0].Path.LocalPath;
                Debug.WriteLine($"[OnBrowseButtonClick] Selected file: {filePath}");

                if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
                {
                    viewModel.Path = filePath;
                    Debug.WriteLine($"[OnBrowseButtonClick] Path updated in ViewModel");
                }
            }
            else
            {
                Debug.WriteLine("[OnBrowseButtonClick] No file selected");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OnBrowseButtonClick] Exception: {ex}");
        }
    }
}