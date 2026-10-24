using System;
using System.Runtime.InteropServices;

namespace Sensa
{
    internal static class GlobalHotkeys
    {
        internal const int WmHotkey = 0x0312;
        internal const int FineId = 5101;
        internal const int BalancedId = 5102;
        internal const int SwiftId = 5103;
        internal const int ShowId = 5104;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        internal static bool Register(IntPtr window, bool useShift)
        {
            Unregister(window);
            uint modifiers = ModControl | ModNoRepeat | (useShift ? ModShift : ModAlt);
            bool fine = RegisterHotKey(window, FineId, modifiers, 0x31);
            bool balanced = RegisterHotKey(window, BalancedId, modifiers, 0x32);
            bool swift = RegisterHotKey(window, SwiftId, modifiers, 0x33);
            bool show = RegisterHotKey(window, ShowId, modifiers, 0x53);
            return fine && balanced && swift && show;
        }

        internal static void Unregister(IntPtr window)
        {
            UnregisterHotKey(window, FineId);
            UnregisterHotKey(window, BalancedId);
            UnregisterHotKey(window, SwiftId);
            UnregisterHotKey(window, ShowId);
        }

        internal static string ModifierName(bool useShift)
        {
            return useShift ? "Ctrl+Shift" : "Ctrl+Alt";
        }
    }
}
