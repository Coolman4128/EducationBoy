using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EducationBoy.Emulator;

namespace EducationBoy.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public WriteableBitmap FramebufferBitmap { get; }

    private const int Width = 160;
    private const int Height = 144;
    private const int BytesPerPixel = 4;
    private readonly byte[] _framebuffer = new byte[Width * Height * BytesPerPixel];
    private string _loadedRomName = "No ROM loaded";

    public EmulatorCore Emulator { get; private set; }

    public MainWindowViewModel()
    {
        FramebufferBitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        Emulator = new EmulatorCore(this);

    }

    public void UpdateFrame(byte[] framebuffer)
    {
        using var fb = FramebufferBitmap.Lock();
        Marshal.Copy(framebuffer, 0, fb.Address, framebuffer.Length);
        // Force UI to refresh the binding
        OnPropertyChanged(nameof(FramebufferBitmap));
    }


    public string LoadedRomName
    {
        get => _loadedRomName;
        private set => SetProperty(ref _loadedRomName, value);
    }

    [RelayCommand]
    public async Task LoadRom()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var window = lifetime?.MainWindow;
        if (window is null)
        {
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Load ROM",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new FilePickerFileType("Game Boy ROM") { Patterns = new[] { "*.gb", "*.gbc", "*.bin" } },
                FilePickerFileTypes.All
            }
        };

        var selection = await window.StorageProvider.OpenFilePickerAsync(options);
        if (selection is null || selection.Count == 0)
        {
            return;
        }

        var file = selection[0];
        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        var romBytes = ms.ToArray();
        Emulator.LoadRom(romBytes, file.Name);
        LoadedRomName = file.Name;
    }



}
