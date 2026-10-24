using System;
using System.IO;
using System.Linq;

namespace Sensa
{
    internal static class SelfTests
    {
        private static int _count;

        private static void Assert(bool condition, string name)
        {
            _count++;
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
            Console.WriteLine("PASS: " + name);
        }

        [STAThread]
        internal static int Main()
        {
            try
            {
                Assert(NativeMouse.ParseDeviceId(@"\\?\HID#VID_1532&PID_0098#abc") == "1532:0098", "Razer USB ID parsing");
                Assert(NativeMouse.ParseDeviceId(@"\\?\HID#VID_046d&PID_c077#abc") == "046D:C077", "Logitech USB ID parsing and normalization");
                Assert(NativeMouse.ParseDeviceId(@"\\?\HID#VID_258A&PID_010C&MI_01&COL07#abc") == "258A:010C", "AOC combo receiver mouse ID parsing");
                Assert(NativeMouse.ParseDeviceId(@"\\?\HID#without-id") == null, "Unknown device fallback");
                int originalSpeed = NativeMouse.GetSpeed();
                bool originalAcceleration = NativeMouse.GetAcceleration();
                Assert(NativeMouse.SetSpeed(originalSpeed) && NativeMouse.GetSpeed() == originalSpeed, "Windows speed API read/write");
                Assert(NativeMouse.SetAcceleration(originalAcceleration), "Windows acceleration API read/write");
                var report = RazerDpi.BuildReport(true, 1600);
                Assert(report.Length == 91 && report[7] == 0x04 && report[8] == 0x05 && report[10] == 0x06 && report[11] == 0x40, "Razer DPI report encoding");
                int hardwareDpi;
                string dpiError;
                if (NativeMouse.EnumerateDevices().Exists(d => d.Id == "1532:0098"))
                {
                    bool supported = RazerDpi.TryGetDpi("1532:0098", out hardwareDpi, out dpiError);
                    Console.WriteLine(supported ? "INFO: Hardware DPI readback " + hardwareDpi : "INFO: Hardware DPI command rejected by this device: " + dpiError);
                }

                var defaults = ProfileCatalog.CreateDefaults(10, false);
                Assert(defaults.Count == 4, "Three supported models plus generic profile");
                Assert(defaults[0].Model == "RZ01-03850" && defaults[0].Speed == 10, "Razer profile metadata");
                Assert(ProfileCatalog.Find(defaults, "258A:010C").Name == "AOC KM151 D0", "AOC profile metadata");
                Assert(defaults[0].SupportsHardwareDpi && defaults[0].Dpi == 1600 && defaults[1].Dpi == 800 && !defaults[1].SupportsHardwareDpi && defaults[2].Dpi == 1600 && !defaults[2].SupportsHardwareDpi, "Hardware and fixed DPI capabilities");
                Assert(ProfileCatalog.Find(defaults, "UNKNOWN").Id == "OTHER", "Unknown model uses generic profile");
                Assert(GlobalHotkeys.ModifierName(false) == "Ctrl+Alt" && GlobalHotkeys.ModifierName(true) == "Ctrl+Shift", "Hotkey modifier display");
                Version parsed;
                Assert(UpdateManager.TryParseVersion("v1.2.0", out parsed) && parsed.ToString() == "1.2.0", "Release version parsing");
                Assert(UpdateManager.IsNewer("v1.3.0", "1.2.0") && !UpdateManager.IsNewer("v1.2.0", "1.2.0"), "Update version comparison");

                string directory = Path.Combine(Path.GetTempPath(), "SensaTests_" + Guid.NewGuid().ToString("N"));
                string path = Path.Combine(directory, "profiles.json");
                var store = new ProfileStore(path);
                var settings = store.Load();
                var discovered = new System.Collections.Generic.List<MouseDevice>
                {
                    new MouseDevice { Id = "1234:ABCD", DisplayName = "Example Optical Mouse" },
                    new MouseDevice { Id = "1234:ABCD", DisplayName = "Example Optical Mouse" },
                    new MouseDevice { Id = "1532:0098", DisplayName = "Duplicate Razer" }
                };
                Assert(ProfileCatalog.AddDiscoveredProfiles(settings, discovered, 11, false), "Unknown HID mouse automatic registration");
                Assert(settings.Profiles.Count == 5 && ProfileCatalog.Find(settings.Profiles, "1234:ABCD").Name == "Example Optical Mouse" && ProfileCatalog.Find(settings.Profiles, "1234:ABCD").Speed == 11, "Discovered mouse independent named profile");
                Assert(!ProfileCatalog.AddDiscoveredProfiles(settings, discovered, 11, false) && settings.Profiles.Count == 5, "Duplicate device registration prevention");
                settings.SelectedId = "046D:C077";
                settings.AutoSwitch = false;
                ProfileCatalog.Find(settings.Profiles, settings.SelectedId).Speed = 14;
                store.Save(settings);
                string package = Path.Combine(directory, "package.exe");
                string hashFile = Path.Combine(directory, "package.sha256");
                File.WriteAllText(package, "signed test package");
                string digest;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(package))).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(hashFile, digest + "  Sensa.exe");
                UpdateManager.VerifyPackage(package, hashFile);
                Assert(File.Exists(package), "Valid update package checksum");
                File.AppendAllText(package, "tampered");
                bool blocked = false;
                try { UpdateManager.VerifyPackage(package, hashFile); }
                catch (InvalidDataException) { blocked = true; }
                Assert(blocked && !File.Exists(package), "Tampered update package blocked and deleted");
                var loaded = store.Load();
                Assert(loaded.SelectedId == "046D:C077" && !loaded.AutoSwitch, "Settings round trip");
                Assert(ProfileCatalog.Find(loaded.Profiles, "046D:C077").Speed == 14, "Independent model speed persistence");

                ProfileCatalog.Find(loaded.Profiles, "046D:C077").Speed = 100;
                store.Save(loaded);
                Assert(ProfileCatalog.Find(store.Load().Profiles, "046D:C077").Speed == 20, "Speed upper bound clamp");
                File.WriteAllText(path, "not valid json");
                Assert(store.Load().Profiles.Count == 4, "Corrupt configuration recovery");
                Directory.Delete(directory, true);
                if (Environment.GetEnvironmentVariable("SENSA_TEST_ONLINE") == "1")
                {
                    var release = UpdateManager.CheckLatest();
                    Assert(release.tag_name == "v" + UpdateManager.CurrentVersion, "GitHub latest release detection");
                    string downloaded = UpdateManager.DownloadVerifiedPackage(release);
                    Assert(File.Exists(downloaded) && new FileInfo(downloaded).Length > 50000, "GitHub update download and SHA-256 verification");
                }

                Console.WriteLine("All " + _count + " tests passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
