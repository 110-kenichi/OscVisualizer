using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OscVisualizer.Views;

public partial class PictureRender3DView : UserControl
{
    private TextBox? _pathTextBox;
    private Grid? _mainGrid;

    public PictureRender3DView()
    {
        InitializeComponent();
        this.Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PictureRender3DView] Loaded event fired");
        
        _pathTextBox = this.FindControl<TextBox>("PathTextBox");
        _mainGrid = this.FindControl<Grid>("MainGrid");
        var browseButton = this.FindControl<Button>("BrowseButton");

        if (_pathTextBox != null)
        {
            Debug.WriteLine("[PictureRender3DView] PathTextBox found");
            SetupTextBoxDragDrop();
        }

        if (_mainGrid != null)
        {
            Debug.WriteLine("[PictureRender3DView] MainGrid found");
            SetupGridDragDrop();
        }

        if (browseButton != null)
        {
            Debug.WriteLine("[PictureRender3DView] BrowseButton found");
            browseButton.Click += OnBrowseButtonClick;
        }

        // UserControl全体にもハンドラーを登録
        Debug.WriteLine("[PictureRender3DView] Setting up UserControl drag/drop");
        this.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        this.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
    }

    private void SetupTextBoxDragDrop()
    {
        if (_pathTextBox == null) return;

        // Bubble戦略でハンドラーを登録
        _pathTextBox.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        _pathTextBox.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);

        Debug.WriteLine("[SetupTextBoxDragDrop] Completed");
    }

    private void SetupGridDragDrop()
    {
        if (_mainGrid == null) return;

        // Bubble戦略でハンドラーを登録
        _mainGrid.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        _mainGrid.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);

        Debug.WriteLine("[SetupGridDragDrop] Completed");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        Debug.WriteLine($"[OnDragOver] Sender: {sender?.GetType().Name}, Files: {e.Data.Contains(DataFormats.Files)}");
        
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            Debug.WriteLine("[OnDragOver] -> Copy allowed");
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            Debug.WriteLine("[OnDragOver] -> No files");
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Debug.WriteLine($"[OnDrop] Sender: {sender?.GetType().Name}");
        HandleFileDrop(e);
    }

    private void HandleFileDrop(DragEventArgs e)
    {
        try
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                var fileCount = files?.Count() ?? 0;
                Debug.WriteLine($"[HandleFileDrop] Files count: {fileCount}");

                if (files != null && fileCount > 0)
                {
                    var filePath = files.First().Path.LocalPath;
                    Debug.WriteLine($"[HandleFileDrop] First file: {filePath}");

                    if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
                    {
                        viewModel.Path = filePath;
                        Debug.WriteLine($"[HandleFileDrop] Path updated in ViewModel");
                    }
                    else
                    {
                        Debug.WriteLine($"[HandleFileDrop] ViewModel not found. DataContext type: {this.DataContext?.GetType().Name}");
                    }
                }
            }
            else
            {
                Debug.WriteLine("[HandleFileDrop] No files in drop data");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HandleFileDrop] Exception: {ex}");
        }
    }

    private async void OnBrowseButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                AllowMultiple = false,
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Image Files", Extensions = new System.Collections.Generic.List<string> { "jpg", "jpeg", "png", "bmp", "gif" } },
                    new FileDialogFilter { Name = "All Files", Extensions = new System.Collections.Generic.List<string> { "*" } }
                }
            };

            var window = TopLevel.GetTopLevel(this) as Window;
            var result = await dialog.ShowAsync(window);

            if (result != null && result.Length > 0)
            {
                var filePath = result[0];
                Debug.WriteLine($"[OnBrowseButtonClick] Selected file: {filePath}");

                if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
                {
                    viewModel.Path = filePath;
                    Debug.WriteLine($"[OnBrowseButtonClick] Path updated in ViewModel");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OnBrowseButtonClick] Exception: {ex}");
        }
    }
}