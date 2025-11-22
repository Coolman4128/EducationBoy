using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EducationBoy.Emulator;
using EducationBoy.ViewModels;

namespace EducationBoy.Views;

public partial class MainWindow : Window
{
    private static readonly Dictionary<Key, GameBoyButton> KeyMap = new()
    {
        { Key.Right, GameBoyButton.Right },
        { Key.Left,  GameBoyButton.Left },
        { Key.Up,    GameBoyButton.Up },
        { Key.Down,  GameBoyButton.Down },
        { Key.Z,     GameBoyButton.A },
        { Key.X,     GameBoyButton.B },
        { Key.Enter, GameBoyButton.Start },
        { Key.RightShift, GameBoyButton.Select },
        { Key.Space, GameBoyButton.Select }
    };

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        Opened += (_, _) => Focus();

        DataContextChanged += (_, __) => SubscribeBitmapInvalidation();
        SubscribeBitmapInvalidation();
    }

    private void SubscribeBitmapInvalidation()
    {
        if (DataContext is MainWindowViewModel vm && FrameImage is not null)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.FramebufferBitmap))
                {
                    FrameImage.InvalidateVisual();
                }
            };
            FrameImage.InvalidateVisual();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!TryMapButton(e.Key, out var button))
            return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.Emulator.SetButtonState(button, true);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!TryMapButton(e.Key, out var button))
            return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.Emulator.SetButtonState(button, false);
            e.Handled = true;
        }
    }

    private static bool TryMapButton(Key key, out GameBoyButton button)
    {
        return KeyMap.TryGetValue(key, out button);
    }
}
