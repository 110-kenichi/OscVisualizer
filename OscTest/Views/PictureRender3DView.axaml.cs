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
    public PictureRender3DView()
    {
        InitializeComponent();

        DragDrop.AddDropHandler(this, OnDrop);
        DragDrop.AddDragOverHandler(this, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
        {
            // Accept file drops only; reject everything else
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
        {
            if (e.DataTransfer.Formats.Contains(DataFormat.File))
            {
                var files = e.DataTransfer.Items;
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        // Process each dropped file
                        viewModel.Path = file.TryGetFile()!.Path.LocalPath;
                        break;
                    }
                }
            }
        }
    }

    private async void OnBrowseButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null)
            {
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

                if (this.DataContext is ViewModels.PictureRender3DViewModel viewModel)
                {
                    viewModel.Path = filePath;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OnBrowseButtonClick] Exception: {ex}");
        }
    }
}