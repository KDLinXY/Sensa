using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Sensa
{
    internal sealed class MouseDevice
    {
        public IntPtr Handle;
        public string DevicePath;
        public string Id;
        public string DisplayName;
    }

    internal static class NativeMouse
    {
        internal const int WmInput = 0x00FF;
        internal const int WmDeviceChange = 0x0219;

        private const uint RimTypeMouse = 0;
        private const uint RidHeader = 0x10000005;
        private const uint RidiDeviceName = 0x20000007;
        private const uint SpiGetMouse = 0x0003;
        private const uint SpiSetMouse = 0x0004;
        private const uint SpiGetMouseSpeed = 0x0070;
        private const uint SpiSetMouseSpeed = 0x0071;
        private const uint SpifSendChange = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDeviceList
        {
            public IntPtr Device;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList(IntPtr devices, ref uint count, uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, int[] value, uint flags);

        internal static void RegisterForInput(IntPtr window)
        {
            var devices = new[]
            {
                new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0x00000100, Target = window }
            };
            RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
        }

        internal static IntPtr GetInputDevice(IntPtr rawInput)
        {
            uint size = (uint)Marshal.SizeOf(typeof(RawInputHeader));
            uint headerSize = size;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(rawInput, RidHeader, buffer, ref size, headerSize) == UInt32.MaxValue)
                    return IntPtr.Zero;
                var header = (RawInputHeader)Marshal.PtrToStructure(buffer, typeof(RawInputHeader));
                return header.Type == RimTypeMouse ? header.Device : IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        internal static string GetDeviceId(IntPtr handle)
        {
            return ParseDeviceId(GetDevicePath(handle));
        }

        internal static List<MouseDevice> EnumerateDevices()
        {
            var result = new List<MouseDevice>();
            uint count = 0;
            uint itemSize = (uint)Marshal.SizeOf(typeof(RawInputDeviceList));
            if (GetRawInputDeviceList(IntPtr.Zero, ref count, itemSize) != 0 || count == 0)
                return result;

            IntPtr buffer = Marshal.AllocHGlobal((int)(count * itemSize));
            try
            {
                if (GetRawInputDeviceList(buffer, ref count, itemSize) == UInt32.MaxValue)
                    return result;
                for (int i = 0; i < count; i++)
                {
                    var item = (RawInputDeviceList)Marshal.PtrToStructure(
                        new IntPtr(buffer.ToInt64() + i * itemSize), typeof(RawInputDeviceList));
                    if (item.Type != RimTypeMouse) continue;
                    string path = GetDevicePath(item.Device);
                    string id = ParseDeviceId(path);
                    if (id == null) continue;
                    result.Add(new MouseDevice
                    {
                        Handle = item.Device,
                        DevicePath = path,
                        Id = id,
                        DisplayName = ProfileCatalog.GetDisplayName(id)
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        private static string GetDevicePath(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return "";
            uint size = 0;
            GetRawInputDeviceInfo(handle, RidiDeviceName, null, ref size);
            if (size == 0) return "";
            var text = new StringBuilder((int)size);
            return GetRawInputDeviceInfo(handle, RidiDeviceName, text, ref size) == UInt32.MaxValue
                ? "" : text.ToString();
        }

        internal static string ParseDeviceId(string devicePath)
        {
            if (String.IsNullOrEmpty(devicePath)) return null;
            var match = Regex.Match(devicePath, "VID_([0-9A-Fa-f]{4}).*PID_([0-9A-Fa-f]{4})");
            return match.Success
                ? (match.Groups[1].Value + ":" + match.Groups[2].Value).ToUpperInvariant()
                : null;
        }

        internal static int GetSpeed()
        {
            IntPtr value = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                return SystemParametersInfo(SpiGetMouseSpeed, 0, value, 0)
                    ? Math.Max(1, Math.Min(20, Marshal.ReadInt32(value))) : 10;
            }
            finally { Marshal.FreeHGlobal(value); }
        }

        internal static bool SetSpeed(int speed)
        {
            speed = Math.Max(1, Math.Min(20, speed));
            return SystemParametersInfo(SpiSetMouseSpeed, 0, new IntPtr(speed), SpifSendChange);
        }

        internal static bool GetAcceleration()
        {
            var values = new int[3];
            return SystemParametersInfo(SpiGetMouse, 0, values, 0) && values[2] != 0;
        }

        internal static bool SetAcceleration(bool enabled)
        {
            var values = new int[3];
            if (!SystemParametersInfo(SpiGetMouse, 0, values, 0)) return false;
            values[2] = enabled ? 1 : 0;
            return SystemParametersInfo(SpiSetMouse, 0, values, SpifSendChange);
        }
    }
}
