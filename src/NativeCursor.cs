using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Sensa
{
    internal static class NativeCursor
    {
        internal static readonly int[] Sizes = { 32, 48, 64, 96, 128 };
        private const uint SpiSetCursors = 0x0057;
        private const uint SpifUpdateIniFile = 0x0001;
        private const uint SpifSendChange = 0x0002;
        private static readonly int[] CursorIds =
        {
            32512, 32513, 32514, 32515, 32516, 32640, 32641, 32642,
            32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public uint Size;
            public uint Flags;
            public IntPtr Cursor;
            public System.Drawing.Point ScreenPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool Icon;
            public uint HotspotX;
            public uint HotspotY;
            public IntPtr MaskBitmap;
            public IntPtr ColorBitmap;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap
        {
            public uint Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPixel;
            public IntPtr Bits;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref CursorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetObject(IntPtr graphics, int bufferSize, out Bitmap bitmap);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr graphics);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr cursor, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconIndirect([In] ref IconInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        internal static int GetSize()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", false))
            {
                object value = key == null ? null : key.GetValue("CursorBaseSize", 32);
                int size;
                return value != null && int.TryParse(value.ToString(), out size) && size >= 1 && size <= 1024 ? size : 32;
            }
        }

        internal static bool SetSize(int size)
        {
            if (size < 32 || size > 256) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
                {
                    if (key == null) return false;
                    key.SetValue("CursorBaseSize", size, RegistryValueKind.DWord);
                }
                SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);
                return size == 32 || ApplyScaledCursors(size);
            }
            catch { return false; }
        }

        private static bool ApplyScaledCursors(int size)
        {
            bool normalCursorUpdated = false;
            foreach (int id in CursorIds)
            {
                IntPtr original = LoadCursor(IntPtr.Zero, new IntPtr(id));
                if (original == IntPtr.Zero) continue;
                int originalWidth = GetIconWidth(original);
                if (originalWidth <= 0) continue;
                int targetWidth = Math.Max(1, (int)Math.Round(originalWidth * size / 32.0));
                IntPtr scaled = CreateScaledCursor(original, originalWidth, targetWidth);
                if (scaled == IntPtr.Zero) continue;
                if (SetSystemCursor(scaled, (uint)id) && id == 32512) normalCursorUpdated = true;
            }
            return normalCursorUpdated;
        }

        private static int GetIconWidth(IntPtr icon)
        {
            IconInfo info;
            if (!GetIconInfo(icon, out info)) return 0;
            IntPtr handle = info.ColorBitmap != IntPtr.Zero ? info.ColorBitmap : info.MaskBitmap;
            int width = 0;
            if (handle != IntPtr.Zero)
            {
                Bitmap bitmap;
                if (GetObject(handle, Marshal.SizeOf(typeof(Bitmap)), out bitmap) != 0) width = bitmap.Width;
            }
            if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
            if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
            return width;
        }

        private static IntPtr CreateScaledCursor(IntPtr original, int originalWidth, int targetWidth)
        {
            IconInfo source;
            if (!GetIconInfo(original, out source)) return IntPtr.Zero;
            uint hotspotX = source.HotspotX;
            uint hotspotY = source.HotspotY;
            if (source.ColorBitmap != IntPtr.Zero) DeleteObject(source.ColorBitmap);
            if (source.MaskBitmap != IntPtr.Zero) DeleteObject(source.MaskBitmap);

            using (var canvas = new System.Drawing.Bitmap(targetWidth, targetWidth,
                       System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(canvas))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    var icon = System.Drawing.Icon.FromHandle(original);
                    graphics.DrawIcon(icon, new System.Drawing.Rectangle(0, 0, targetWidth, targetWidth));
                }
                IntPtr temporary = canvas.GetHicon();
                if (temporary == IntPtr.Zero) return IntPtr.Zero;
                IconInfo info;
                IntPtr result = IntPtr.Zero;
                if (GetIconInfo(temporary, out info))
                {
                    info.Icon = false;
                    info.HotspotX = (uint)Math.Round(hotspotX * targetWidth / (double)originalWidth);
                    info.HotspotY = (uint)Math.Round(hotspotY * targetWidth / (double)originalWidth);
                    result = CreateIconIndirect(ref info);
                    if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
                    if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                }
                DestroyIcon(temporary);
                return result;
            }
        }

        internal static int GetCurrentCursorPixelSize()
        {
            var cursor = new CursorInfo { Size = (uint)Marshal.SizeOf(typeof(CursorInfo)) };
            if (!GetCursorInfo(ref cursor) || cursor.Cursor == IntPtr.Zero) return 0;
            IconInfo icon;
            if (!GetIconInfo(cursor.Cursor, out icon)) return 0;
            IntPtr bitmapHandle = icon.ColorBitmap != IntPtr.Zero ? icon.ColorBitmap : icon.MaskBitmap;
            int width = 0;
            if (bitmapHandle != IntPtr.Zero)
            {
                Bitmap bitmap;
                if (GetObject(bitmapHandle, Marshal.SizeOf(typeof(Bitmap)), out bitmap) != 0) width = bitmap.Width;
            }
            if (icon.ColorBitmap != IntPtr.Zero) DeleteObject(icon.ColorBitmap);
            if (icon.MaskBitmap != IntPtr.Zero) DeleteObject(icon.MaskBitmap);
            return width;
        }
    }
}
