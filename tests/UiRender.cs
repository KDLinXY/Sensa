using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sensa
{
    internal static class UiRender
    {
        [STAThread]
        internal static int Main(string[] args)
        {
            if (args.Length != 1) return 2;
            var app = new Application();
            var window = new MainWindow();
            window.Loaded += delegate
            {
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(452, 730, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render((Visual)window.Content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(args[0])) encoder.Save(stream);
                    window.SetMode(true);
                    window.UpdateLayout();
                    bitmap = new RenderTargetBitmap(452, 730, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render((Visual)window.Content);
                    encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(args[0]), "dpi-render.png"))) encoder.Save(stream);
                    window.ExitApplication();
                }), DispatcherPriority.ApplicationIdle);
            };
            app.Run(window);
            return 0;
        }
    }
}
