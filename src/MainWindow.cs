using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Sensa
{
    internal sealed class MainWindow : Window
    {
        private readonly ProfileStore _store = new ProfileStore();
        private ProfileSettings _settings;
        private ComboBox _devicePicker;
        private Slider _speedSlider;
        private TextBlock _speedValue;
        private TextBlock _deviceName;
        private TextBlock _deviceMeta;
        private TextBlock _connectionText;
        private TextBlock _statusText;
        private CheckBox _acceleration;
        private CheckBox _autoSwitch;
        private Border _connectionDot;
        private bool _ready;
        private MouseProfile _current;
        private FrameworkElement _root;
        private WinForms.NotifyIcon _trayIcon;
        private WinForms.ContextMenuStrip _trayMenu;
        private WinForms.ToolStripMenuItem _modifierMenu;
        private TextBlock _hotkeyText;
        private IntPtr _windowHandle;
        private bool _reallyExit;
        private bool _dpiMode;
        private Button _sensitivityModeButton;
        private Button _dpiModeButton;
        private UIElement _sensitivityPanel;
        private UIElement _dpiPanel;
        private TextBlock _dpiValue;
        private TextBlock _dpiDescription;
        private UniformGrid _dpiStages;
        private Button _updateButton;
        private Button _adminRetryButton;
        private bool _checkingUpdate;

        internal MainWindow()
        {
            _settings = _store.Load();
            Title = "Sensa";
            Width = 452;
            Height = 730;
            MinWidth = 420;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            _root = (FrameworkElement)XamlReader.Parse(ContentXaml);
            Content = _root;
            BindControls();
            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
            Application.Current.SessionEnding += delegate { _reallyExit = true; };
        }

        private void BindControls()
        {
            _devicePicker = (ComboBox)_root.FindName("DevicePicker");
            _speedSlider = (Slider)_root.FindName("SpeedSlider");
            _speedValue = (TextBlock)_root.FindName("SpeedValue");
            _deviceName = (TextBlock)_root.FindName("DeviceName");
            _deviceMeta = (TextBlock)_root.FindName("DeviceMeta");
            _connectionText = (TextBlock)_root.FindName("ConnectionText");
            _statusText = (TextBlock)_root.FindName("StatusText");
            _acceleration = (CheckBox)_root.FindName("Acceleration");
            _autoSwitch = (CheckBox)_root.FindName("AutoSwitch");
            _connectionDot = (Border)_root.FindName("ConnectionDot");
            _hotkeyText = (TextBlock)_root.FindName("HotkeyText");
            _sensitivityModeButton = (Button)_root.FindName("SensitivityMode");
            _dpiModeButton = (Button)_root.FindName("DpiMode");
            _sensitivityPanel = (UIElement)_root.FindName("SensitivityPanel");
            _dpiPanel = (UIElement)_root.FindName("DpiPanel");
            _dpiValue = (TextBlock)_root.FindName("DpiValue");
            _dpiDescription = (TextBlock)_root.FindName("DpiDescription");
            _dpiStages = (UniformGrid)_root.FindName("DpiStages");
            _updateButton = (Button)_root.FindName("UpdateButton");
            _adminRetryButton = (Button)_root.FindName("AdminRetryButton");

            _updateButton.Click += delegate { CheckForUpdates(); };
            _adminRetryButton.Click += delegate { RestartAsAdministrator(); };
            _sensitivityModeButton.Click += delegate { SetMode(false); };
            _dpiModeButton.Click += delegate { SetMode(true); };
            foreach (var dpi in new[] { 400, 800, 1600, 3200, 6400 })
            {
                int selectedDpi = dpi;
                ((Button)_root.FindName("Dpi" + dpi)).Click += delegate { SetHardwareDpi(selectedDpi); };
            }

            ((Border)_root.FindName("TitleBar")).MouseLeftButtonDown += delegate
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            };
            ((Button)_root.FindName("MinimizeButton")).Click += delegate { WindowState = WindowState.Minimized; };
            ((Button)_root.FindName("CloseButton")).Click += delegate { HideToTray(); };
            ((Button)_root.FindName("ResetButton")).Click += delegate
            {
                if (_dpiMode)
                {
                    if (_current.SupportsHardwareDpi) SetHardwareDpi(1600);
                    else SetStatus("该型号为固定 DPI，无需恢复");
                    return;
                }
                _speedSlider.Value = 10;
                _acceleration.IsChecked = true;
                ApplyAndSave("已恢复 Windows 默认设置");
            };
            foreach (var preset in new[] { 6, 10, 14 })
            {
                int speed = preset;
                ((Button)_root.FindName("Preset" + preset)).Click += delegate { _speedSlider.Value = speed; };
            }

            _devicePicker.ItemsSource = _settings.Profiles;
            _devicePicker.SelectionChanged += delegate
            {
                if (!_ready) return;
                _current = (MouseProfile)_devicePicker.SelectedItem;
                _settings.SelectedId = _current.Id;
                UpdateProfileUi();
                ApplyAndSave("已切换并应用配置");
            };
            _speedSlider.ValueChanged += delegate
            {
                _speedValue.Text = ((int)_speedSlider.Value).ToString();
                if (!_ready || _current == null) return;
                _current.Speed = (int)_speedSlider.Value;
                ApplyAndSave("灵敏度已即时生效");
            };
            _acceleration.Click += delegate
            {
                if (!_ready || _current == null) return;
                _current.Acceleration = _acceleration.IsChecked == true;
                ApplyAndSave(_current.Acceleration ? "已开启指针精确度" : "已关闭指针精确度");
            };
            _autoSwitch.Click += delegate
            {
                if (!_ready) return;
                _settings.AutoSwitch = _autoSwitch.IsChecked == true;
                Save();
            };
        }

        private void OnSourceInitialized(object sender, EventArgs args)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            _windowHandle = source.Handle;
            source.AddHook(WndProc);
            NativeMouse.RegisterForInput(_windowHandle);
            RegisterHotkeys();
            InitializeTray();
            RefreshDevices();
            string preferred = _settings.Profiles.Any(p => p.Id == _settings.SelectedId && p.Connected)
                ? _settings.SelectedId
                : (_settings.Profiles.FirstOrDefault(p => p.Connected) ?? ProfileCatalog.Find(_settings.Profiles, _settings.SelectedId)).Id;
            _current = ProfileCatalog.Find(_settings.Profiles, preferred);
            _devicePicker.SelectedItem = _current;
            _autoSwitch.IsChecked = _settings.AutoSwitch;
            _ready = true;
            UpdateProfileUi();
            SetStatus("准备就绪 · 移动鼠标可自动切换");
        }

        private void RefreshDevices()
        {
            var connected = NativeMouse.EnumerateDevices();
            bool added = ProfileCatalog.AddDiscoveredProfiles(_settings, connected,
                NativeMouse.GetSpeed(), NativeMouse.GetAcceleration());
            foreach (var profile in _settings.Profiles)
                profile.Connected = connected.Any(d => d.Id == profile.Id);
            _devicePicker.Items.Refresh();
            if (added) Save();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == GlobalHotkeys.WmHotkey && _ready)
            {
                int id = wParam.ToInt32();
                if (id == GlobalHotkeys.ShowId) ToggleWindow();
                else if (id == GlobalHotkeys.FineId) SetHotkeySpeed(6);
                else if (id == GlobalHotkeys.BalancedId) SetHotkeySpeed(10);
                else if (id == GlobalHotkeys.SwiftId) SetHotkeySpeed(14);
                handled = true;
            }
            else if (message == NativeMouse.WmInput && _ready && _settings.AutoSwitch)
            {
                IntPtr device = NativeMouse.GetInputDevice(lParam);
                if (device != IntPtr.Zero)
                {
                    string id = NativeMouse.GetDeviceId(device);
                    var profile = ProfileCatalog.Find(_settings.Profiles, id ?? "OTHER");
                    if (_current == null || profile.Id != _current.Id)
                    {
                        _current = profile;
                        _settings.SelectedId = profile.Id;
                        _devicePicker.SelectedItem = profile;
                        UpdateProfileUi();
                        ApplyAndSave("已识别活动鼠标 · 自动应用配置");
                    }
                }
            }
            else if (message == NativeMouse.WmDeviceChange && _ready)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    RefreshDevices();
                    UpdateProfileUi();
                }));
            }
            return IntPtr.Zero;
        }

        internal void SetMode(bool dpiMode)
        {
            _dpiMode = dpiMode;
            UpdateModeUi();
            SetStatus(dpiMode ? "硬件 DPI 模式 · 直接请求鼠标传感器档位" : "系统灵敏度模式 · 调节 Windows 指针速度");
        }

        private void UpdateModeUi()
        {
            if (_current == null) return;
            _sensitivityPanel.Visibility = _dpiMode ? Visibility.Collapsed : Visibility.Visible;
            _dpiPanel.Visibility = _dpiMode ? Visibility.Visible : Visibility.Collapsed;
            _adminRetryButton.Visibility = Visibility.Collapsed;
            _acceleration.Visibility = _dpiMode ? Visibility.Collapsed : Visibility.Visible;
            _hotkeyText.Visibility = _dpiMode ? Visibility.Collapsed : Visibility.Visible;
            _sensitivityModeButton.Background = new SolidColorBrush(_dpiMode ? Color.FromRgb(32, 38, 45) : Color.FromRgb(92, 225, 180));
            _sensitivityModeButton.Foreground = new SolidColorBrush(_dpiMode ? Color.FromRgb(232, 237, 242) : Color.FromRgb(16, 33, 28));
            _dpiModeButton.Background = new SolidColorBrush(_dpiMode ? Color.FromRgb(92, 225, 180) : Color.FromRgb(32, 38, 45));
            _dpiModeButton.Foreground = new SolidColorBrush(_dpiMode ? Color.FromRgb(16, 33, 28) : Color.FromRgb(232, 237, 242));
            _dpiValue.Text = _current.Dpi > 0 ? _current.Dpi.ToString() : "—";
            _dpiDescription.Text = _current.SupportsHardwareDpi
                ? "硬件 DPI · 五档切换，成功应答后保存"
                : (_current.Dpi > 0 ? "固定硬件 DPI · 此型号不支持软件切换" : "未知设备 · 无法确认硬件 DPI 能力");
            _dpiStages.IsEnabled = _current.SupportsHardwareDpi && _current.Connected;
            _dpiStages.Opacity = _dpiStages.IsEnabled ? 1 : 0.35;
            foreach (var child in _dpiStages.Children)
            {
                var button = child as Button;
                if (button == null) continue;
                bool active = _current.SupportsHardwareDpi && button.Name == "Dpi" + _current.Dpi;
                button.Background = new SolidColorBrush(active ? Color.FromRgb(52, 86, 74) : Color.FromRgb(32, 38, 45));
            }
        }

        private void SetHardwareDpi(int dpi)
        {
            if (_current == null || !_current.SupportsHardwareDpi) return;
            if (!_current.Connected)
            {
                SetStatus("未检测到鼠标，无法切换硬件 DPI");
                return;
            }
            string error;
            if (!RazerDpi.TrySetDpi(_current.Id, dpi, out error))
            {
                _dpiDescription.Text = "切换失败：" + error;
                _adminRetryButton.Visibility = Visibility.Visible;
                SetStatus("DPI 未切换 · 可尝试管理员身份重试");
                return;
            }
            _current.Dpi = dpi;
            Save();
            UpdateModeUi();
            SetStatus("硬件 DPI 已切换为 " + dpi + " · 设备已确认");
        }

        private void RestartAsAdministrator()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                ExitApplication();
            }
            catch (Exception ex) { SetStatus("未以管理员身份重启：" + ex.Message); }
        }

        private void UpdateProfileUi()
        {
            if (_current == null) return;
            _speedSlider.Value = _current.Speed;
            _speedValue.Text = _current.Speed.ToString();
            _acceleration.IsChecked = _current.Acceleration;
            _deviceName.Text = _current.Name;
            _deviceMeta.Text = _current.Model + "  ·  " + _current.DeviceLabel;
            _connectionText.Text = _current.Connected ? "已连接" : (_current.Id == "OTHER" ? "通用配置" : "未检测到");
            _connectionDot.Background = new SolidColorBrush(_current.Connected
                ? Color.FromRgb(92, 225, 180) : Color.FromRgb(100, 108, 118));
            UpdateModeUi();
        }

        private void CheckForUpdates()
        {
            if (_checkingUpdate) return;
            _checkingUpdate = true;
            _updateButton.IsEnabled = false;
            SetStatus("正在检查 GitHub 更新…");
            System.Threading.Tasks.Task.Factory.StartNew(delegate { return UpdateManager.CheckLatest(); })
                .ContinueWith(delegate(System.Threading.Tasks.Task<GitHubRelease> task)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _checkingUpdate = false;
                        _updateButton.IsEnabled = true;
                        if (task.Exception != null)
                        {
                            SetStatus("检查更新失败：" + task.Exception.GetBaseException().Message);
                            return;
                        }
                        var release = task.Result;
                        if (!UpdateManager.IsNewer(release.tag_name, UpdateManager.CurrentVersion))
                        {
                            SetStatus("已是最新版本 · v" + UpdateManager.CurrentVersion);
                            return;
                        }
                        string notes = String.IsNullOrWhiteSpace(release.body) ? "无更新说明" : release.body;
                        if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
                        var result = MessageBox.Show(
                            "发现新版本 " + release.tag_name + "（当前 v" + UpdateManager.CurrentVersion + "）\n\n" + notes +
                            "\n\n是否立即下载并手动更新？", "Sensa 更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes) DownloadAndInstall(release);
                        else SetStatus("已取消更新，当前版本可继续使用");
                    }));
                });
        }

        private void DownloadAndInstall(GitHubRelease release)
        {
            _checkingUpdate = true;
            _updateButton.IsEnabled = false;
            SetStatus("正在下载更新包并校验 SHA-256…");
            System.Threading.Tasks.Task.Factory.StartNew(delegate { return UpdateManager.DownloadVerifiedPackage(release); })
                .ContinueWith(delegate(System.Threading.Tasks.Task<string> task)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _checkingUpdate = false;
                        _updateButton.IsEnabled = true;
                        if (task.Exception != null)
                        {
                            SetStatus("更新失败：" + task.Exception.GetBaseException().Message);
                            return;
                        }
                        try
                        {
                            UpdateManager.LaunchUpdater(task.Result);
                            ExitApplication();
                        }
                        catch (Exception ex) { SetStatus("无法启动更新：" + ex.Message); }
                    }));
                });
        }

        private void InitializeTray()
        {
            _trayMenu = new WinForms.ContextMenuStrip();
            _trayMenu.Items.Add("打开 Sensa", null, delegate { ShowFromTray(); });
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add("精细  6", null, delegate { SetHotkeySpeed(6); });
            _trayMenu.Items.Add("均衡  10", null, delegate { SetHotkeySpeed(10); });
            _trayMenu.Items.Add("迅捷  14", null, delegate { SetHotkeySpeed(14); });
            _modifierMenu = new WinForms.ToolStripMenuItem("快捷键修饰键");
            _modifierMenu.DropDownItems.Add("Ctrl + Alt", null, delegate { SetHotkeyModifier(false); });
            _modifierMenu.DropDownItems.Add("Ctrl + Shift", null, delegate { SetHotkeyModifier(true); });
            _trayMenu.Items.Add(_modifierMenu);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add("检查更新", null, delegate { ShowFromTray(); CheckForUpdates(); });
            _trayMenu.Items.Add("退出", null, delegate { ExitApplication(); });
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "Sensa · 鼠标灵敏度",
                Icon = Drawing.Icon.ExtractAssociatedIcon(WinForms.Application.ExecutablePath),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.MouseClick += delegate(object sender, WinForms.MouseEventArgs args)
            {
                if (args.Button == WinForms.MouseButtons.Left) ToggleWindow();
            };
            UpdateModifierChecks();
        }

        private void RegisterHotkeys()
        {
            bool registered = GlobalHotkeys.Register(_windowHandle, _settings.HotkeyUseShift);
            string modifier = GlobalHotkeys.ModifierName(_settings.HotkeyUseShift);
            _hotkeyText.Text = modifier + "+1/2/3 调速 · " + modifier + "+S 显示";
            if (!registered) SetStatus("部分全局快捷键被占用，可在托盘切换修饰键");
        }

        private void SetHotkeyModifier(bool useShift)
        {
            _settings.HotkeyUseShift = useShift;
            Save();
            RegisterHotkeys();
            UpdateModifierChecks();
        }

        private void UpdateModifierChecks()
        {
            if (_modifierMenu == null) return;
            ((WinForms.ToolStripMenuItem)_modifierMenu.DropDownItems[0]).Checked = !_settings.HotkeyUseShift;
            ((WinForms.ToolStripMenuItem)_modifierMenu.DropDownItems[1]).Checked = _settings.HotkeyUseShift;
        }

        private void SetHotkeySpeed(int speed)
        {
            if (_current == null) return;
            _speedSlider.Value = speed;
            if (_trayIcon != null)
                _trayIcon.ShowBalloonTip(700, "Sensa", _current.Name + " · 灵敏度 " + speed, WinForms.ToolTipIcon.Info);
        }

        private void ToggleWindow()
        {
            if (IsVisible && WindowState != WindowState.Minimized) HideToTray();
            else ShowFromTray();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void HideToTray()
        {
            Hide();
            if (_trayIcon != null)
                _trayIcon.ShowBalloonTip(700, "Sensa", "已最小化到托盘，全局快捷键仍可使用。", WinForms.ToolTipIcon.Info);
        }

        internal void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        private void ApplyAndSave(string message)
        {
            if (_current == null) return;
            if (_dpiMode)
            {
                if (_current.SupportsHardwareDpi) SetHardwareDpi(_current.Dpi);
                else SetStatus("该型号为固定 DPI，仅可切换系统灵敏度");
                return;
            }
            bool speedOk = NativeMouse.SetSpeed(_current.Speed);
            bool accelerationOk = NativeMouse.SetAcceleration(_current.Acceleration);
            Save();
            SetStatus(speedOk && accelerationOk ? message : "设置未完全生效，请重试");
        }

        private void Save()
        {
            try { _store.Save(_settings); }
            catch { SetStatus("配置保存失败，请检查磁盘权限"); }
        }

        private void SetStatus(string text)
        {
            _statusText.Text = text;
        }

        private void OnClosing(object sender, CancelEventArgs args)
        {
            if (!_reallyExit)
            {
                args.Cancel = true;
                HideToTray();
                return;
            }
            Save();
            GlobalHotkeys.Unregister(_windowHandle);
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            if (_trayMenu != null) _trayMenu.Dispose();
        }

        private const string ContentXaml = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid.Resources>
    <SolidColorBrush x:Key='Accent' Color='#5CE1B4'/>
    <SolidColorBrush x:Key='Muted' Color='#8B95A3'/>
    <Style TargetType='Button'>
      <Setter Property='Foreground' Value='#E8EDF2'/><Setter Property='Background' Value='#20262D'/>
      <Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='9'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>
          <Setter TargetName='B' Property='Background' Value='#2B343D'/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='Slider'>
      <Setter Property='Height' Value='30'/><Setter Property='Template'><Setter.Value>
        <ControlTemplate TargetType='Slider'><Grid>
          <Border Height='5' VerticalAlignment='Center' CornerRadius='3' Background='#303840'/>
          <Track x:Name='PART_Track'><Track.Thumb><Thumb Width='20' Height='20'>
            <Thumb.Template><ControlTemplate TargetType='Thumb'><Grid>
              <Ellipse Fill='#5CE1B4'/><Ellipse Margin='5' Fill='#10211C'/>
            </Grid></ControlTemplate></Thumb.Template>
          </Thumb></Track.Thumb></Track>
        </Grid></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='CheckBox'>
      <Setter Property='Foreground' Value='#E8EDF2'/><Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
        <StackPanel Orientation='Horizontal'><Border x:Name='Box' Width='36' Height='21' CornerRadius='11' Background='#303840'>
          <Ellipse x:Name='Knob' Width='15' Height='15' Fill='#AAB2BC' HorizontalAlignment='Left' Margin='3 0 0 0'/></Border>
          <ContentPresenter Margin='11 0 0 0' VerticalAlignment='Center'/></StackPanel>
        <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='Box' Property='Background' Value='#5CE1B4'/><Setter TargetName='Knob' Property='Fill' Value='#10211C'/>
          <Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Knob' Property='Margin' Value='0 0 3 0'/>
        </Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='ComboBoxItem'>
      <Setter Property='Foreground' Value='#E8EDF2'/><Setter Property='Template'><Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Item' Background='Transparent' Padding='12 9'>
          <ContentPresenter/></Border><ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#34414A'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='ComboBox'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid>
        <ToggleButton ClickMode='Press' Focusable='False' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
          <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>
            <Border Background='#20262D' BorderBrush='#303840' BorderThickness='1' CornerRadius='9'/>
          </ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding ItemTemplate}'
          IsHitTestVisible='False' Margin='14 0 30 0' VerticalAlignment='Center'/>
        <TextBlock Text='⌄' Foreground='#8B95A3' IsHitTestVisible='False' HorizontalAlignment='Right'
          VerticalAlignment='Center' Margin='0 0 13 3'/>
        <Popup Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
          <Border Background='#20262D' BorderBrush='#3A454F' BorderThickness='1' CornerRadius='8' Padding='4'
            MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ComboBox}}' Margin='0 5 0 0'>
            <ScrollViewer><ItemsPresenter/></ScrollViewer>
          </Border>
        </Popup>
      </Grid></ControlTemplate></Setter.Value></Setter>
    </Style>
  </Grid.Resources>
  <Border Background='#11161B' CornerRadius='18' BorderBrush='#293139' BorderThickness='1'>
    <Grid Margin='26 18 26 22'><Grid.RowDefinitions>
      <RowDefinition Height='42'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <Border x:Name='TitleBar' Grid.Row='0' Background='Transparent'>
      <Grid><StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
        <Border Width='30' Height='30' CornerRadius='9' Background='#5CE1B4'><TextBlock Text='S' FontSize='19' FontWeight='Bold'
          Foreground='#10211C' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <TextBlock Text='SENSA' FontSize='16' FontWeight='SemiBold' Foreground='#F3F6F8' Margin='10 0 0 0' VerticalAlignment='Center'/>
        <TextBlock Text='鼠标灵敏度' FontSize='11' Foreground='#76818E' Margin='10 0 0 0' VerticalAlignment='Center'/>
      </StackPanel><StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top'>
        <Button x:Name='MinimizeButton' Content='—' Width='32' Height='28' Background='Transparent' FontSize='13'/>
        <Button x:Name='CloseButton' Content='✕' Width='32' Height='28' Background='Transparent' FontSize='12' Margin='4 0 0 0'/>
      </StackPanel></Grid>
    </Border>
    <StackPanel Grid.Row='1' Margin='0 12 0 18'>
      <TextBlock Text='你的手感，随鼠切换。' FontSize='25' FontWeight='SemiBold' Foreground='#F3F6F8'/>
      <TextBlock Text='每只鼠标独立记忆，调节后立即生效。' FontSize='12' Foreground='#8B95A3' Margin='0 7 0 0'/>
    </StackPanel>
    <ComboBox x:Name='DevicePicker' Grid.Row='2' Height='42' Padding='12 0' FontSize='12' Foreground='#E8EDF2'
      Background='#20262D' BorderBrush='#303840' VerticalContentAlignment='Center'/>
    <Border Grid.Row='3' Background='#181E24' CornerRadius='13' Padding='18' Margin='0 12 0 0'>
      <StackPanel><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <StackPanel><TextBlock x:Name='DeviceName' FontSize='15' FontWeight='SemiBold' Foreground='#F0F4F7'/>
          <TextBlock x:Name='DeviceMeta' FontSize='10' Foreground='#7F8A97' Margin='0 6 0 0' TextTrimming='CharacterEllipsis'/></StackPanel>
        <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Top' Margin='12 2 0 0'>
          <Border x:Name='ConnectionDot' Width='7' Height='7' CornerRadius='4' Background='#5CE1B4' Margin='0 0 6 0'/>
          <TextBlock x:Name='ConnectionText' Text='已连接' Foreground='#8B95A3' FontSize='10'/></StackPanel>
      </Grid>
        <UniformGrid Columns='2' Margin='0 16 0 0'>
          <Button x:Name='SensitivityMode' Content='系统灵敏度' Height='32' Margin='0 0 4 0' FontSize='11'/>
          <Button x:Name='DpiMode' Content='硬件 DPI' Height='32' Margin='4 0 0 0' FontSize='11'/>
        </UniformGrid>
        <StackPanel x:Name='SensitivityPanel'>
          <Grid Margin='0 14 0 0'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <TextBlock Text='WINDOWS 指针速度' FontSize='10' Foreground='#8B95A3' VerticalAlignment='Center'/>
            <StackPanel Grid.Column='1' Orientation='Horizontal'><TextBlock x:Name='SpeedValue' Text='10' FontSize='34' FontWeight='Light'
              Foreground='#5CE1B4' VerticalAlignment='Bottom'/><TextBlock Text=' / 20' FontSize='12' Foreground='#687480'
              VerticalAlignment='Bottom' Margin='3 0 0 6'/></StackPanel></Grid>
          <Slider x:Name='SpeedSlider' Minimum='1' Maximum='20' Value='10' SmallChange='1' LargeChange='1'
            IsSnapToTickEnabled='True' TickFrequency='1' Margin='0 8 0 0'/>
          <Grid><TextBlock Text='慢' FontSize='10' Foreground='#687480'/><TextBlock Text='默认 10' FontSize='10' Foreground='#687480'
            HorizontalAlignment='Center'/><TextBlock Text='快' FontSize='10' Foreground='#687480' HorizontalAlignment='Right'/></Grid>
          <UniformGrid Columns='3' Margin='0 16 0 0'>
            <Button x:Name='Preset6' Content='精细  6' Height='34' Margin='0 0 6 0' FontSize='11'/>
            <Button x:Name='Preset10' Content='均衡  10' Height='34' Margin='3 0' FontSize='11'/>
            <Button x:Name='Preset14' Content='迅捷  14' Height='34' Margin='6 0 0 0' FontSize='11'/>
          </UniformGrid>
        </StackPanel>
        <StackPanel x:Name='DpiPanel' Visibility='Collapsed'>
          <Grid Margin='0 14 0 0'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <TextBlock Text='传感器分辨率' FontSize='10' Foreground='#8B95A3' VerticalAlignment='Center'/>
            <StackPanel Grid.Column='1' Orientation='Horizontal'><TextBlock x:Name='DpiValue' Text='1600' FontSize='34' FontWeight='Light'
              Foreground='#5CE1B4' VerticalAlignment='Bottom'/><TextBlock Text=' DPI' FontSize='12' Foreground='#687480'
              VerticalAlignment='Bottom' Margin='3 0 0 6'/></StackPanel></Grid>
          <TextBlock x:Name='DpiDescription' FontSize='10' Foreground='#7F8A97' Margin='0 12 0 0'/>
          <UniformGrid x:Name='DpiStages' Columns='5' Margin='0 16 0 0'>
            <Button x:Name='Dpi400' Content='400' Height='34' Margin='0 0 4 0' FontSize='11'/>
            <Button x:Name='Dpi800' Content='800' Height='34' Margin='2 0' FontSize='11'/>
            <Button x:Name='Dpi1600' Content='1600' Height='34' Margin='2 0' FontSize='11'/>
            <Button x:Name='Dpi3200' Content='3200' Height='34' Margin='2 0' FontSize='11'/>
            <Button x:Name='Dpi6400' Content='6400' Height='34' Margin='4 0 0 0' FontSize='11'/>
          </UniformGrid>
          <Button x:Name='AdminRetryButton' Content='以管理员身份重启并重试' Height='30' FontSize='10'
            Margin='0 10 0 0' Visibility='Collapsed'/>
        </StackPanel>
      </StackPanel>
    </Border>
    <StackPanel Grid.Row='4' Margin='2 18 2 0'>
      <CheckBox x:Name='Acceleration' Content='提高指针精确度（系统加速度）' FontSize='11'/>
      <CheckBox x:Name='AutoSwitch' Content='根据当前移动的鼠标自动切换' FontSize='11' Margin='0 15 0 0'/>
      <TextBlock x:Name='HotkeyText' FontSize='9' Foreground='#687480' Margin='48 10 0 0'/>
    </StackPanel>
    <Border Grid.Row='6' Background='#181E24' CornerRadius='9' Padding='12 9' Margin='0 16 0 0'>
      <TextBlock FontSize='10' Foreground='#7F8A97' TextWrapping='Wrap' LineHeight='16'
        Text='两种模式互不影响：系统灵敏度改变 Windows 指针速度；硬件 DPI 请求鼠标传感器档位，仅对硬件支持的型号开放。'/>
    </Border>
    <Grid Grid.Row='5' VerticalAlignment='Bottom' Margin='0 14 0 0'>
      <TextBlock x:Name='StatusText' Text='正在初始化…' FontSize='10' Foreground='#5CE1B4' TextTrimming='CharacterEllipsis' Margin='0 0 172 0'/>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='UpdateButton' Content='检查更新' Width='76' Height='30' FontSize='10' Background='Transparent' Margin='0 0 4 0'/>
        <Button x:Name='ResetButton' Content='恢复默认' Width='86' Height='30' FontSize='10' Background='Transparent'/>
      </StackPanel>
    </Grid>
    </Grid>
  </Border>
</Grid>";
    }
}
