using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sensa
{
    internal static class RazerDpi
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string path, uint access, FileShare share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr handle, byte[] report, uint length);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr handle, byte[] report, uint length);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr data);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr data);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);

        [StructLayout(LayoutKind.Sequential)]
        private struct HidCaps
        {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps,
                NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps,
                NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps,
                NumberFeatureDataIndices;
        }

        internal static bool TryGetDpi(string deviceId, out int dpi, out string error)
        {
            return TryExchange(deviceId, false, 0, out dpi, out error);
        }

        internal static bool TrySetDpi(string deviceId, int dpi, out string error)
        {
            int acknowledged;
            return TryExchange(deviceId, true, dpi, out acknowledged, out error);
        }

        private static bool TryExchange(string deviceId, bool write, int requestedDpi,
            out int dpi, out string error)
        {
            dpi = 0;
            error = null;
            if (deviceId != "1532:0098")
            {
                error = "该型号不支持硬件 DPI 写入";
                return false;
            }
            if (write && (requestedDpi < 100 || requestedDpi > 6400))
            {
                error = "DPI 超出该型号允许范围";
                return false;
            }

            var device = NativeMouse.EnumerateDevices().FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
            {
                error = "未检测到 Razer DeathAdder Essential";
                return false;
            }

            IntPtr handle = CreateFile(device.DevicePath, GenericWrite,
                FileShare.ReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == InvalidHandle)
            {
                error = "无法打开鼠标 HID 接口（错误 " + Marshal.GetLastWin32Error() + "），请退出 Synapse 后重试";
                return false;
            }

            try
            {
                IntPtr preparsed;
                if (HidD_GetPreparsedData(handle, out preparsed))
                {
                    HidCaps caps;
                    caps.Reserved = new ushort[17];
                    if (HidP_GetCaps(preparsed, out caps) == 0x110000)
                        Console.WriteLine("INFO: Razer feature report length " + caps.FeatureLength);
                    HidD_FreePreparsedData(preparsed);
                }
                byte[] request = BuildReport(write, write ? requestedDpi : 0);
                if (!HidD_SetFeature(handle, request, (uint)request.Length))
                {
                    error = "DPI 命令发送失败（错误 " + Marshal.GetLastWin32Error() + "）";
                    return false;
                }

                System.Threading.Thread.Sleep(15);
                byte[] response = new byte[91];
                if (!HidD_GetFeature(handle, response, (uint)response.Length))
                {
                    error = "未收到鼠标 DPI 响应（错误 " + Marshal.GetLastWin32Error() + "）";
                    return false;
                }
                byte status = response[1];
                if (status != 0x00 && status != 0x01)
                {
                    error = "鼠标拒绝 DPI 命令（状态 0x" + status.ToString("X2") + "）";
                    return false;
                }
                if (response[7] != 0x04 || response[8] != (write ? 0x05 : 0x85))
                {
                    error = "鼠标返回了无法识别的 DPI 响应";
                    return false;
                }

                if (write) return true;
                dpi = (response[10] << 8) | response[11];
                if (dpi < 100 || dpi > 45000)
                {
                    error = "设备返回了无效的 DPI 数值";
                    dpi = 0;
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        internal static byte[] BuildReport(bool write, int dpi)
        {
            var report = new byte[91];
            report[2] = 0xFF;
            report[6] = 0x07;
            report[7] = 0x04;
            report[8] = (byte)(write ? 0x05 : 0x85);
            report[9] = 0x00;
            if (write)
            {
                report[10] = (byte)((dpi >> 8) & 0xFF);
                report[11] = (byte)(dpi & 0xFF);
                report[12] = report[10];
                report[13] = report[11];
            }
            byte crc = 0;
            for (int i = 3; i <= 88; i++) crc ^= report[i];
            report[89] = crc;
            return report;
        }
    }
}
