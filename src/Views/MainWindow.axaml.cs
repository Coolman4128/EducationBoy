using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EducationBoy.Views;

public partial class MainWindow : Window
{


    public MainWindow()
    {
        InitializeComponent();

        // Subscribe to bitmap property change to invalidate the Image
        this.DataContextChanged += (_, __) => SubscribeBitmapInvalidation();
        SubscribeBitmapInvalidation();
    }

    private void SubscribeBitmapInvalidation()
    {
        if (DataContext is EducationBoy.ViewModels.MainWindowViewModel vm && FrameImage is not null)
        {
            // Unsubscribe previous if needed (not strictly necessary here)
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.FramebufferBitmap))
                {
                    FrameImage.InvalidateVisual();
                }
            };
            // Also invalidate immediately in case already updated
            FrameImage.InvalidateVisual();
        }
    }




}
