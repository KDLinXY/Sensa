using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Sensa
{
    internal sealed class MouseProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Model { get; set; }
        public int Speed { get; set; }
        public int Dpi { get; set; }
        public bool Acceleration { get; set; }
        public bool Connected { get; set; }

        public bool SupportsHardwareDpi
        {
            get { return Id == "1532:0098"; }
        }

        public string DeviceLabel
        {
            get { return Id == "OTHER" ? "未识别或通用 HID 鼠标" : "USB " + Id; }
        }

        public override string ToString()
        {
            return Name + (Connected ? "   ●" : "");
        }
    }

    internal sealed class ProfileSettings
    {
        public ProfileSettings()
        {
            AutoSwitch = true;
        }

        public string SelectedId { get; set; }
        public bool AutoSwitch { get; set; }
        public bool HotkeyUseShift { get; set; }
        public List<MouseProfile> Profiles { get; set; }
    }

    internal static class ProfileCatalog
    {
        internal static List<MouseProfile> CreateDefaults(int currentSpeed, bool acceleration)
        {
            return new List<MouseProfile>
            {
                new MouseProfile
                {
                    Id = "1532:0098", Name = "Razer DeathAdder Essential",
                    Model = "RZ01-03850", Speed = currentSpeed, Dpi = 1600, Acceleration = acceleration
                },
                new MouseProfile
                {
                    Id = "046D:C077", Name = "Logitech B100",
                    Model = "M-U0026 / USB Optical Mouse", Speed = currentSpeed, Dpi = 800, Acceleration = acceleration
                },
                new MouseProfile
                {
                    Id = "258A:010C", Name = "AOC KM151 D0",
                    Model = "有线键鼠套装 · 1600 DPI", Speed = currentSpeed, Dpi = 1600, Acceleration = acceleration
                },
                new MouseProfile
                {
                    Id = "OTHER", Name = "其他鼠标",
                    Model = "通用配置", Speed = currentSpeed, Dpi = 0, Acceleration = acceleration
                }
            };
        }

        internal static string GetDisplayName(string id)
        {
            if (id == "1532:0098") return "Razer DeathAdder Essential";
            if (id == "046D:C077") return "Logitech B100";
            if (id == "258A:010C") return "AOC KM151 D0";
            return null;
        }

        internal static bool AddDiscoveredProfiles(ProfileSettings settings, IEnumerable<MouseDevice> devices,
            int currentSpeed, bool acceleration)
        {
            bool added = false;
            foreach (var device in devices)
            {
                if (String.IsNullOrEmpty(device.Id) || settings.Profiles.Any(p => p.Id == device.Id)) continue;
                var profile = new MouseProfile
                {
                    Id = device.Id,
                    Name = String.IsNullOrWhiteSpace(device.DisplayName) ? "HID 鼠标 " + device.Id : device.DisplayName,
                    Model = "自动适配 · 通用 HID",
                    Speed = currentSpeed,
                    Dpi = 0,
                    Acceleration = acceleration
                };
                int genericIndex = settings.Profiles.FindIndex(p => p.Id == "OTHER");
                settings.Profiles.Insert(genericIndex < 0 ? settings.Profiles.Count : genericIndex, profile);
                added = true;
            }
            return added;
        }

        internal static MouseProfile Find(IList<MouseProfile> profiles, string id)
        {
            return profiles.FirstOrDefault(p => p.Id == id) ?? profiles.First(p => p.Id == "OTHER");
        }
    }

    internal sealed class ProfileStore
    {
        private readonly string _path;

        internal ProfileStore(string path = null)
        {
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sensa", "profiles.json");
        }

        internal ProfileSettings Load()
        {
            ProfileSettings settings = null;
            try
            {
                if (File.Exists(_path))
                    settings = new JavaScriptSerializer().Deserialize<ProfileSettings>(File.ReadAllText(_path));
            }
            catch { }

            if (settings == null) settings = new ProfileSettings();
            if (settings.Profiles == null) settings.Profiles = new List<MouseProfile>();

            var defaults = ProfileCatalog.CreateDefaults(NativeMouse.GetSpeed(), NativeMouse.GetAcceleration());
            foreach (var item in defaults)
            {
                if (settings.Profiles.All(p => p.Id != item.Id)) settings.Profiles.Add(item);
            }
            foreach (var profile in settings.Profiles)
            {
                profile.Speed = Math.Max(1, Math.Min(20, profile.Speed));
                if (profile.Dpi <= 0)
                {
                    var fallback = defaults.FirstOrDefault(d => d.Id == profile.Id);
                    profile.Dpi = fallback == null ? 0 : fallback.Dpi;
                }
                profile.Connected = false;
            }
            if (String.IsNullOrEmpty(settings.SelectedId)) settings.SelectedId = defaults[0].Id;
            ProfileCatalog.Find(settings.Profiles, settings.SelectedId);
            if (settings.Profiles.All(p => p.Id != settings.SelectedId)) settings.SelectedId = "OTHER";
            return settings;
        }

        internal void Save(ProfileSettings settings)
        {
            string directory = Path.GetDirectoryName(_path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
    }
}
