using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Nanally
{
    sealed class MediaItem
    {
        public string Name;
        public string RemotePath;
        public string Folder;
        public long Size;
        public string DateText;
        public long DateTicks;
        public bool IsVideo;
        public bool IsChecked;
        public readonly object Gate = new object();
        public string LocalPath;
        public BitmapSource Thumb;
        public Image ThumbHost;
        public TextBlock ThumbHint;
        public bool ThumbQueued;
        public bool ThumbFailed;
    }

    sealed class AppSettings
    {
        public string LogsDir = "";
        public string FeishuAppId = "";
        public string FeishuToken = "";
        public string FeishuChatId = "";
        public string ThemeMode = "Light";
        public string Accent = "Blue";
        public bool FollowSystem;
        public bool EnableGlass = true;
        public bool SidebarExpanded = true;
        public bool ApkUninstallFirst;
        public bool ApkInstallAllDevices;

        public bool ResolvedDark()
        {
            if (FollowSystem)
                return Theme.SystemIsDark();
            return string.Equals(ThemeMode, "Dark", StringComparison.OrdinalIgnoreCase);
        }

        static string FilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nanally");
            return Path.Combine(dir, "settings.json");
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                var path = FilePath();
                if (!File.Exists(path))
                    return s;
                var text = File.ReadAllText(path, Encoding.UTF8);
                s.LogsDir = Read(text, "LogsDir");
                s.FeishuAppId = Read(text, "FeishuAppId");
                s.FeishuToken = Read(text, "FeishuToken");
                s.FeishuChatId = Read(text, "FeishuChatId");
                var theme = Read(text, "ThemeMode");
                s.ThemeMode = theme == "Dark" ? "Dark" : "Light";
                var accent = Read(text, "Accent");
                s.Accent = accent == "Pink" || accent == "Green" ? accent : "Blue";
                s.FollowSystem = Read(text, "FollowSystem") == "true";
                s.EnableGlass = Read(text, "EnableGlass") != "false";
                s.SidebarExpanded = Read(text, "SidebarExpanded") != "false";
                s.ApkUninstallFirst = Read(text, "ApkUninstallFirst") == "true";
                s.ApkInstallAllDevices = Read(text, "ApkInstallAllDevices") == "true";
            }
            catch { }
            return s;
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(FilePath());
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = "{"
                + "\"LogsDir\":\"" + Esc(LogsDir) + "\","
                + "\"FeishuAppId\":\"" + Esc(FeishuAppId) + "\","
                + "\"FeishuToken\":\"" + Esc(FeishuToken) + "\","
                + "\"FeishuChatId\":\"" + Esc(FeishuChatId) + "\","
                + "\"ThemeMode\":\"" + Esc(ThemeMode) + "\","
                + "\"Accent\":\"" + Esc(Accent) + "\","
                + "\"FollowSystem\":\"" + (FollowSystem ? "true" : "false") + "\","
                + "\"EnableGlass\":\"" + (EnableGlass ? "true" : "false") + "\","
                + "\"SidebarExpanded\":\"" + (SidebarExpanded ? "true" : "false") + "\","
                + "\"ApkUninstallFirst\":\"" + (ApkUninstallFirst ? "true" : "false") + "\","
                + "\"ApkInstallAllDevices\":\"" + (ApkInstallAllDevices ? "true" : "false") + "\""
                + "}";
            File.WriteAllText(FilePath(), json, Encoding.UTF8);
        }

        static string Read(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                return "";
            return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");
        }

        static string Esc(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }

    sealed partial class MainWindow
    {
        static readonly string[] MediaRoots = new[]
        {
            "/sdcard/DCIM/Screenshots",
            "/sdcard/Pictures/Screenshots",
            "/sdcard/DCIM/ScreenRecorder",
            "/sdcard/DCIM/Screen recordings",
            "/sdcard/Movies/ScreenRecorder",
            "/sdcard/Movies/Screenrecords",
            "/sdcard/Movies/Screen recordings",
            "/sdcard/Pictures/ScreenRecorder"
        };

        void BuildShell()
        {
            Title = "Nanally";
            Width = 1100;
            Height = 720;
            MinWidth = 936;
            MinHeight = 620;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Font;
            SetResourceReference(ForegroundProperty, "nn.Ink");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            ApplyWindowChrome();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = root;

            titleBar = BuildTitleBar();
            root.Children.Add(titleBar);

            shellBody = new Grid();
            if (!settings.EnableGlass)
                Theme.BindElement(shellBody, BackgroundProperty, "nn.Bg");
            shellBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            colSidebar = shellBody.ColumnDefinitions[0];
            shellBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shellSidebar = BuildSidebar() as Border;
            if (shellSidebar != null)
            {
                Theme.BindElement(shellSidebar, BackgroundProperty, "nn.Sidebar");
                Theme.BindElement(shellSidebar, Border.BorderBrushProperty, "nn.Hairline");
                shellBody.Children.Add(shellSidebar);
            }
            else
                shellBody.Children.Add(BuildSidebar());

            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            deviceBar = BuildToolBar();
            main.Children.Add(deviceBar);
            var pages = new Grid();
            pageLogs = BuildLogsPage();
            pageMedia = BuildMediaPage();
            pageApk = BuildApkPage();
            pageSettings = BuildSettingsPage();
            pageAbout = BuildAboutPage();
            pageMedia.Visibility = Visibility.Collapsed;
            pageApk.Visibility = Visibility.Collapsed;
            pageSettings.Visibility = Visibility.Collapsed;
            pageAbout.Visibility = Visibility.Collapsed;
            pages.Children.Add(pageLogs);
            pages.Children.Add(pageMedia);
            pages.Children.Add(pageApk);
            pages.Children.Add(pageSettings);
            pages.Children.Add(pageAbout);
            Grid.SetRow(pages, 1);
            main.Children.Add(pages);
            actionFooter = BuildActionFooter();
            Grid.SetRow(actionFooter, 2);
            main.Children.Add(actionFooter);
            Grid.SetColumn(main, 1);
            shellBody.Children.Add(main);
            Grid.SetRow(shellBody, 1);
            root.Children.Add(shellBody);

            previewLayer = BuildPreviewLayer();
            Grid.SetRow(previewLayer, 1);
            previewLayer.Visibility = Visibility.Collapsed;
            root.Children.Add(previewLayer);

            ApplyChromeLook();
            ShowNav("logs");
            SourceInitialized += delegate { Acrylic.TryApply(this, Theme.Dark, settings.EnableGlass); };
            Closing += delegate { thumbGeneration++; ClosePreview(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Escape)
                    return;
                if (previewLayer != null && previewLayer.Visibility == Visibility.Visible)
                {
                    ClosePreview();
                    e.Handled = true;
                    return;
                }
                if (busy)
                    return;
                if (devicePopup != null && devicePopup.IsOpen)
                {
                    devicePopup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                Close();
            };
        }

        void ApplyWindowChrome()
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 36,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = settings.EnableGlass ? new Thickness(-1) : new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
        }

        void ApplyChromeLook()
        {
            if (settings.EnableGlass)
            {
                Background = Brushes.Transparent;
                if (titleBar != null)
                    titleBar.Background = Brushes.Transparent;
                // Let DWM Acrylic show through the sidebar (don't paint shellBody under it).
                if (shellBody != null)
                    shellBody.Background = Brushes.Transparent;
            }
            else
            {
                Theme.BindElement(this, BackgroundProperty, "nn.Bg");
                if (titleBar != null)
                    Theme.BindElement(titleBar, BackgroundProperty, "nn.Sidebar");
                if (shellBody != null)
                    Theme.BindElement(shellBody, BackgroundProperty, "nn.Bg");
            }
            Acrylic.TryApply(this, Theme.Dark, settings.EnableGlass);
        }

        Border BuildTitleBar()
        {
            var bar = new Border { Background = Brushes.Transparent };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
            brand.Children.Add(Portrait(16, 4));
            brand.Children.Add(new TextBlock
            {
                Text = "Nanally",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Ink,
                Margin = new Thickness(8, 0, 0, 0)
            });
            grid.Children.Add(brand);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(CaptionButton("\uE921", false, delegate { WindowState = WindowState.Minimized; }));
            var max = CaptionButton("\uE922", false, delegate
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            });
            StateChanged += delegate { max.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; };
            buttons.Children.Add(max);
            buttons.Children.Add(CaptionButton("\uE8BB", true, delegate { Close(); }));
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            bar.Child = grid;
            return bar;
        }

        Button CaptionButton(string glyph, bool close, RoutedEventHandler click)
        {
            var btn = new Button
            {
                Content = glyph,
                Width = 46,
                Height = 36,
                FontFamily = Theme.IconFont,
                FontSize = 10,
                Foreground = Theme.Ink,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btn, true);
            btn.Template = FlatButtonTemplate(Brushes.Transparent, close ? B("#E81123") : Theme.NavHover, 0);
            if (close)
            {
                btn.MouseEnter += delegate { btn.Foreground = Brushes.White; };
                btn.MouseLeave += delegate { btn.Foreground = Theme.Ink; };
            }
            btn.Click += click;
            return btn;
        }

        UIElement BuildSidebar()
        {
            var side = new Border
            {
                Background = Theme.Sidebar,
                BorderBrush = Theme.Hairline,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var dock = new DockPanel();
            navToggle = NavButton("\uE700", null, false);
            navToggle.Margin = new Thickness(12, 10, 12, 4);
            navToggle.ToolTip = "展开 / 收起侧栏";
            navToggle.Click += delegate { SetSidebar(!settings.SidebarExpanded); };
            DockPanel.SetDock(navToggle, Dock.Top);
            dock.Children.Add(navToggle);

            var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            tabLogs = NavButton("\uE8B7", "提取日志", true);
            txtNavLogs = LabelOf(tabLogs);
            tabLogs.Click += delegate { ShowNav("logs"); };
            tabMedia = NavButton("\uE91B", "截图录屏", false);
            txtNavMedia = LabelOf(tabMedia);
            tabMedia.Click += delegate { ShowNav("media"); };
            tabApk = NavButton("\uE7B8", "安装 APK", false);
            txtNavApk = LabelOf(tabApk);
            tabApk.Click += delegate { ShowNav("apk"); };
            navSettings = NavButton("\uE713", "设置", false);
            txtNavSettings = LabelOf(navSettings);
            navSettings.Click += delegate { ShowNav("settings"); };
            nav.Children.Add(tabLogs);
            nav.Children.Add(tabMedia);
            nav.Children.Add(tabApk);
            nav.Children.Add(navSettings);
            DockPanel.SetDock(nav, Dock.Top);
            dock.Children.Add(nav);

            var about = AboutButton();
            DockPanel.SetDock(about, Dock.Bottom);
            dock.Children.Add(about);
            dock.Children.Add(new Border());
            side.Child = dock;
            SetSidebar(settings.SidebarExpanded);
            return side;
        }

        Button AboutButton()
        {
            btnAbout = new Button
            {
                Margin = new Thickness(12, 0, 12, 14),
                Padding = new Thickness(0),
                Height = 40,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Focusable = false,
                ToolTip = "关于 Nanally"
            };
            btnAbout.Template = LeftButtonTemplate(Brushes.Transparent, Theme.CardSoft, 8);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var avatar = Portrait(36, 9);
            avatar.HorizontalAlignment = HorizontalAlignment.Center;
            avatar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(avatar);
            panelSidebarBrand = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var brand = new TextBlock { Text = "Nanally", FontWeight = FontWeights.SemiBold, FontSize = 14 };
            BindInk(brand);
            var ver = new TextBlock { Text = "v1.0", FontSize = 11 };
            BindLabel(ver);
            panelSidebarBrand.Children.Add(brand);
            panelSidebarBrand.Children.Add(ver);
            Grid.SetColumn(panelSidebarBrand, 1);
            row.Children.Add(panelSidebarBrand);
            btnAbout.Content = row;
            btnAbout.Click += delegate { ShowNav("about"); };
            return btnAbout;
        }

        static TextBlock LabelOf(Button nav)
        {
            var grid = nav.Content as Grid;
            if (grid == null)
                return null;
            foreach (UIElement child in grid.Children)
            {
                if (Grid.GetColumn(child) == 1)
                    return child as TextBlock;
            }
            return null;
        }

        static Border IconTile(Button nav)
        {
            var grid = nav.Content as Grid;
            if (grid == null || grid.Children.Count == 0)
                return null;
            return grid.Children[0] as Border;
        }

        Button NavButton(string glyph, string label, bool active)
        {
            var btn = new Button
            {
                Margin = new Thickness(12, 4, 12, 4),
                Padding = new Thickness(0),
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                FontSize = 13,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            btn.Template = LeftButtonTemplate(Brushes.Transparent, Brushes.Transparent, 0);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tile = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = Theme.IconFont,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            grid.Children.Add(tile);
            if (!string.IsNullOrEmpty(label))
            {
                var text = new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
            }
            btn.Content = grid;
            btn.MouseEnter += delegate
            {
                if (!Equals(tile.Tag, "on"))
                    tile.Background = Theme.NavHover;
            };
            btn.MouseLeave += delegate
            {
                tile.Background = Equals(tile.Tag, "on") ? Theme.Pink : Brushes.Transparent;
            };
            PaintNav(btn, active);
            return btn;
        }

        void PaintNav(Button tab, bool active)
        {
            if (tab == null)
                return;
            var tile = IconTile(tab);
            if (tile != null)
            {
                tile.Tag = active ? "on" : "off";
                tile.Background = active ? Theme.Pink : Brushes.Transparent;
                var icon = tile.Child as TextBlock;
                if (icon != null)
                    icon.Foreground = active ? Brushes.White : Theme.Ink;
                var label = LabelOf(tab);
                if (label != null)
                {
                    // Icon tile carries the accent fill; label stays dark in light theme (white-on-white was invisible).
                    label.Foreground = active
                        ? (Theme.Dark ? Brushes.White : Theme.Pink)
                        : Theme.Ink;
                    label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                }
                tab.FontWeight = FontWeights.Normal;
                return;
            }
            tab.Template = LeftButtonTemplate(active ? Theme.Pink : Theme.Chip, active ? Theme.PinkPressed : Theme.NavHover, 10);
            tab.Foreground = active ? Brushes.White : Theme.Ink;
            tab.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        void SetSidebar(bool expanded)
        {
            settings.SidebarExpanded = expanded;
            settings.Save();
            if (colSidebar != null)
                colSidebar.Width = new GridLength(expanded ? 176 : 64);
            var vis = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (txtNavLogs != null)
                txtNavLogs.Visibility = vis;
            if (txtNavMedia != null)
                txtNavMedia.Visibility = vis;
            if (txtNavApk != null)
                txtNavApk.Visibility = vis;
            if (txtNavSettings != null)
                txtNavSettings.Visibility = vis;
            if (panelSidebarBrand != null)
                panelSidebarBrand.Visibility = vis;
            if (btnAbout != null)
            {
                btnAbout.HorizontalContentAlignment = HorizontalAlignment.Left;
                btnAbout.Margin = new Thickness(12, 0, 12, 14);
            }
            AlignTile(navToggle, expanded);
            AlignTile(tabLogs, expanded);
            AlignTile(tabMedia, expanded);
            AlignTile(tabApk, expanded);
            AlignTile(navSettings, expanded);
        }

        static void AlignTile(Button tab, bool expanded)
        {
            var tile = IconTile(tab);
            if (tile != null)
                tile.HorizontalAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        }

        void ShowNav(string page)
        {
            currentPage = page;
            if (pageLogs != null)
                pageLogs.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
            if (pageMedia != null)
                pageMedia.Visibility = page == "media" ? Visibility.Visible : Visibility.Collapsed;
            if (pageApk != null)
                pageApk.Visibility = page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (pageSettings != null)
                pageSettings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            if (pageAbout != null)
                pageAbout.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
            if (deviceBar != null)
                deviceBar.Visibility = page == "logs" || page == "media" || page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (logsTools != null)
                logsTools.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
            if (mediaTools != null)
                mediaTools.Visibility = page == "media" ? Visibility.Visible : Visibility.Collapsed;
            if (apkTools != null)
                apkTools.Visibility = page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (actionFooter != null)
                actionFooter.Visibility = page == "logs" || page == "media" || page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (footerButtons != null)
                footerButtons.Visibility = page == "apk" ? Visibility.Collapsed : Visibility.Visible;
            if (apkProgressHost != null)
                apkProgressHost.Visibility = page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (copyButton != null)
                copyButton.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
            if (openCurrentButton != null)
                openCurrentButton.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
            if (sendButton != null)
                sendButton.Visibility = page == "media" ? Visibility.Visible : Visibility.Collapsed;
            if (page == "logs")
                ClosePreview();
            if (page == "media")
            {
                FitMediaTiles();
                if (!busy)
                    RefreshMediaAsync();
            }
            if (page == "apk")
                SetStatus(settings.ApkInstallAllDevices
                    ? "把 APK 拖进来，或点「选择 APK」；会装到所有已连接手机"
                    : "把 APK 拖进来，或点「选择 APK」；装到顶栏选中的那台", Theme.Label);
            if (page == "settings")
                BindSettingsFields();
            if (page == "about")
                BeginAboutUpdateCheck();
            PaintNav(tabLogs, page == "logs");
            PaintNav(tabMedia, page == "media");
            PaintNav(tabApk, page == "apk");
            PaintNav(navSettings, page == "settings");
        }

        Border Portrait(double size, double radius)
        {
            var icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), ClipToBounds = true };
            try
            {
                var png = Path.Combine(toolRoot, "icon", "Nanally.png");
                if (File.Exists(png))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(png, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();
                    icon.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                }
            }
            catch { }
            return icon;
        }

        UIElement BuildToolBar()
        {
            var bar = new Border
            {
                Margin = new Thickness(12, 10, 12, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 12, 8)
            };
            Theme.BindElement(bar, BackgroundProperty, "nn.Card");
            Theme.BindElement(bar, Border.BorderBrushProperty, "nn.Hairline");
            ClipRound(bar, 14);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            deviceCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "nodrag",
                Child = deviceHost
            };
            Theme.BindElement(deviceCard, BackgroundProperty, "nn.Field");
            Theme.BindElement(deviceCard, Border.BorderBrushProperty, "nn.Hairline");
            row.Children.Add(deviceCard);
            refreshText = new TextBlock
            {
                Text = "刷新设备",
                FontSize = 13,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 16, 0),
                Tag = "nodrag"
            };
            refreshText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Pink");
            refreshText.MouseLeftButtonUp += delegate
            {
                if (!busy)
                    RefreshAsync();
            };
            Grid.SetColumn(refreshText, 1);
            row.Children.Add(refreshText);
            logsTools = BuildLogsTools();
            mediaTools = BuildMediaTools();
            apkTools = BuildApkTools();
            mediaTools.Visibility = Visibility.Collapsed;
            apkTools.Visibility = Visibility.Collapsed;
            Grid.SetColumn(logsTools, 2);
            Grid.SetColumn(mediaTools, 2);
            Grid.SetColumn(apkTools, 2);
            row.Children.Add(logsTools);
            row.Children.Add(mediaTools);
            row.Children.Add(apkTools);
            bar.Child = row;
            ShowDevicePlaceholder("正在查找手机…", "稍等一下");
            return bar;
        }

        UIElement BuildApkTools()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            chkApkUninstall = IosSwitch();
            chkApkUninstall.IsChecked = settings.ApkUninstallFirst;
            chkApkUninstall.Checked += delegate { OnApkUninstallToggled(); };
            chkApkUninstall.Unchecked += delegate { OnApkUninstallToggled(); };
            bar.Children.Add(ApkOption("先卸载同包名再安装", chkApkUninstall));
            chkApkInstallAll = IosSwitch();
            chkApkInstallAll.IsChecked = settings.ApkInstallAllDevices;
            chkApkInstallAll.Checked += delegate { OnApkInstallAllToggled(); };
            chkApkInstallAll.Unchecked += delegate { OnApkInstallAllToggled(); };
            bar.Children.Add(ApkOption("装到全部已连接手机", chkApkInstallAll));
            return bar;
        }

        UIElement BuildLogsTools()
        {
            var addr = new Grid { VerticalAlignment = VerticalAlignment.Center };
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var up = SmallButton("上级");
            up.Click += delegate { BrowseUp(); };
            addr.Children.Add(up);
            logsPathBox = new TextBox
            {
                Text = logsBrowsePath,
                Height = 32,
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Theme.Field,
                Foreground = Theme.Ink,
                BorderBrush = Theme.Hairline,
                CaretBrush = Theme.Pink
            };
            StyleField(logsPathBox);
            logsPathBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                    ApplyLogsPath(logsPathBox.Text);
            };
            Grid.SetColumn(logsPathBox, 1);
            addr.Children.Add(logsPathBox);
            var browse = SmallButton("浏览");
            browse.Margin = new Thickness(0, 0, 0, 0);
            browse.Click += delegate { BrowseLogsFolder(); };
            Grid.SetColumn(browse, 2);
            addr.Children.Add(browse);
            var refresh = SmallButton("刷新");
            refresh.Margin = new Thickness(8, 0, 0, 0);
            refresh.Click += delegate { RefreshLogsList(); };
            Grid.SetColumn(refresh, 3);
            addr.Children.Add(refresh);
            return addr;
        }

        UIElement BuildMediaTools()
        {
            var bar = new Grid { VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mediaFilter = new ComboBox { Width = 120, Height = 32, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Left };
            mediaFilter.Items.Add("全部");
            mediaFilter.Items.Add("截图");
            mediaFilter.Items.Add("录屏");
            StyleCombo(mediaFilter);
            mediaFilter.SelectionChanged += delegate { RenderMediaRows(); };
            bar.Children.Add(mediaFilter);
            var reload = SmallButton("读取设备");
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Click += delegate { RefreshMediaAsync(); };
            Grid.SetColumn(reload, 1);
            bar.Children.Add(reload);
            var all = SmallButton("全选");
            all.Margin = new Thickness(8, 0, 0, 0);
            all.Click += delegate { SetAllChecked(true); };
            Grid.SetColumn(all, 2);
            bar.Children.Add(all);
            mediaCount = new TextBlock
            {
                Text = "还没读取 · 双击可以预览",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 12
            };
            BindLabel(mediaCount);
            Grid.SetColumn(mediaCount, 3);
            bar.Children.Add(mediaCount);
            return bar;
        }

        Grid BuildLogsPage()
        {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 8) };
            var card = GlassCard();
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = ExplorerHeader(false);
            inner.Children.Add(header);
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var host = new Grid();
            logsRows = new StackPanel();
            logsEmpty = new TextBlock
            {
                Text = "这个目录是空的",
                Margin = new Thickness(12, 16, 0, 0),
                Visibility = Visibility.Collapsed
            };
            BindLabel(logsEmpty);
            host.Children.Add(logsRows);
            host.Children.Add(logsEmpty);
            scroller.Content = host;
            Grid.SetRow(scroller, 1);
            inner.Children.Add(scroller);
            card.Child = inner;
            page.Children.Add(card);
            return page;
        }

        Grid BuildMediaPage()
        {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 8) };
            var card = GlassCard();
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(4, 4, 4, 8)
            };
            var host = new Grid();
            mediaTiles = new WrapPanel { Margin = new Thickness(2, 4, 2, 8) };
            mediaEmpty = new TextBlock
            {
                Text = "点「读取设备」，从手机列出截图和录屏",
                Margin = new Thickness(12, 16, 0, 0)
            };
            BindLabel(mediaEmpty);
            host.Children.Add(mediaTiles);
            host.Children.Add(mediaEmpty);
            scroller.Content = host;
            scroller.SizeChanged += delegate { FitMediaTiles(); };
            card.Child = scroller;
            page.Children.Add(card);
            return page;
        }

        Grid BuildApkPage()
        {
            var page = new Grid { Margin = new Thickness(12, 8, 12, 8) };
            page.AllowDrop = true;
            page.DragOver += OnApkDragOver;
            page.Drop += OnApkDrop;
            var card = GlassCard();
            var stack = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

            var title = new TextBlock
            {
                Text = "安装 APK",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            BindInk(title);
            stack.Children.Add(title);

            var drop = new Border
            {
                MinHeight = 72,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                AllowDrop = true,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Theme.BindElement(drop, BackgroundProperty, "nn.Field");
            Theme.BindElement(drop, Border.BorderBrushProperty, "nn.Hairline");
            drop.DragOver += OnApkDragOver;
            drop.Drop += OnApkDrop;
            drop.MouseLeftButtonUp += delegate
            {
                if (!busy)
                    PickApkFile();
            };
            apkHint = new TextBlock
            {
                Text = "把 .apk 拖到这里，或点击选择",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            BindLabel(apkHint);
            drop.Child = apkHint;
            stack.Children.Add(drop);

            var pick = SmallButton("选择 APK");
            pick.HorizontalAlignment = HorizontalAlignment.Left;
            pick.Margin = new Thickness(0, 0, 0, 12);
            pick.Click += delegate { PickApkFile(); };
            stack.Children.Add(pick);

            var logTitle = new TextBlock
            {
                Text = "安装记录",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            BindInk(logTitle);
            stack.Children.Add(logTitle);
            apkLogScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320
            };
            apkLogPanel = new StackPanel();
            apkLogScroller.Content = apkLogPanel;
            stack.Children.Add(apkLogScroller);

            card.Child = stack;
            page.Children.Add(card);
            return page;
        }

        static UIElement ApkOption(string label, CheckBox box)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            BindInk(text);
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(0);
            row.Children.Add(text);
            row.Children.Add(box);
            return row;
        }

        void OnApkUninstallToggled()
        {
            if (applyingTheme || chkApkUninstall == null)
                return;
            settings.ApkUninstallFirst = chkApkUninstall.IsChecked == true;
            settings.Save();
        }

        void OnApkInstallAllToggled()
        {
            if (applyingTheme || chkApkInstallAll == null)
                return;
            settings.ApkInstallAllDevices = chkApkInstallAll.IsChecked == true;
            settings.Save();
        }

        void OnApkDragOver(object sender, DragEventArgs e)
        {
            if (busy)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        void OnApkDrop(object sender, DragEventArgs e)
        {
            if (busy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            e.Handled = true;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;
            var apks = new List<string>();
            foreach (var path in files)
            {
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    apks.Add(path);
            }
            if (apks.Count == 0)
            {
                SetStatus("请拖入 .apk 文件", Theme.Orange);
                return;
            }
            InstallApksAsync(apks);
        }

        void PickApkFile()
        {
            if (busy)
                return;
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Android APK|*.apk";
            dlg.Multiselect = true;
            dlg.Title = "选择要安装的 APK";
            if (dlg.ShowDialog() != true)
                return;
            var apks = new List<string>();
            foreach (var path in dlg.FileNames)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    apks.Add(path);
            }
            if (apks.Count == 0)
                return;
            InstallApksAsync(apks);
        }

        void AppendApkLog(string text, Brush color)
        {
            if (apkLogPanel == null)
                return;
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var key = color == null ? "nn.Label" : Theme.Key(color);
            if (key != null)
                block.SetResourceReference(TextBlock.ForegroundProperty, key);
            else
                block.Foreground = color;
            // 最新在上，打开记录就能先看到最新
            apkLogPanel.Children.Insert(0, block);
            if (apkLogScroller != null)
                apkLogScroller.ScrollToHome();
            if (!string.IsNullOrEmpty(text))
            {
                if (color == Theme.Red)
                    WhatHappened.Error("apk " + text);
                else if (color == Theme.Orange)
                    WhatHappened.Warn("apk " + text);
                else
                    WhatHappened.Info("apk " + text);
            }
        }

        void ClearApkLog()
        {
            if (apkLogPanel != null)
                apkLogPanel.Children.Clear();
        }

        void SyncApkProgressFill()
        {
            if (apkProgressTrack == null || apkProgressFill == null)
                return;
            var w = apkProgressTrack.ActualWidth - 2;
            if (w < 0)
                w = 0;
            apkProgressFill.Margin = new Thickness(0);
            apkProgressFill.Width = w * (apkProgressValue / 100.0);
        }

        void ShowApkProgress(string detail, double value, bool indeterminate)
        {
            // indeterminate 参数保留兼容，但条只用确定百分比，避免「晃一下却一直 0%」的假进度。
            if (value < 0)
                value = 0;
            if (value > 100)
                value = 100;
            apkProgressValue = value;
            if (apkProgressText != null)
            {
                apkProgressText.Text = ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture) + "%";
                BindInk(apkProgressText);
            }
            SyncApkProgressFill();
        }

        void HideApkProgress()
        {
            apkProgressValue = 0;
            if (apkProgressText != null)
            {
                apkProgressText.Text = "0%";
                BindInk(apkProgressText);
            }
            SyncApkProgressFill();
            apkLastLoggedPercent = -1;
        }

        void InstallApksAsync(List<string> apkPaths)
        {
            if (busy || apkPaths == null || apkPaths.Count == 0)
                return;
            if (adbExe == null)
            {
                SetStatus("没有找到 adb，装好后再试", Theme.Red);
                return;
            }
            var ready = new List<DeviceInfo>();
            if (settings.ApkInstallAllDevices)
            {
                foreach (var item in devices)
                {
                    if (item != null && item.IsReady)
                        ready.Add(item);
                }
            }
            else if (selected != null && selected.IsReady)
            {
                ready.Add(selected);
            }
            if (ready.Count == 0)
            {
                SetStatus(settings.ApkInstallAllDevices
                    ? "没有已授权的手机，先连接并允许调试"
                    : "先在顶栏选一台已授权的手机", Theme.Orange);
                return;
            }

            var uninstallFirst = settings.ApkUninstallFirst;
            var adb = adbExe;
            var paths = new List<string>(apkPaths);
            var parallel = ready.Count > 1;
            busy = true;
            UpdateButtons();
            ClearApkLog();
            apkProgressStartedUtc = DateTime.UtcNow;
            WhatHappened.Info("安装 APK 开始 files=" + paths.Count.ToString(CultureInfo.InvariantCulture)
                + " devices=" + ready.Count.ToString(CultureInfo.InvariantCulture)
                + " uninstallFirst=" + (uninstallFirst ? "1" : "0")
                + " allDevices=" + (settings.ApkInstallAllDevices ? "1" : "0"));
            ShowApkProgress(null, 1, false);
            SetStatus("正在安装到 " + ready.Count.ToString(CultureInfo.InvariantCulture) + " 台手机…", Theme.Label);

            Task.Run(delegate
            {
                var counts = new int[2]; // 0=ok, 1=fail
                var countLock = new object();
                foreach (var apkPath in paths)
                {
                    var fileName = Path.GetFileName(apkPath);
                    string packageName = null;
                    string packageError = null;
                    if (uninstallFirst)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            ShowApkProgress(null, 3, false);
                        }));
                        packageName = Apk.ReadPackageName(apkPath, out packageError);
                        if (string.IsNullOrEmpty(packageName))
                        {
                            lock (countLock) { counts[1]++; }
                            var msg = "读包名失败：" + fileName + " — " + (packageError ?? "未知错误");
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                AppendApkLog(msg, Theme.Red);
                                SetStatus(msg, Theme.Red);
                            }));
                            continue;
                        }
                    }

                    var pkg = packageName;
                    var deviceCount = ready.Count;
                    var deviceProg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in ready)
                        deviceProg[d.Serial] = 0;
                    var progLock = new object();
                    Action<string, double> setDeviceProg = delegate(string serial, double pct)
                    {
                        if (string.IsNullOrEmpty(serial))
                            return;
                        if (pct < 0)
                            pct = 0;
                        if (pct > 100)
                            pct = 100;
                        double avg;
                        lock (progLock)
                        {
                            double old;
                            if (deviceProg.TryGetValue(serial, out old) && pct < old)
                                pct = old;
                            deviceProg[serial] = pct;
                            double sum = 0;
                            foreach (var d in ready)
                            {
                                double p;
                                if (!deviceProg.TryGetValue(d.Serial, out p))
                                    p = 0;
                                sum += p;
                            }
                            avg = deviceCount <= 0 ? 0 : (sum / deviceCount);
                        }
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            ShowApkProgress(null, avg, false);
                        }));
                    };

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog("开始 " + fileName + (string.IsNullOrEmpty(pkg) ? "" : "（" + pkg + "）")
                            + " → " + deviceCount.ToString(CultureInfo.InvariantCulture) + " 台"
                            + (deviceCount > 1 ? "（并行推送）" : ""), Theme.Ink);
                        ShowApkProgress(null, 5, false);
                    }));

                    var tasks = new List<Task>();
                    foreach (var device in ready)
                    {
                        var target = device;
                        var apk = apkPath;
                        var uninstall = uninstallFirst;
                        var package = pkg;
                        var report = setDeviceProg;
                        tasks.Add(Task.Factory.StartNew(delegate
                        {
                            RunInstallOnDevice(adb, target, apk, uninstall, package, counts, countLock, parallel, report);
                        }));
                    }
                    try { Task.WaitAll(tasks.ToArray()); }
                    catch { }
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    UpdateButtons();
                    if (counts[1] == 0)
                    {
                        ShowApkProgress(null, 100, false);
                        SetStatus("全部完成：成功 " + counts[0].ToString(CultureInfo.InvariantCulture) + " 次", Theme.PinkSoft);
                    }
                    else
                    {
                        ShowApkProgress(null, 100, false);
                        SetStatus("完成：成功 " + counts[0].ToString(CultureInfo.InvariantCulture) + "，失败 " + counts[1].ToString(CultureInfo.InvariantCulture), Theme.Orange);
                    }
                    var hide = new DispatcherTimer();
                    hide.Interval = TimeSpan.FromSeconds(2.5);
                    hide.Tick += delegate
                    {
                        hide.Stop();
                        if (!busy)
                            HideApkProgress();
                    };
                    hide.Start();
                }));
            });
        }

        void RunInstallOnDevice(string adb, DeviceInfo device, string apkPath, bool uninstallFirst, string packageName, int[] counts, object countLock, bool parallel, Action<string, double> reportProgress)
        {
            var label = device.DisplayName + " [" + device.Serial + "]";
            var serial = device.Serial;
            var phase = "push";
            var lastPct = -1;
            var lastUi = DateTime.UtcNow;
            var phaseStarted = DateTime.UtcNow;
            Timer heartbeat = null;
            Action<double> bump = delegate(double pct)
            {
                if (reportProgress != null)
                    reportProgress(serial, pct);
            };

            Action refreshHeartbeat = delegate
            {
                var sec = (int)(DateTime.UtcNow - phaseStarted).TotalSeconds;
                if (sec < 2)
                    return;
                if (string.Equals(phase, "push", StringComparison.Ordinal))
                {
                    var mapped = lastPct >= 0 ? (10 + lastPct * 0.65) : 12;
                    bump(mapped);
                    if (!parallel)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetStatus(label + " 推送中…已等 " + sec.ToString(CultureInfo.InvariantCulture) + "s"
                                + (lastPct >= 0 ? "（" + lastPct.ToString(CultureInfo.InvariantCulture) + "%）" : ""), Theme.Label);
                        }));
                    }
                }
                else if (string.Equals(phase, "install", StringComparison.Ordinal))
                {
                    bump(88);
                    if (!parallel)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetStatus(label + " 等待 pm install / 手机确认…已等 " + sec.ToString(CultureInfo.InvariantCulture) + "s", Theme.Label);
                        }));
                    }
                }
            };

            Action<string> onAdbLine = delegate(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                var text = line.Trim();
                var pct = Adb.TryParsePercent(text);
                if (pct >= 0 && string.Equals(phase, "push", StringComparison.Ordinal))
                {
                    var now = DateTime.UtcNow;
                    if (pct > lastPct && (pct >= lastPct + 2 || (now - lastUi).TotalMilliseconds >= 350 || pct == 100))
                    {
                        lastPct = pct;
                        lastUi = now;
                        var mapped = 10 + pct * 0.65;
                        bump(mapped);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetStatus(label + " 推送 " + pct.ToString(CultureInfo.InvariantCulture) + "%", Theme.Label);
                            if (pct >= apkLastLoggedPercent + 10 || pct == 100)
                            {
                                apkLastLoggedPercent = pct;
                                AppendApkLog(label + " · 推送 " + pct.ToString(CultureInfo.InvariantCulture) + "%", Theme.Label);
                            }
                        }));
                    }
                    return;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    AppendApkLog(label + " · " + text, Theme.Label);
                    SetStatus(label + " · " + text, Theme.Label);
                }));
            };

            Action<string> onPhase = delegate(string next)
            {
                phase = next ?? phase;
                phaseStarted = DateTime.UtcNow;
                if (string.Equals(phase, "push", StringComparison.Ordinal))
                {
                    bump(10);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog(label + " · 开始推送（push）", Theme.Ink);
                    }));
                }
                else if (string.Equals(phase, "install", StringComparison.Ordinal))
                {
                    bump(85);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog(label + " · 推送完成，开始 pm install（无输出时看手机确认框）", Theme.Ink);
                        if (!parallel)
                            SetStatus(label + " pm install 中…", Theme.Label);
                    }));
                }
            };

            heartbeat = new Timer(delegate { refreshHeartbeat(); }, null, 1000, 1500);

            try
            {
                bump(4);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    AppendApkLog(label + " · 开始（push + pm install）", Theme.Ink);
                    if (!parallel)
                        SetStatus("正在安装到 " + label + "…", Theme.Label);
                }));

                if (uninstallFirst && !string.IsNullOrEmpty(packageName))
                {
                    if (Apk.IsInstalled(adb, device.Serial, packageName))
                    {
                        phase = "uninstall";
                        phaseStarted = DateTime.UtcNow;
                        bump(8);
                        var un = Apk.Uninstall(adb, device.Serial, packageName, onAdbLine);
                        if (un.TimedOut || un.ExitCode != 0)
                        {
                            lock (countLock) { counts[1]++; }
                            bump(100);
                            var detail = Adb.FirstLine(un.Stderr.Length > 0 ? un.Stderr : un.Stdout);
                            var msg = label + " 卸载失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
                            Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(msg, Theme.Red); }));
                            return;
                        }
                        Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(label + " 已卸载旧包", Theme.Label); }));
                    }
                }

                var installed = Apk.Install(adb, device.Serial, apkPath, !uninstallFirst, onAdbLine, onPhase);
                if (installed.TimedOut)
                {
                    lock (countLock) { counts[1]++; }
                    bump(100);
                    Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(label + " 安装超时（可看手机是否还在等确认）", Theme.Red); }));
                    return;
                }
                var outText = ((installed.Stdout ?? "") + "\n" + (installed.Stderr ?? "")).ToLowerInvariant();
                // 不少机型会打出 Success 但 adb/shell 退出码非 0；以文案为准，再用 pm path 兜底。
                var success = outText.IndexOf("success") >= 0;
                if (!success && outText.IndexOf("failure") < 0 && outText.IndexOf("error:") < 0 && installed.ExitCode == 0)
                    success = true;
                if (!success)
                {
                    var pkg = packageName;
                    string pkgErr;
                    if (string.IsNullOrEmpty(pkg))
                        pkg = Apk.ReadPackageName(apkPath, out pkgErr);
                    if (!string.IsNullOrEmpty(pkg) && Apk.IsInstalled(adb, device.Serial, pkg))
                        success = true;
                }
                bump(100);
                if (success)
                {
                    lock (countLock) { counts[0]++; }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog(label + " 安装成功", Theme.PinkSoft);
                    }));
                }
                else
                {
                    lock (countLock) { counts[1]++; }
                    var detail = Adb.FirstLine(installed.Stderr.Length > 0 ? installed.Stderr : installed.Stdout);
                    if (string.IsNullOrEmpty(detail))
                        detail = "exit " + installed.ExitCode.ToString(CultureInfo.InvariantCulture);
                    var msg = label + " 安装失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
                    Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(msg, Theme.Red); }));
                }
            }
            finally
            {
                if (heartbeat != null)
                {
                    try { heartbeat.Dispose(); } catch { }
                }
            }
        }

        static Border GlassCard()
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14)
            };
            Theme.BindElement(card, BackgroundProperty, "nn.Card");
            Theme.BindElement(card, Border.BorderBrushProperty, "nn.Hairline");
            ClipRound(card, 14);
            return card;
        }

        // WPF CornerRadius only rounds the Border chrome; children still paint square unless clipped.
        static void ClipRound(FrameworkElement element, double radius)
        {
            if (element == null)
                return;
            element.SizeChanged += delegate { ApplyRoundClip(element, radius); };
            element.Loaded += delegate { ApplyRoundClip(element, radius); };
        }

        static void ApplyRoundClip(FrameworkElement element, double radius)
        {
            if (element == null)
                return;
            var w = element.ActualWidth;
            var h = element.ActualHeight;
            if (w < 1 || h < 1)
                return;
            element.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
        }

        UIElement ExplorerHeader(bool media)
        {
            var header = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Padding = new Thickness(0)
            };
            Theme.BindElement(header, BackgroundProperty, "nn.CardSoft");
            var grid = HeaderGrid(media);
            AddHeader(grid, 0, media ? "" : "");
            AddHeader(grid, 1, "名称");
            AddHeader(grid, 2, "修改时间");
            AddHeader(grid, 3, "大小");
            AddHeader(grid, 4, media ? "来源" : "类型");
            header.Child = grid;
            return header;
        }

        static Grid HeaderGrid(bool media)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(media ? 36 : 8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            return grid;
        }

        static void AddHeader(Grid grid, int col, string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            BindLabel(block);
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        UIElement BuildActionFooter()
        {
            var footer = new Border
            {
                Margin = new Thickness(12, 0, 12, 10),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 10, 16, 12)
            };
            Theme.BindElement(footer, BackgroundProperty, "nn.Card");
            Theme.BindElement(footer, Border.BorderBrushProperty, "nn.Hairline");
            ClipRound(footer, 14);
            var stack = new StackPanel();

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock
            {
                Text = "连上手机，点一下就拷贝",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            row.Children.Add(statusText);
            footerRightHost = new Grid { MinWidth = 180, VerticalAlignment = VerticalAlignment.Center };
            footerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            openCurrentButton = SmallButton("打开文件夹");
            openCurrentButton.Margin = new Thickness(0, 0, 8, 0);
            openCurrentButton.Click += delegate { OpenCurrentFolder(); };
            copyButton = MakePrimary("拷贝日志");
            copyButton.IsEnabled = false;
            copyButton.Click += delegate { StartCopy(); };
            sendButton = MakePrimary("发给自己");
            sendButton.Visibility = Visibility.Collapsed;
            sendButton.Click += delegate { SendSelectedAsync(); };
            footerButtons.Children.Add(openCurrentButton);
            footerButtons.Children.Add(copyButton);
            footerButtons.Children.Add(sendButton);
            footerRightHost.Children.Add(footerButtons);

            apkProgressHost = new Grid { Visibility = Visibility.Collapsed, MinWidth = 200 };
            apkProgressHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            apkProgressHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            apkProgressTrack = new Border
            {
                Height = 12,
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                MinWidth = 120,
                ClipToBounds = true
            };
            Theme.BindElement(apkProgressTrack, BackgroundProperty, "nn.Chip");
            Theme.BindElement(apkProgressTrack, Border.BorderBrushProperty, "nn.Hairline");
            apkProgressTrack.BorderThickness = new Thickness(1);
            apkProgressFill = new Border
            {
                Width = 0,
                Height = 12,
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Theme.BindElement(apkProgressFill, BackgroundProperty, "nn.Pink");
            apkProgressTrack.Child = apkProgressFill;
            apkProgressTrack.SizeChanged += delegate { SyncApkProgressFill(); };
            Grid.SetColumn(apkProgressTrack, 0);
            apkProgressHost.Children.Add(apkProgressTrack);
            apkProgressText = new TextBlock
            {
                Text = "0%",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 44,
                TextAlignment = TextAlignment.Right
            };
            BindInk(apkProgressText);
            Grid.SetColumn(apkProgressText, 1);
            apkProgressHost.Children.Add(apkProgressText);
            footerRightHost.Children.Add(apkProgressHost);

            Grid.SetColumn(footerRightHost, 1);
            row.Children.Add(footerRightHost);
            stack.Children.Add(row);
            footer.Child = stack;
            return footer;
        }

        Button MakePrimary(string text)
        {
            var btn = new Button
            {
                Content = text,
                Height = 32,
                MinWidth = 88,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.OnAccent,
                Background = Theme.Pink,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btn.Template = FlatButtonTemplate(Theme.Pink, Theme.PinkPressed, 6);
            return btn;
        }

        Button SmallButton(string text)
        {
            var btn = new Button
            {
                Content = text,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = Cursors.Hand,
                FontSize = 12
            };
            btn.SetResourceReference(Control.ForegroundProperty, "nn.Ink");
            btn.SetResourceReference(Control.BackgroundProperty, "nn.CardSoft");
            btn.Template = OutlineButtonTemplate();
            return btn;
        }

        static ControlTemplate OutlineButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            Theme.Bind(border, Border.BackgroundProperty, Theme.CardSoft);
            Theme.Bind(border, Border.BorderBrushProperty, Theme.Hairline);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 0, 12, 0));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);
            template.VisualTree = border;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(Theme.Dyn(Border.BackgroundProperty, Theme.AccentSoft, "Bd"));
            over.Setters.Add(Theme.Dyn(Border.BorderBrushProperty, Theme.Pink, "Bd"));
            template.Triggers.Add(over);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(off);
            return template;
        }

        static ControlTemplate LeftButtonTemplate(Brush normal, Brush hover, double radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            Theme.Bind(border, Border.BackgroundProperty, normal);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(Theme.Dyn(Border.BackgroundProperty, hover, "Bd"));
            template.Triggers.Add(over);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(off);
            return template;
        }

        static ControlTemplate FlatButtonTemplate(Brush normal, Brush hover, double radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            Theme.Bind(border, Border.BackgroundProperty, normal);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(Theme.Dyn(Border.BackgroundProperty, hover, "Bd"));
            template.Triggers.Add(over);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(off);
            return template;
        }

        static SolidColorBrush B(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        void BrowseLogsFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择日志保存目录";
            dlg.SelectedPath = Directory.Exists(logsBrowsePath) ? logsBrowsePath : settings.LogsDir;
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            ApplyLogsPath(dlg.SelectedPath);
        }

        void ApplyLogsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("目录不存在", Theme.Red);
                return;
            }
            logsBrowsePath = path;
            settings.LogsDir = path;
            settings.Save();
            if (logsPathBox != null)
                logsPathBox.Text = path;
            RefreshLogsList();
        }

        void BrowseUp()
        {
            if (string.IsNullOrEmpty(logsBrowsePath))
                return;
            var parent = Path.GetDirectoryName(logsBrowsePath.TrimEnd('\\'));
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return;
            logsBrowsePath = parent;
            if (logsPathBox != null)
                logsPathBox.Text = parent;
            RefreshLogsList();
        }

        void RefreshLogsList()
        {
            if (logsRows == null)
                return;
            logsRows.Children.Clear();
            if (string.IsNullOrEmpty(logsBrowsePath) || !Directory.Exists(logsBrowsePath))
            {
                logsEmpty.Text = "目录不存在";
                logsEmpty.Visibility = Visibility.Visible;
                return;
            }
            if (logsPathBox != null)
                logsPathBox.Text = logsBrowsePath;
            var dirs = new List<DirectoryInfo>(new DirectoryInfo(logsBrowsePath).GetDirectories());
            dirs.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            var files = new List<FileInfo>(new DirectoryInfo(logsBrowsePath).GetFiles());
            files.Sort(delegate(FileInfo a, FileInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            foreach (var dir in dirs)
                logsRows.Children.Add(LocalRow(dir.Name, dir.LastWriteTime, "", "文件夹", dir.FullName, true));
            foreach (var file in files)
                logsRows.Children.Add(LocalRow(file.Name, file.LastWriteTime, Paths.FormatSize(file.Length), "文件", file.FullName, false));
            logsEmpty.Visibility = logsRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            logsEmpty.Text = "这个目录是空的";
        }

        Border LocalRow(string name, DateTime when, string size, string kind, string full, bool folder)
        {
            var row = MakeRow(false);
            AddCell(row, 1, name, true);
            AddCell(row, 2, when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), false);
            AddCell(row, 3, size, false);
            AddCell(row, 4, kind, false);
            row.MouseLeftButtonUp += delegate
            {
                if (folder)
                {
                    logsBrowsePath = full;
                    RefreshLogsList();
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
            };
            return row;
        }

        void RefreshMediaAsync()
        {
            if (busy)
                return;
            if (selected == null || !selected.IsReady || adbExe == null)
            {
                SetStatus("先连上一台已授权的手机", Theme.Orange);
                return;
            }
            busy = true;
            UpdateButtons();
            SetStatus("正在读取手机上的截图和录屏…", Theme.Label);
            var adb = adbExe;
            var serial = selected.Serial;
            Task.Run(delegate
            {
                var found = new List<MediaItem>();
                string error = null;
                try { found = ListDeviceMedia(adb, serial); }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    mediaItems.Clear();
                    mediaItems.AddRange(found);
                    ResetThumbs();
                    RenderMediaRows();
                    if (error != null)
                        SetStatus(error, Theme.Red);
                    else if (found.Count == 0)
                        SetStatus("这些常见目录里没有截图或录屏", Theme.Orange);
                    else
                        SetStatus("读到 " + found.Count.ToString(CultureInfo.InvariantCulture) + " 个。点一下勾选，双击预览", Theme.Label);
                    UpdateButtons();
                }));
            });
        }

        static List<MediaItem> ListDeviceMedia(string adb, string serial)
        {
            var list = new List<MediaItem>();
            foreach (var root in MediaRoots)
            {
                var listed = Adb.Run(adb, serial, "shell ls -lt \"" + root + "\"", 15000, null);
                if (listed.ExitCode != 0 || string.IsNullOrEmpty(listed.Stdout))
                    continue;
                foreach (var raw in listed.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = ParseLs(raw, root);
                    if (item != null)
                        list.Add(item);
                }
            }
            list.Sort(CompareMediaNewestFirst);
            return list;
        }

        static int CompareMediaNewestFirst(MediaItem a, MediaItem b)
        {
            var byTime = b.DateTicks.CompareTo(a.DateTicks);
            if (byTime != 0)
                return byTime;
            return string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
        }

        static MediaItem ParseLs(string line, string folder)
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith("total", StringComparison.OrdinalIgnoreCase) || text.StartsWith("d", StringComparison.Ordinal))
                return null;
            if (!text.StartsWith("-", StringComparison.Ordinal))
                return null;
            string name = null;
            long size = 0;
            string dateText = "";
            long ticks = 0;
            var iso = Regex.Match(text, @"\s(\d+)\s+(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}(?::\d{2})?)\s+(.+)$");
            if (iso.Success)
            {
                long.TryParse(iso.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
                dateText = iso.Groups[2].Value + " " + iso.Groups[3].Value;
                if (dateText.Length == 16)
                    dateText += ":00";
                DateTime when;
                if (DateTime.TryParseExact(dateText, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    ticks = when.Ticks;
                name = iso.Groups[4].Value.Trim();
            }
            else
            {
                var legacy = Regex.Match(text, @"\s(\d+)\s+([A-Za-z]{3})\s+(\d{1,2})\s+(\d{2}:\d{2}|\d{4})\s+(.+)$");
                if (!legacy.Success)
                    return null;
                long.TryParse(legacy.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
                name = legacy.Groups[5].Value.Trim();
                ticks = ParseLegacyLsDate(legacy.Groups[2].Value, legacy.Groups[3].Value, legacy.Groups[4].Value, out dateText);
            }
            if (name == "." || name == ".." || name.Length == 0)
                return null;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var video = ext == ".mp4" || ext == ".mov" || ext == ".mkv" || ext == ".3gp" || ext == ".webm";
            var image = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".gif";
            if (!video && !image)
                return null;
            if (ticks <= 0)
                ticks = GuessTicksFromName(name);
            if (ticks > 0 && string.IsNullOrEmpty(dateText))
                dateText = new DateTime(ticks).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var item = new MediaItem();
            item.Name = name;
            item.RemotePath = folder.TrimEnd('/') + "/" + name;
            item.Folder = folder;
            item.Size = size;
            item.DateText = dateText;
            item.DateTicks = ticks;
            item.IsVideo = video;
            return item;
        }

        static long ParseLegacyLsDate(string month, string day, string timeOrYear, out string dateText)
        {
            dateText = "";
            int d;
            if (!int.TryParse(day, NumberStyles.Integer, CultureInfo.InvariantCulture, out d))
                return 0;
            DateTime when;
            if (timeOrYear.IndexOf(':') >= 0)
            {
                var year = DateTime.Now.Year;
                if (!DateTime.TryParseExact(month + " " + d.ToString("00", CultureInfo.InvariantCulture) + " " + year.ToString(CultureInfo.InvariantCulture) + " " + timeOrYear, "MMM dd yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    return 0;
                if (when > DateTime.Now.AddDays(2))
                    when = when.AddYears(-1);
            }
            else
            {
                if (!DateTime.TryParseExact(month + " " + d.ToString("00", CultureInfo.InvariantCulture) + " " + timeOrYear, "MMM dd yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    return 0;
            }
            dateText = when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return when.Ticks;
        }

        static long GuessTicksFromName(string name)
        {
            var m = Regex.Match(name, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})");
            if (!m.Success)
                return 0;
            DateTime when;
            if (!DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                return 0;
            return when.Ticks;
        }

        void RenderMediaRows()
        {
            if (mediaTiles == null)
                return;
            mediaTiles.Children.Clear();
            var filter = mediaFilter == null ? 0 : mediaFilter.SelectedIndex;
            var shown = 0;
            var ordered = new List<MediaItem>();
            foreach (var item in mediaItems)
            {
                if (filter == 1 && item.IsVideo)
                    continue;
                if (filter == 2 && !item.IsVideo)
                    continue;
                ordered.Add(item);
            }
            ordered.Sort(CompareMediaNewestFirst);
            foreach (var item in ordered)
            {
                mediaTiles.Children.Add(MediaTile(item));
                shown++;
            }
            mediaEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
            mediaEmpty.Text = mediaItems.Count == 0 ? "正在读取或点「读取设备」" : "这个筛选下没有文件";
            UpdateMediaCount();
            FitMediaTiles();
            foreach (var item in ordered)
                EnqueueThumb(item);
        }

        Border MediaTile(MediaItem item)
        {
            var tile = new Border
            {
                Width = 148,
                Margin = new Thickness(4),
                Padding = new Thickness(6, 6, 6, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = BuildMediaTip(item)
            };
            var stack = new StackPanel();
            var frame = new Border
            {
                Width = 136,
                Height = 102,
                Background = B("#111111"),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true
            };
            var box = new Grid();
            var hint = new TextBlock
            {
                Text = item.IsVideo ? "▶" : "图",
                Foreground = Theme.Label,
                FontSize = item.IsVideo ? 22 : 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var image = new Image { Stretch = Stretch.Uniform };
            if (item.Thumb != null)
            {
                image.Source = item.Thumb;
                hint.Visibility = Visibility.Collapsed;
            }
            item.ThumbHost = image;
            item.ThumbHint = hint;
            box.Children.Add(hint);
            box.Children.Add(image);
            if (item.IsVideo)
            {
                var play = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = B("#CCEC4899"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = new TextBlock
                    {
                        Text = "▶",
                        Foreground = Brushes.White,
                        FontSize = 9,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(1, 0, 0, 0)
                    }
                };
                play.Tag = "play";
                play.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    OpenPreview(item);
                };
                box.Children.Add(play);
            }
            var check = new CheckBox
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 6, 0, 0),
                Focusable = false,
                Cursor = Cursors.Hand,
                Template = DotCheckTemplate()
            };
            check.Checked += delegate { item.IsChecked = true; PaintDot(check); PaintTile(tile, item, tile.IsMouseOver); UpdateMediaCount(); };
            check.Unchecked += delegate { item.IsChecked = false; PaintDot(check); PaintTile(tile, item, tile.IsMouseOver); UpdateMediaCount(); };
            check.Loaded += delegate { PaintDot(check); };
            check.IsChecked = item.IsChecked;
            box.Children.Add(check);
            frame.Child = box;
            stack.Children.Add(frame);
            stack.Children.Add(new TextBlock
            {
                Text = item.Name,
                Foreground = Theme.Ink,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Margin = new Thickness(2, 6, 2, 0)
            });
            tile.Child = stack;
            PaintTile(tile, item, false);
            var clicks = 0;
            tile.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsCheckBox(e.OriginalSource as DependencyObject) || IsPlayBadge(e.OriginalSource as DependencyObject))
                    return;
                clicks = e.ClickCount;
                if (e.ClickCount >= 2)
                    OpenPreview(item);
            };
            tile.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsCheckBox(e.OriginalSource as DependencyObject) || IsPlayBadge(e.OriginalSource as DependencyObject) || clicks >= 2)
                    return;
                item.IsChecked = !item.IsChecked;
                check.IsChecked = item.IsChecked;
                PaintDot(check);
                PaintTile(tile, item, true);
                UpdateMediaCount();
            };
            tile.MouseEnter += delegate { PaintTile(tile, item, true); };
            tile.MouseLeave += delegate { PaintTile(tile, item, false); };
            return tile;
        }

        static ControlTemplate DotCheckTemplate()
        {
            var template = new ControlTemplate(typeof(CheckBox));
            var ring = new FrameworkElementFactory(typeof(Border));
            ring.Name = "Dot";
            ring.SetValue(FrameworkElement.WidthProperty, 12.0);
            ring.SetValue(FrameworkElement.HeightProperty, 12.0);
            ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            ring.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            ring.SetValue(Border.BorderBrushProperty, B("#E6FFFFFF"));
            ring.SetValue(Border.BackgroundProperty, B("#66000000"));
            template.VisualTree = ring;
            return template;
        }

        static void PaintDot(CheckBox check)
        {
            if (check == null || check.Template == null)
                return;
            var dot = check.Template.FindName("Dot", check) as Border;
            if (dot == null)
                return;
            if (check.IsChecked == true)
            {
                dot.Background = Theme.Pink;
                dot.BorderBrush = Theme.Pink;
            }
            else
            {
                dot.Background = B("#66000000");
                dot.BorderBrush = B("#E6FFFFFF");
            }
        }

        static bool IsCheckBox(DependencyObject source)
        {
            while (source != null)
            {
                if (source is CheckBox)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        static bool IsPlayBadge(DependencyObject source)
        {
            while (source != null)
            {
                var fe = source as FrameworkElement;
                if (fe != null && Equals(fe.Tag, "play"))
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        static void PaintTile(Border tile, MediaItem item, bool hover)
        {
            if (item.IsChecked)
            {
                tile.Background = Theme.Selected;
                tile.BorderBrush = Theme.Pink;
            }
            else if (hover)
            {
                tile.Background = Theme.RowHover;
                tile.BorderBrush = Theme.Hairline;
            }
            else
            {
                tile.Background = Brushes.Transparent;
                tile.BorderBrush = Brushes.Transparent;
            }
        }

        static ToolTip BuildMediaTip(MediaItem item)
        {
            var stack = new StackPanel();
            stack.Children.Add(TipLine("类型：", KindLabel(item)));
            stack.Children.Add(TipLine("大小：", Paths.FormatSize(item.Size)));
            stack.Children.Add(TipLine("修改日期：", FormatMediaDate(item.DateText)));
            return new ToolTip
            {
                Background = Theme.Card,
                BorderBrush = Theme.Hairline,
                Padding = new Thickness(10, 8, 10, 8),
                Content = stack
            };
        }

        static TextBlock TipLine(string label, string value)
        {
            var line = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1, 0, 1) };
            line.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Theme.Label });
            line.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = Theme.Ink });
            return line;
        }

        static string KindLabel(MediaItem item)
        {
            var ext = Path.GetExtension(item.Name).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
                return "JPEG 图像";
            if (ext == ".png")
                return "PNG 图像";
            if (ext == ".webp")
                return "WEBP 图像";
            if (ext == ".gif")
                return "GIF 图像";
            if (ext == ".mp4")
                return "MP4 视频";
            if (ext == ".mov")
                return "MOV 视频";
            if (ext == ".mkv")
                return "MKV 视频";
            if (ext == ".webm")
                return "WEBM 视频";
            if (ext == ".3gp")
                return "3GP 视频";
            return item.IsVideo ? "视频" : "图像";
        }

        static string FormatMediaDate(string text)
        {
            DateTime when;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                return when.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
            return text ?? "";
        }

        void UpdateMediaCount()
        {
            var picked = 0;
            foreach (var item in mediaItems)
            {
                if (item.IsChecked)
                    picked++;
            }
            if (mediaCount != null)
            {
                var text = "已选 " + picked.ToString(CultureInfo.InvariantCulture) + " / " + mediaItems.Count.ToString(CultureInfo.InvariantCulture);
                if (thumbTotal > 0 && thumbDone < thumbTotal)
                    text += " · 预览 " + thumbDone.ToString(CultureInfo.InvariantCulture) + "/" + thumbTotal.ToString(CultureInfo.InvariantCulture);
                mediaCount.Text = text;
            }
        }

        Border MakeRow(bool media)
        {
            var grid = HeaderGrid(media);
            var row = new Border
            {
                Child = grid,
                Height = 34,
                Background = Brushes.Transparent,
                BorderBrush = Theme.Hairline,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand
            };
            row.MouseEnter += delegate { if (row.Background == Brushes.Transparent) row.Background = Theme.RowHover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            return row;
        }

        void AddCell(Border row, int col, string text, bool strong)
        {
            var grid = row.Child as Grid;
            var block = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = strong ? Theme.Ink : Theme.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        void SetAllChecked(bool value)
        {
            var filter = mediaFilter == null ? 0 : mediaFilter.SelectedIndex;
            foreach (var item in mediaItems)
            {
                if (filter == 1 && item.IsVideo)
                    continue;
                if (filter == 2 && !item.IsVideo)
                    continue;
                item.IsChecked = value;
            }
            RenderMediaRows();
        }

        void SendSelectedAsync()
        {
            if (busy)
                return;
            var chosen = new List<MediaItem>();
            foreach (var item in mediaItems)
            {
                if (item.IsChecked)
                    chosen.Add(item);
            }
            if (chosen.Count == 0)
            {
                SetStatus("先勾选要发的截图或录屏", Theme.Orange);
                return;
            }
            if (selected == null || !selected.IsReady || adbExe == null)
            {
                SetStatus("手机不在线", Theme.Red);
                return;
            }
            var chat = settings.FeishuChatId == null ? "" : settings.FeishuChatId.Trim();
            if (chat.Length == 0)
            {
                SetStatus("先在设置里填自己的飞书会话 ID", Theme.Orange);
                return;
            }
            busy = true;
            UpdateButtons();
            SetStatus("正在准备发给自己…", Theme.Label);
            WhatHappened.Info("飞书发送开始 count=" + chosen.Count.ToString(CultureInfo.InvariantCulture) + " chatIdLen=" + chat.Length.ToString(CultureInfo.InvariantCulture));
            var adb = adbExe;
            var serial = selected.Serial;
            var useStored = !string.IsNullOrWhiteSpace(settings.FeishuToken);
            Task.Run(delegate
            {
                string message;
                var ok = false;
                try
                {
                    message = SendMedia(adb, serial, chosen, chat, useStored);
                    ok = true;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    SetStatus(message, ok ? Theme.PinkSoft : Theme.Red);
                    if (ok)
                        WhatHappened.Info("飞书发送完成: " + message);
                    else
                        WhatHappened.Error("飞书发送失败: " + message);
                    UpdateButtons();
                }));
            });
        }

        string SendMedia(string adb, string serial, List<MediaItem> chosen, string chatId, bool useStored)
        {
            var cli = FindLarkRunner();
            if (cli == null)
                throw new InvalidOperationException("没有找到 lark-cli。请先安装并加入 PATH，再点设置里的「登录飞书 CLI」。");
            var asWho = EnsureLarkIdentity(cli, useStored);
            var sent = 0;
            var failed = 0;
            var lastError = "";
            var temp = Path.Combine(toolRoot, ".nanally-send");
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);
                for (var i = 0; i < chosen.Count; i++)
                {
                    var item = chosen[i];
                    var n = i + 1;
                    var fileName = item.Name;
                    var total = chosen.Count;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        SetStatus("正在发送 " + n.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + " " + fileName, Theme.Label);
                    }));
                    var localDir = Path.Combine(temp, n.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(localDir);
                    var local = Path.Combine(localDir, item.Name);
                    string cached = null;
                    lock (item.Gate)
                    {
                        if (!string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
                            cached = item.LocalPath;
                    }
                    if (cached != null)
                    {
                        File.Copy(cached, local, true);
                    }
                    else
                    {
                        var pulled = Adb.Run(adb, serial, "pull \"" + item.RemotePath + "\" \"" + localDir + "\"", 180000, null);
                        if (pulled.ExitCode != 0)
                        {
                            failed++;
                            lastError = "adb pull 失败：" + fileName;
                            continue;
                        }
                        var files = Directory.GetFiles(localDir);
                        if (files.Length == 0)
                        {
                            failed++;
                            lastError = "pull 后没有文件：" + fileName;
                            continue;
                        }
                        local = files[0];
                    }
                    AdbResult result;
                    if (item.IsVideo)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetStatus("正在生成视频封面 " + fileName, Theme.Label);
                        }));
                        var cover = Path.Combine(localDir, "nanally-cover.jpg");
                        if (!EnsureVideoCover(local, cover))
                        {
                            failed++;
                            lastError = "无法生成视频封面（需要 ffmpeg）。视频不能当普通文件发。";
                            continue;
                        }
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetStatus("正在上传视频 " + n.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + " " + fileName, Theme.Label);
                        }));
                        result = RunLarkVideo(cli, local, cover, chatId, asWho, false);
                    }
                    else
                    {
                        result = RunLark(cli, local, "--image", chatId, asWho, false);
                    }
                    var outText = ((result.Stdout ?? "") + "\n" + (result.Stderr ?? "")).Trim();
                    if (result.ExitCode == 0 && IsLarkSendOk(outText))
                        sent++;
                    else
                    {
                        failed++;
                        lastError = FormatLarkError(outText, "exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
            if (sent == 0)
            {
                if (lastError.Length == 0)
                    lastError = "可在设置确认会话 ID，并确认机器人已在该会话中";
                throw new InvalidOperationException("没有发出去：" + lastError);
            }
            return "已发给自己 " + sent.ToString(CultureInfo.InvariantCulture) + " 个" + (failed > 0 ? "，失败 " + failed.ToString(CultureInfo.InvariantCulture) + " 个" : "");
        }

        static bool IsLarkSendOk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            if (text.IndexOf("\"ok\":false", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            var code = MatchJsonString(text, "code");
            if (code != null && code != "0")
                return false;
            var codeMatch = Regex.Match(text, "\"code\"\\s*:\\s*(-?\\d+)");
            if (codeMatch.Success && codeMatch.Groups[1].Value != "0")
                return false;
            return true;
        }

        string EnsureLarkIdentity(LarkRunner cli, bool useStored)
        {
            EnsureLarkConfigured(cli);
            if (BotIdentityReady(cli))
                return "bot";
            TryStoreTokenFromSettings(cli);
            if (BotIdentityReady(cli))
                return "bot";
            throw new InvalidOperationException("机器人未就绪。发给自己走机器人身份，请在设置填写 App ID 和 Token 并保存后再试。");
        }

        void EnsureLarkConfigured(LarkRunner cli)
        {
            var status = RunLarkCli(cli, "auth status --json", null, false, 60000);
            var text = ((status.Stdout ?? "") + "\n" + (status.Stderr ?? "")).Trim();
            if (!IsNotConfigured(text))
                return;
            StatusOnUi("飞书 CLI 未配置，正在打开浏览器完成应用初始化…", Theme.Orange);
            RunConfigInitNew(cli);
            status = RunLarkCli(cli, "auth status --json", null, false, 60000);
            text = ((status.Stdout ?? "") + "\n" + (status.Stderr ?? "")).Trim();
            if (IsNotConfigured(text))
                throw new InvalidOperationException("飞书 CLI 仍未配置完成。请在浏览器里完成应用创建后重试。");
            TryStoreTokenFromSettings(cli);
        }

        void TryStoreTokenFromSettings(LarkRunner cli)
        {
            var appId = settings.FeishuAppId == null ? "" : settings.FeishuAppId.Trim();
            var token = settings.FeishuToken == null ? "" : settings.FeishuToken.Trim();
            if (appId.Length == 0 || token.Length == 0)
                return;
            try { StoreToken(appId, token); }
            catch { }
        }

        static bool IsNotConfigured(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            if (text.IndexOf("not_configured", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (text.IndexOf("not configured", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (text.IndexOf("\"ok\":false", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0
                && IdentityReady(text, "bot") == false
                && IdentityReady(text, "user") == false)
                return true;
            return false;
        }

        static string PickReadyIdentity(LarkRunner cli)
        {
            var result = RunLarkCli(cli, "auth status --json", null, false, 60000);
            var json = ((result.Stdout ?? "") + "\n" + (result.Stderr ?? "")).Trim();
            if (IsNotConfigured(json))
                return null;
            if (IdentityReady(json, "bot"))
                return "bot";
            if (IdentityReady(json, "user"))
                return "user";
            return null;
        }

        static bool BotIdentityReady(LarkRunner cli)
        {
            var result = RunLarkCli(cli, "auth status --json", null, false, 60000);
            var json = ((result.Stdout ?? "") + "\n" + (result.Stderr ?? "")).Trim();
            return IdentityReady(json, "bot");
        }

        static bool UserIdentityReady(LarkRunner cli)
        {
            var result = RunLarkCli(cli, "auth status --json", null, false, 60000);
            var json = ((result.Stdout ?? "") + "\n" + (result.Stderr ?? "")).Trim();
            return IdentityReady(json, "user");
        }

        static bool IdentityReady(string json, string which)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(which))
                return false;
            var key = "\"" + which + "\"";
            var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;
            var len = Math.Min(500, json.Length - idx);
            var slice = json.Substring(idx, len);
            return slice.IndexOf("\"status\":\"ready\"", StringComparison.OrdinalIgnoreCase) >= 0
                || slice.IndexOf("\"status\": \"ready\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void RunConfigInitNew(LarkRunner cli)
        {
            var psi = CreateLarkStartInfo(cli, "config init --new --brand feishu --force-init", null, false);
            var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("无法启动 lark-cli config init");
            var opened = new bool[1];
            var gate = new object();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                    return;
                lock (gate) { stdout.AppendLine(e.Data); }
                TryOpenAuthUrl(e.Data, opened);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                    return;
                lock (gate) { stderr.AppendLine(e.Data); }
                TryOpenAuthUrl(e.Data, opened);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(600000))
            {
                try { process.Kill(); } catch { }
                throw new InvalidOperationException("飞书应用初始化超时。请重试「登录飞书 CLI」。");
            }
            var text = stdout.ToString() + "\n" + stderr.ToString();
            var after = RunLarkCli(cli, "auth status --json", null, false, 60000);
            var afterText = ((after.Stdout ?? "") + "\n" + (after.Stderr ?? "")).Trim();
            if (process.ExitCode != 0 && IsNotConfigured(afterText))
                throw new InvalidOperationException("飞书应用初始化失败：" + FormatLarkError(text, "exit " + process.ExitCode.ToString(CultureInfo.InvariantCulture)));
        }

        void TryOpenAuthUrl(string line, bool[] opened)
        {
            if (opened == null || opened.Length == 0 || opened[0] || string.IsNullOrEmpty(line))
                return;
            var uri = ExtractVerificationUrl(line);
            if (uri == null)
                return;
            opened[0] = true;
            try { OpenBrowser(uri); }
            catch { }
            StatusOnUi("已打开浏览器，请完成飞书应用/授权…", Theme.Orange);
        }

        void RunUserLogin(LarkRunner cli)
        {
            var start = RunLarkCli(cli, "auth login --scope \"im:message offline_access\" --no-wait --json", null, false, 60000);
            var text = ((start.Stdout ?? "") + "\n" + (start.Stderr ?? "")).Trim();
            if (UserIdentityReady(cli))
                return;
            var uri = ExtractVerificationUrl(text);
            var deviceCode = MatchJsonString(text, "device_code");
            if (uri == null || deviceCode == null)
                throw new InvalidOperationException("无法启动飞书登录：" + FormatLarkError(text, "exit " + start.ExitCode.ToString(CultureInfo.InvariantCulture)));
            OpenBrowser(uri);
            StatusOnUi("已打开浏览器，请在飞书完成授权…", Theme.Orange);
            var done = RunLarkCli(cli, "auth login --device-code \"" + deviceCode.Replace("\"", "") + "\" --json", null, false, 600000);
            if (done.ExitCode != 0 && !UserIdentityReady(cli))
            {
                var err = ((done.Stdout ?? "") + "\n" + (done.Stderr ?? "")).Trim();
                throw new InvalidOperationException("飞书授权未完成：" + FormatLarkError(err, "授权超时或被取消"));
            }
        }

        static string ExtractVerificationUrl(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var uri = MatchJsonString(text, "verification_uri_complete");
            if (uri == null)
                uri = MatchJsonString(text, "verification_url");
            if (uri == null)
                uri = MatchJsonString(text, "verification_uri");
            if (uri == null)
            {
                var m = Regex.Match(text, @"https://[^\s\""'\\]+");
                if (m.Success)
                    uri = m.Value.TrimEnd('.', ',', ';', ')');
            }
            return uri;
        }

        void StatusOnUi(string text, SolidColorBrush brush)
        {
            Dispatcher.BeginInvoke(new Action(delegate { SetStatus(text, brush); }));
        }

        static string FormatLarkError(string text, string fallback)
        {
            if (string.IsNullOrEmpty(fallback))
                fallback = "未知错误";
            if (string.IsNullOrEmpty(text))
                return fallback;
            var msg = MatchJsonString(text, "message");
            var hint = MatchJsonString(text, "hint");
            if (!string.IsNullOrEmpty(msg))
            {
                if (!string.IsNullOrEmpty(hint) && hint.IndexOf(msg, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var shortHint = hint.Length > 80 ? hint.Substring(0, 80) + "…" : hint;
                    return msg + "。" + shortHint;
                }
                return msg;
            }
            var subtype = MatchJsonString(text, "subtype");
            if (!string.IsNullOrEmpty(subtype))
                return subtype + (string.IsNullOrEmpty(hint) ? "" : "。" + (hint.Length > 80 ? hint.Substring(0, 80) + "…" : hint));
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
                return fallback;
            if (trimmed.Length > 180)
                return trimmed.Substring(0, 180) + "…";
            return trimmed;
        }

        static string MatchJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return null;
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                return null;
            return m.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        static void OpenBrowser(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            var psi = new ProcessStartInfo();
            psi.FileName = url;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        void LoginFeishuCliAsync()
        {
            if (busy)
                return;
            var cli = FindLarkRunner();
            if (cli == null)
            {
                SetStatus("没有找到 lark-cli。请先安装并加入 PATH。", Theme.Red);
                if (settingsHint != null)
                    settingsHint.Text = "未找到 lark-cli";
                return;
            }
            busy = true;
            UpdateButtons();
            SetStatus("正在检查飞书登录…", Theme.Label);
            Task.Run(delegate
            {
                string message;
                var ok = false;
                try
                {
                    EnsureLarkConfigured(cli);
                    if (UserIdentityReady(cli))
                    {
                        message = "飞书用户已登录";
                        ok = true;
                    }
                    else
                    {
                        StatusOnUi("需要登录飞书用户，正在打开浏览器…", Theme.Label);
                        RunUserLogin(cli);
                        if (!UserIdentityReady(cli))
                            throw new InvalidOperationException("登录后用户身份仍未就绪，请重试。");
                        message = "飞书用户登录成功";
                        ok = true;
                    }
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    SetStatus(message, ok ? Theme.PinkSoft : Theme.Red);
                    if (settingsHint != null)
                        settingsHint.Text = message;
                    UpdateButtons();
                }));
            });
        }

        static AdbResult RunLark(LarkRunner cli, string localFile, string flag, string chatId, string asWho, bool useStored)
        {
            if (string.IsNullOrEmpty(asWho))
                asWho = "bot";
            var args = new StringBuilder();
            args.Append("im +messages-send --as ").Append(asWho.Replace("\"", "")).Append(" --chat-id \"");
            args.Append(chatId.Replace("\"", "")).Append("\" ");
            args.Append(flag).Append(" \"").Append(Path.GetFileName(localFile).Replace("\"", "")).Append("\"");
            return RunLarkCli(cli, args.ToString(), Path.GetDirectoryName(localFile), useStored && asWho == "bot", 180000);
        }

        static AdbResult RunLarkVideo(LarkRunner cli, string localVideo, string localCover, string chatId, string asWho, bool useStored)
        {
            if (string.IsNullOrEmpty(asWho))
                asWho = "bot";
            var args = new StringBuilder();
            args.Append("im +messages-send --as ").Append(asWho.Replace("\"", "")).Append(" --chat-id \"");
            args.Append(chatId.Replace("\"", "")).Append("\" ");
            args.Append("--video \"").Append(Path.GetFileName(localVideo).Replace("\"", "")).Append("\" ");
            args.Append("--video-cover \"").Append(Path.GetFileName(localCover).Replace("\"", "")).Append("\"");
            return RunLarkCli(cli, args.ToString(), Path.GetDirectoryName(localVideo), useStored && asWho == "bot", 600000);
        }

        bool EnsureVideoCover(string localVideo, string coverPath)
        {
            if (string.IsNullOrEmpty(localVideo) || string.IsNullOrEmpty(coverPath))
                return false;
            if (ExtractVideoFrame(localVideo, coverPath) && File.Exists(coverPath))
                return true;
            try
            {
                var shot = ShellThumb(localVideo, 360, 200);
                if (shot == null)
                    return false;
                SaveJpeg(shot, coverPath);
                return File.Exists(coverPath);
            }
            catch
            {
                return false;
            }
        }

        static ProcessStartInfo CreateLarkStartInfo(LarkRunner cli, string commandArgs, string workingDirectory, bool useStored)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = cli.FileName;
            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["LARKSUITE_CLI_NO_UPDATE_NOTIFIER"] = "1";
            psi.EnvironmentVariables["LARKSUITE_CLI_NO_SKILLS_NOTIFIER"] = "1";
            try { psi.EnvironmentVariables.Remove("HERMES_HOME"); } catch { }
            try { psi.EnvironmentVariables.Remove("OPENCLAW_HOME"); } catch { }
            try { psi.EnvironmentVariables.Remove("LARK_CHANNEL"); } catch { }
            if (useStored)
                psi.EnvironmentVariables["LARKSUITE_CLI_TENANT_ACCESS_TOKEN_SOURCE"] = "credential-store";
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(cli.Script))
                args.Append("\"").Append(cli.Script).Append("\" ");
            args.Append(commandArgs);
            psi.Arguments = args.ToString();
            return psi;
        }

        static AdbResult RunLarkCli(LarkRunner cli, string commandArgs, string workingDirectory, bool useStored, int timeoutMs)
        {
            var psi = CreateLarkStartInfo(cli, commandArgs, workingDirectory, useStored);
            var process = Process.Start(psi);
            string stdout = "";
            string stderr = "";
            var outDone = new ManualResetEvent(false);
            var errDone = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { stdout = process.StandardOutput.ReadToEnd(); }
                catch { }
                outDone.Set();
            });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { stderr = process.StandardError.ReadToEnd(); }
                catch { }
                errDone.Set();
            });
            bool exited;
            if (timeoutMs > 0)
                exited = process.WaitForExit(timeoutMs);
            else
            {
                process.WaitForExit();
                exited = true;
            }
            if (!exited)
            {
                try { process.Kill(); } catch { }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                return new AdbResult { ExitCode = -1, Stdout = stdout, Stderr = (stderr ?? "") + "\ntimeout" };
            }
            outDone.WaitOne(60000);
            errDone.WaitOne(60000);
            return new AdbResult { ExitCode = process.ExitCode, Stdout = stdout, Stderr = stderr };
        }

        static LarkRunner FindLarkRunner()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(';'))
            {
                var dir = part.Trim();
                if (dir.Length == 0)
                    continue;
                var exe = Path.Combine(dir, "lark-cli.exe");
                if (File.Exists(exe))
                    return new LarkRunner { FileName = exe };
                var cmd = Path.Combine(dir, "lark-cli.cmd");
                if (File.Exists(cmd))
                    return new LarkRunner { FileName = cmd };
            }
            var node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes", "node", "node.exe");
            var script = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes", "node", "node_modules", "@larksuite", "cli", "scripts", "run.js");
            if (File.Exists(node) && File.Exists(script))
                return new LarkRunner { FileName = node, Script = script };
            var nodePath = FindOnPath("node.exe");
            if (nodePath != null && File.Exists(script))
                return new LarkRunner { FileName = nodePath, Script = script };
            return null;
        }

        static string FindOnPath(string exe)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(';'))
            {
                try
                {
                    var candidate = Path.Combine(part.Trim(), exe);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        void BindSettingsFields()
        {
            applyingTheme = true;
            setLogsBox.Text = settings.LogsDir;
            setAppIdBox.Text = settings.FeishuAppId;
            setTokenBox.Password = "";
            setChatBox.Text = settings.FeishuChatId;
            if (chkFollowSystem != null)
                chkFollowSystem.IsChecked = settings.FollowSystem;
            if (chkThemeDark != null)
            {
                chkThemeDark.IsChecked = settings.ResolvedDark();
                chkThemeDark.IsEnabled = !settings.FollowSystem;
            }
            if (chkGlass != null)
                chkGlass.IsChecked = settings.EnableGlass;
            if (cmbAccent != null)
                cmbAccent.SelectedIndex = settings.Accent == "Blue" ? 0 : settings.Accent == "Green" ? 2 : 1;
            applyingTheme = false;
            ShowSettingsSection("look");
        }

        Grid BuildSettingsPage()
        {
            var page = new Grid { Margin = new Thickness(16) };
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            settingsNavCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6)
            };
            Theme.BindElement(settingsNavCard, BackgroundProperty, "nn.Card");
            Theme.BindElement(settingsNavCard, Border.BorderBrushProperty, "nn.Hairline");
            ClipRound(settingsNavCard, 14);
            var navStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            btnSetLook = SettingsNav("外观", true);
            btnSetPath = SettingsNav("目录", false);
            btnSetFeishu = SettingsNav("飞书", false);
            btnSetLook.Click += delegate { ShowSettingsSection("look"); };
            btnSetPath.Click += delegate { ShowSettingsSection("path"); };
            btnSetFeishu.Click += delegate { ShowSettingsSection("feishu"); };
            navStack.Children.Add(btnSetLook);
            navStack.Children.Add(btnSetPath);
            navStack.Children.Add(btnSetFeishu);
            settingsNavCard.Child = navStack;
            var nav = settingsNavCard;
            nav.SizeChanged += delegate
            {
                var width = nav.ActualWidth - 16;
                if (width < 80)
                    return;
                btnSetLook.Width = width;
                btnSetPath.Width = width;
                btnSetFeishu.Width = width;
            };
            page.Children.Add(nav);

            settingsContentCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14)
            };
            Theme.BindElement(settingsContentCard, BackgroundProperty, "nn.Card");
            Theme.BindElement(settingsContentCard, Border.BorderBrushProperty, "nn.Hairline");
            ClipRound(settingsContentCard, 14);
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(22) };
            var host = new Grid();
            panelSetLook = BuildLookSettings();
            panelSetPath = BuildPathSettings();
            panelSetFeishu = BuildFeishuSettings();
            host.Children.Add(panelSetLook);
            host.Children.Add(panelSetPath);
            host.Children.Add(panelSetFeishu);
            scroller.Content = host;
            settingsContentCard.Child = scroller;
            Grid.SetColumn(settingsContentCard, 2);
            page.Children.Add(settingsContentCard);
            return page;
        }

        Button SettingsNav(string text, bool active)
        {
            var btn = new Button
            {
                Content = text,
                Margin = new Thickness(2),
                Padding = new Thickness(10, 10, 10, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 13,
                Cursor = Cursors.Hand
            };
            PaintNav(btn, active);
            return btn;
        }

        void ShowSettingsSection(string section)
        {
            if (panelSetLook != null)
                panelSetLook.Visibility = section == "look" ? Visibility.Visible : Visibility.Collapsed;
            if (panelSetPath != null)
                panelSetPath.Visibility = section == "path" ? Visibility.Visible : Visibility.Collapsed;
            if (panelSetFeishu != null)
                panelSetFeishu.Visibility = section == "feishu" ? Visibility.Visible : Visibility.Collapsed;
            PaintNav(btnSetLook, section == "look");
            PaintNav(btnSetPath, section == "path");
            PaintNav(btnSetFeishu, section == "feishu");
        }

        StackPanel BuildLookSettings()
        {
            var stack = new StackPanel();
            stack.Children.Add(SettingTitle("主题色"));
            stack.Children.Add(SettingDesc("选择界面强调色：蓝 / 粉 / 绿。"));
            cmbAccent = new ComboBox { Width = 220, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 22) };
            cmbAccent.Items.Add("蓝");
            cmbAccent.Items.Add("粉");
            cmbAccent.Items.Add("绿");
            cmbAccent.SelectedIndex = 0;
            StyleCombo(cmbAccent);
            cmbAccent.SelectionChanged += delegate
            {
                if (applyingTheme || cmbAccent.SelectedIndex < 0)
                    return;
                settings.Accent = cmbAccent.SelectedIndex == 0 ? "Blue" : cmbAccent.SelectedIndex == 2 ? "Green" : "Pink";
                ApplyAppearance();
            };
            stack.Children.Add(cmbAccent);
            stack.Children.Add(SettingTitle("跟随系统主题"));
            stack.Children.Add(SettingDesc("开启后按 Windows 浅色/深色自动切换。"));
            chkFollowSystem = IosSwitch();
            chkFollowSystem.Margin = new Thickness(0, 0, 0, 22);
            chkFollowSystem.Checked += delegate { OnThemeToggled(); };
            chkFollowSystem.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(chkFollowSystem);
            stack.Children.Add(SettingTitle("暗色毛玻璃"));
            stack.Children.Add(SettingDesc("开启后用暗色半透明色板 + 系统 Acrylic。关着「毛玻璃」时只是实色深色壳。跟随系统开启时不可用。"));
            chkThemeDark = IosSwitch();
            chkThemeDark.Margin = new Thickness(0, 0, 0, 22);
            chkThemeDark.Checked += delegate { OnThemeToggled(); };
            chkThemeDark.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(chkThemeDark);
            stack.Children.Add(SettingTitle("毛玻璃"));
            stack.Children.Add(SettingDesc("开启后窗口/侧栏透出系统 Acrylic。建议与浅色或暗色主题一起开。失焦时系统可能收成实色。"));
            chkGlass = IosSwitch();
            chkGlass.Checked += delegate { OnThemeToggled(); };
            chkGlass.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(chkGlass);
            return stack;
        }

        StackPanel BuildPathSettings()
        {
            var stack = new StackPanel { Visibility = Visibility.Collapsed };
            stack.Children.Add(SettingTitle("日志目录"));
            stack.Children.Add(SettingDesc("提取日志的默认保存位置。可以随时改。"));
            setLogsBox = FieldBox(settings.LogsDir);
            setLogsBox.Width = 420;
            setLogsBox.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(setLogsBox);
            var save = MakePrimary("保存目录");
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Click += delegate { SaveSettings(); };
            stack.Children.Add(save);
            return stack;
        }

        StackPanel BuildFeishuSettings()
        {
            var stack = new StackPanel { Visibility = Visibility.Collapsed };
            stack.Children.Add(SettingTitle("App ID"));
            stack.Children.Add(SettingDesc("写入飞书 CLI 凭证时用。留空则只记在本地设置里。"));
            setAppIdBox = FieldBox(settings.FeishuAppId);
            setAppIdBox.Width = 420;
            setAppIdBox.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(setAppIdBox);
            stack.Children.Add(SettingTitle("CLI Token"));
            stack.Children.Add(SettingDesc("留空不会覆盖已保存的。写入本机飞书 CLI 的安全存储，不会出现在窗口里。"));
            setTokenBox = new PasswordBox
            {
                Height = 32,
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 18),
                Padding = new Thickness(10, 0, 10, 0),
                Background = Theme.Field,
                Foreground = Theme.Ink,
                BorderBrush = Theme.Hairline,
                CaretBrush = Theme.Pink
            };
            stack.Children.Add(setTokenBox);
            stack.Children.Add(SettingTitle("发给自己的会话"));
            stack.Children.Add(SettingDesc("飞书会话 ID。截图和录屏会发到这里。"));
            setChatBox = FieldBox(settings.FeishuChatId);
            setChatBox.Width = 420;
            setChatBox.HorizontalAlignment = HorizontalAlignment.Left;
            setChatBox.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(setChatBox);
            stack.Children.Add(SettingTitle("怎么拿到自己的会话 ID"));
            stack.Children.Add(SettingNote("不会自动填。按下面任一种查自己的，贴到上面。不要用别人的会话。"));
            stack.Children.Add(SettingNote("1. 飞书桌面端打开要发到的会话。"));
            stack.Children.Add(SettingNote("2. 点右上角更多，复制链接。"));
            stack.Children.Add(SettingNote("3. 取链接里 openChatId= 后面整段。它以 oc_ 开头。"));
            stack.Children.Add(SettingNote("本机已登录 lark-cli 时，也可以搜会话名："));
            stack.Children.Add(SettingNote("lark-cli im +chat-search --query \"会话名\" --as bot"));
            stack.Children.Add(SettingNote("看结果里的 chat_id。优先用机器人发；未配置机器人时会用已登录的用户身份发。"));
            stack.Children.Add(SettingNote("本机未登录时，点「登录飞书 CLI」会打开浏览器；只申请基础消息权限，不会申请「以用户身份发送消息」。"));
            stack.Children.Add(SettingNote("「发给自己」走机器人。请填 App ID / Token，并确保会话里有该机器人。"));
            var login = MakePrimary("登录飞书 CLI");
            login.HorizontalAlignment = HorizontalAlignment.Left;
            login.Margin = new Thickness(0, 4, 0, 8);
            login.Click += delegate { LoginFeishuCliAsync(); };
            stack.Children.Add(login);
            var save = MakePrimary("保存飞书");
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0, 0, 0, 8);
            save.Click += delegate { SaveSettings(); };
            stack.Children.Add(save);
            settingsHint = new TextBlock { Foreground = Theme.Label, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(settingsHint);
            return stack;
        }

        void OnThemeToggled()
        {
            if (applyingTheme)
                return;
            settings.FollowSystem = chkFollowSystem != null && chkFollowSystem.IsChecked == true;
            settings.EnableGlass = chkGlass == null || chkGlass.IsChecked == true;
            if (!settings.FollowSystem && chkThemeDark != null)
                settings.ThemeMode = chkThemeDark.IsChecked == true ? "Dark" : "Light";
            applyingTheme = true;
            if (chkThemeDark != null)
            {
                chkThemeDark.IsEnabled = !settings.FollowSystem;
                chkThemeDark.IsChecked = settings.ResolvedDark();
            }
            applyingTheme = false;
            ApplyAppearance();
        }

        void ApplyAppearance()
        {
            Theme.Apply(settings.ResolvedDark(), settings.Accent);
            Theme.BindElement(this, ForegroundProperty, "nn.Ink");
            if (!settings.EnableGlass)
                Theme.BindElement(shellBody, BackgroundProperty, "nn.Bg");
            Theme.BindElement(shellSidebar, BackgroundProperty, "nn.Sidebar");
            Theme.BindElement(shellSidebar, Border.BorderBrushProperty, "nn.Hairline");
            Theme.BindElement(settingsNavCard, BackgroundProperty, "nn.Card");
            Theme.BindElement(settingsNavCard, Border.BorderBrushProperty, "nn.Hairline");
            Theme.BindElement(settingsContentCard, BackgroundProperty, "nn.Card");
            Theme.BindElement(settingsContentCard, Border.BorderBrushProperty, "nn.Hairline");
            ApplyWindowChrome();
            ApplyChromeLook();
            PaintNav(tabLogs, currentPage == "logs");
            PaintNav(tabMedia, currentPage == "media");
            PaintNav(tabApk, currentPage == "apk");
            PaintNav(navSettings, currentPage == "settings");
            if (btnSetLook != null)
            {
                PaintNav(btnSetLook, panelSetLook != null && panelSetLook.Visibility == Visibility.Visible);
                PaintNav(btnSetPath, panelSetPath != null && panelSetPath.Visibility == Visibility.Visible);
                PaintNav(btnSetFeishu, panelSetFeishu != null && panelSetFeishu.Visibility == Visibility.Visible);
            }
            if (copyButton != null)
            {
                copyButton.SetResourceReference(Control.ForegroundProperty, "nn.OnAccent");
                copyButton.Template = FlatButtonTemplate(Theme.Pink, Theme.PinkPressed, 6);
            }
            if (sendButton != null)
            {
                sendButton.SetResourceReference(Control.ForegroundProperty, "nn.OnAccent");
                sendButton.Template = FlatButtonTemplate(Theme.Pink, Theme.PinkPressed, 6);
            }
            if (apkProgressFill != null)
                Theme.BindElement(apkProgressFill, BackgroundProperty, "nn.Pink");
            if (apkProgressTrack != null)
            {
                Theme.BindElement(apkProgressTrack, BackgroundProperty, "nn.Chip");
                Theme.BindElement(apkProgressTrack, Border.BorderBrushProperty, "nn.Hairline");
            }
            if (apkProgressText != null)
                BindInk(apkProgressText);
            if (refreshText != null)
                refreshText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Pink");
            if (statusText != null)
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            RestyleSwitches();
            if (cmbAccent != null)
                StyleCombo(cmbAccent);
            if (pageLogs != null)
                pageLogs.Background = Brushes.Transparent;
            if (pageMedia != null)
                pageMedia.Background = Brushes.Transparent;
            if (pageSettings != null)
                pageSettings.Background = Brushes.Transparent;
            if (pageAbout != null)
                Theme.BindElement(pageAbout, BackgroundProperty, "nn.Bg");
            if (deviceBar != null)
            {
                var bar = deviceBar as Border;
                if (bar != null)
                {
                    Theme.BindElement(bar, BackgroundProperty, "nn.Card");
                    Theme.BindElement(bar, Border.BorderBrushProperty, "nn.Hairline");
                }
            }
            if (actionFooter != null)
            {
                var foot = actionFooter as Border;
                if (foot != null)
                {
                    Theme.BindElement(foot, BackgroundProperty, "nn.Card");
                    Theme.BindElement(foot, Border.BorderBrushProperty, "nn.Hairline");
                }
            }
            if (deviceCard != null)
            {
                Theme.BindElement(deviceCard, BackgroundProperty, "nn.Field");
                Theme.BindElement(deviceCard, Border.BorderBrushProperty, "nn.Hairline");
            }
            settings.Save();
        }

        void RestyleSwitches()
        {
            RestyleSwitch(chkFollowSystem);
            RestyleSwitch(chkThemeDark);
            RestyleSwitch(chkGlass);
            RestyleSwitch(chkApkUninstall);
            RestyleSwitch(chkApkInstallAll);
        }

        void RestyleSwitch(CheckBox box)
        {
            if (box == null)
                return;
            var on = box.IsChecked == true;
            applyingTheme = true;
            box.Template = IosSwitchTemplate();
            box.IsChecked = on;
            applyingTheme = false;
        }

        Grid BuildAboutPage()
        {
            var page = new Grid();
            Theme.BindElement(page, BackgroundProperty, "nn.Bg");
            var mid = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 640,
                Margin = new Thickness(40, 20, 40, 72)
            };
            var word = new TextBlock
            {
                Text = "NANALLY",
                FontFamily = new FontFamily("Georgia, Palatino Linotype, Times New Roman"),
                FontSize = 96,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            word.SetResourceReference(TextBlock.ForegroundProperty, "nn.Ink");
            mid.Children.Add(word);
            var tagline = new TextBlock
            {
                Text = "把手机里的日志、截图和录屏，轻轻带回来。",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(24, 28, 24, 0)
            };
            tagline.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            mid.Children.Add(tagline);
            var masks = new StackPanel { Margin = new Thickness(24, 22, 24, 0), HorizontalAlignment = HorizontalAlignment.Center };
            masks.Children.Add(RevealLine("连上设备，就能把 Logs 拷到你指定的目录。"));
            masks.Children.Add(RevealLine("截图和录屏可以预览，勾选之后发给自己。"));
            mid.Children.Add(masks);
            btnAboutUpdate = MakePrimary("从 GitHub 更新");
            btnAboutUpdate.Margin = new Thickness(0, 28, 0, 0);
            btnAboutUpdate.HorizontalAlignment = HorizontalAlignment.Center;
            btnAboutUpdate.Visibility = Visibility.Collapsed;
            btnAboutUpdate.Click += delegate { StartAboutUpdate(); };
            mid.Children.Add(btnAboutUpdate);
            page.Children.Add(mid);
            var foot = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 28)
            };
            txtAboutVersion = new TextBlock
            {
                Text = AppInfo.DisplayVersion,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            txtAboutVersion.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            foot.Children.Add(txtAboutVersion);
            page.Children.Add(foot);
            return page;
        }

        void BeginAboutUpdateCheck()
        {
            if (btnAboutUpdate != null)
                btnAboutUpdate.Visibility = Visibility.Collapsed;
            pendingUpdate = null;
            if (txtAboutVersion != null)
                txtAboutVersion.Text = AppInfo.DisplayVersion;
            var gen = ++aboutUpdateCheckGen;
            var repo = AppInfo.DefaultUpdateRepo;
            Task.Factory.StartNew(delegate
            {
                string error;
                var info = GitHubUpdater.CheckLatest(repo, out error);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen != aboutUpdateCheckGen || currentPage != "about")
                        return;
                    if (info == null)
                    {
                        WhatHappened.Info("关于页检查更新失败：" + (error ?? "unknown"));
                        return;
                    }
                    if (!GitHubUpdater.IsNewer(info.Version, AppInfo.Version))
                    {
                        WhatHappened.Info("关于页检查更新：已是最新 local=" + AppInfo.Version + " remote=" + info.Version);
                        return;
                    }
                    pendingUpdate = info;
                    if (btnAboutUpdate != null)
                    {
                        btnAboutUpdate.Content = "更新到 " + (string.IsNullOrEmpty(info.Tag) ? info.Version : info.Tag);
                        btnAboutUpdate.Visibility = Visibility.Visible;
                    }
                    if (txtAboutVersion != null)
                        txtAboutVersion.Text = AppInfo.DisplayVersion + " · 可更新到 " + (string.IsNullOrEmpty(info.Tag) ? info.Version : info.Tag);
                    WhatHappened.Info("关于页发现新版本 local=" + AppInfo.Version + " remote=" + info.Version);
                }));
            });
        }

        void StartAboutUpdate()
        {
            var info = pendingUpdate;
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                return;
            if (busy)
            {
                SetStatus("正在忙别的事，稍后再更新哦", Theme.Orange);
                return;
            }
            var confirm = MessageBox.Show(
                this,
                "将从 GitHub 下载 " + (string.IsNullOrEmpty(info.Tag) ? info.Version : info.Tag) + " 并替换本机 Nanally.exe。\n下载完成后会自动重启。继续吗？",
                "更新 Nanally",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;
            busy = true;
            if (btnAboutUpdate != null)
                btnAboutUpdate.IsEnabled = false;
            SetStatus("正在从 GitHub 下载更新…", Theme.Label);
            WhatHappened.Info("开始自更新 url=" + info.DownloadUrl);
            var root = toolRoot;
            var url = info.DownloadUrl;
            Task.Factory.StartNew(delegate
            {
                string error;
                GitHubUpdater.DownloadAndApply(url, root, out error);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    if (btnAboutUpdate != null)
                        btnAboutUpdate.IsEnabled = true;
                    if (!string.IsNullOrEmpty(error))
                    {
                        SetStatus(error, Theme.Orange);
                        WhatHappened.Info("自更新失败：" + error);
                        MessageBox.Show(this, error, "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    SetStatus("更新已就绪，正在重启…", Theme.PinkSoft);
                    WhatHappened.Info("自更新脚本已启动，准备退出");
                    Close();
                }));
            });
        }

        Grid RevealLine(string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
            grid.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = Theme.Ink,
                TextAlignment = TextAlignment.Center
            });
            var mask = new Border { Background = Theme.Bg, CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand };
            mask.MouseEnter += delegate { mask.Opacity = 0; };
            mask.MouseLeave += delegate { mask.Opacity = 1; };
            grid.Children.Add(mask);
            return grid;
        }

        static void BindInk(TextBlock block)
        {
            if (block != null)
                block.SetResourceReference(TextBlock.ForegroundProperty, "nn.Ink");
        }

        static void BindLabel(TextBlock block)
        {
            if (block != null)
                block.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
        }

        static TextBlock SettingTitle(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            BindInk(block);
            return block;
        }

        static TextBlock SettingDesc(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            BindLabel(block);
            return block;
        }

        static TextBlock SettingNote(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 6) };
            BindLabel(block);
            return block;
        }

        CheckBox IosSwitch()
        {
            var box = new CheckBox
            {
                Cursor = Cursors.Hand,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            box.Template = IosSwitchTemplate();
            return box;
        }

        static ControlTemplate IosSwitchTemplate()
        {
            var template = new ControlTemplate(typeof(CheckBox));
            var track = new FrameworkElementFactory(typeof(Border));
            track.Name = "Track";
            track.SetValue(Border.WidthProperty, 44.0);
            track.SetValue(Border.HeightProperty, 26.0);
            track.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
            track.SetValue(Border.BackgroundProperty, B("#475569"));
            var knob = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            knob.Name = "Knob";
            knob.SetValue(FrameworkElement.WidthProperty, 22.0);
            knob.SetValue(FrameworkElement.HeightProperty, 22.0);
            knob.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
            knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            knob.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
            track.AppendChild(knob);
            template.VisualTree = track;
            var on = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
            on.Setters.Add(Theme.Dyn(Border.BackgroundProperty, Theme.Pink, "Track"));
            on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
            on.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0), "Knob"));
            template.Triggers.Add(on);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(off);
            return template;
        }

        static void StyleField(TextBox box)
        {
            box.Template = FieldTemplate();
            box.SetResourceReference(Control.BackgroundProperty, "nn.Field");
            box.SetResourceReference(Control.BorderBrushProperty, "nn.Hairline");
            box.SetResourceReference(Control.ForegroundProperty, "nn.Ink");
            box.SetResourceReference(TextBox.CaretBrushProperty, "nn.Pink");
        }

        static ControlTemplate FieldTemplate()
        {
            var template = new ControlTemplate(typeof(TextBox));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TextBox.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TextBox.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 8, 0));
            host.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(host);
            template.VisualTree = border;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(Theme.Dyn(Border.BorderBrushProperty, Theme.Pink, "Bd"));
            template.Triggers.Add(over);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(Theme.Dyn(Border.BorderBrushProperty, Theme.Pink, "Bd"));
            template.Triggers.Add(focus);
            return template;
        }

        static void StyleCombo(ComboBox box)
        {
            box.SetResourceReference(Control.ForegroundProperty, "nn.Ink");
            box.SetResourceReference(Control.BackgroundProperty, "nn.Field");
            box.SetResourceReference(Control.BorderBrushProperty, "nn.Hairline");
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(10, 6, 10, 6);
            box.FontSize = 13;
            box.SnapsToDevicePixels = true;
            box.ItemContainerStyle = ComboItemStyle();
            box.Template = ComboTemplate();
        }

        static Style ComboItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ref(Theme.Ink)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2)));
            var template = new ControlTemplate(typeof(ComboBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.Name = "Check";
            check.SetValue(TextBlock.TextProperty, "\uE73E");
            check.SetValue(TextBlock.FontFamilyProperty, Theme.IconFont);
            check.SetValue(TextBlock.FontSizeProperty, 12.0);
            Theme.Bind(check, TextBlock.ForegroundProperty, Theme.Ink);
            check.SetValue(DockPanel.DockProperty, Dock.Right);
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            dock.AppendChild(check);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dock.AppendChild(presenter);
            border.AppendChild(dock);
            template.VisualTree = border;
            var hi = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hi.Setters.Add(Theme.Dyn(Border.BackgroundProperty, Theme.NavHover, "Bd"));
            template.Triggers.Add(hi);
            var sel = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(Theme.Dyn(Border.BackgroundProperty, Theme.CardSoft, "Bd"));
            sel.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Check"));
            template.Triggers.Add(sel);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        static ControlTemplate ComboTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            Theme.Bind(bd, Border.BackgroundProperty, Theme.Field);
            Theme.Bind(bd, Border.BorderBrushProperty, Theme.Hairline);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var chev = new FrameworkElementFactory(typeof(TextBlock));
            chev.SetValue(TextBlock.TextProperty, "\uE70D");
            chev.SetValue(TextBlock.FontFamilyProperty, Theme.IconFont);
            chev.SetValue(TextBlock.FontSizeProperty, 10.0);
            Theme.Bind(chev, TextBlock.ForegroundProperty, Theme.Label);
            chev.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            chev.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chev.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            chev.SetValue(UIElement.IsHitTestVisibleProperty, false);
            bd.AppendChild(chev);
            toggleTemplate.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(Theme.Dyn(Border.BorderBrushProperty, Theme.Pink, "Bd"));
            toggleTemplate.Triggers.Add(over);
            var open = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            open.Setters.Add(Theme.Dyn(Border.BorderBrushProperty, Theme.Pink, "Bd"));
            toggleTemplate.Triggers.Add(open);
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);
            root.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.Name = "ContentSite";
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 30, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            root.AppendChild(content);

            var editor = new FrameworkElementFactory(typeof(TextBox));
            editor.Name = "PART_EditableTextBox";
            editor.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            editor.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            editor.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            Theme.Bind(editor, Control.ForegroundProperty, Theme.Ink);
            Theme.Bind(editor, TextBox.CaretBrushProperty, Theme.Pink);
            editor.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 30, 0));
            editor.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(editor);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var drop = new FrameworkElementFactory(typeof(Border));
            Theme.Bind(drop, Border.BackgroundProperty, Theme.Card);
            Theme.Bind(drop, Border.BorderBrushProperty, Theme.Hairline);
            drop.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            drop.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            drop.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            drop.SetValue(Border.PaddingProperty, new Thickness(4));
            drop.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
            var host = new FrameworkElementFactory(typeof(StackPanel));
            host.SetValue(Panel.IsItemsHostProperty, true);
            scroller.AppendChild(host);
            drop.AppendChild(scroller);
            popup.AppendChild(drop);
            root.AppendChild(popup);
            template.VisualTree = root;
            return template;
        }

        static TextBlock FieldLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 12, Foreground = Theme.Label, Margin = new Thickness(0, 0, 0, 4) };
        }

        static TextBox FieldBox(string text)
        {
            var box = new TextBox
            {
                Text = text ?? "",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 18),
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Theme.Field,
                Foreground = Theme.Ink,
                BorderBrush = Theme.Hairline,
                CaretBrush = Theme.Pink
            };
            StyleField(box);
            return box;
        }

        void SaveSettings()
        {
            var logs = setLogsBox.Text == null ? "" : setLogsBox.Text.Trim();
            if (logs.Length > 0)
            {
                try { Directory.CreateDirectory(logs); }
                catch (Exception ex)
                {
                    SetStatus("目录不能用：" + ex.Message, Theme.Red);
                    return;
                }
                settings.LogsDir = logs;
                logsBrowsePath = logs;
                if (logsPathBox != null)
                    logsPathBox.Text = logs;
                RefreshLogsList();
            }
            settings.FeishuAppId = setAppIdBox.Text == null ? "" : setAppIdBox.Text.Trim();
            settings.FeishuChatId = setChatBox.Text == null ? "" : setChatBox.Text.Trim();
            var token = setTokenBox.Password == null ? "" : setTokenBox.Password.Trim();
            if (token.Length > 0)
                settings.FeishuToken = token;
            settings.Save();
            if (token.Length == 0)
            {
                    SetStatus("设置已保存", Theme.PinkSoft);
                    if (settingsHint != null)
                        settingsHint.Text = "已保存";
                return;
            }
            if (string.IsNullOrWhiteSpace(settings.FeishuAppId))
            {
                SetStatus("Token 已记在本地。写入飞书 CLI 还需要 App ID。", Theme.Orange);
                return;
            }
            try
            {
                StoreToken(settings.FeishuAppId, token);
                SetStatus("设置已保存，Token 已写入飞书 CLI", Theme.PinkSoft);
            }
            catch (Exception ex)
            {
                SetStatus("设置已保存，但写入飞书 CLI 失败：" + ex.Message, Theme.Orange);
            }
        }

        void FitMediaTiles()
        {
            if (mediaTiles == null || pageMedia == null)
                return;
            var width = pageMedia.ActualWidth - 28;
            if (width < 160)
                width = 160;
            mediaTiles.Width = width;
        }

        void ResetThumbs()
        {
            thumbGeneration++;
            lock (thumbLock)
            {
                thumbQueue.Clear();
                thumbPumping = false;
            }
            thumbDone = 0;
            thumbTotal = 0;
        }

        void EnqueueThumb(MediaItem item)
        {
            if (item.Thumb != null || item.ThumbQueued || item.ThumbFailed)
                return;
            if (selected == null || adbExe == null)
                return;
            item.ThumbQueued = true;
            var start = false;
            lock (thumbLock)
            {
                thumbQueue.Enqueue(item);
                thumbTotal++;
                if (!thumbPumping)
                {
                    thumbPumping = true;
                    start = true;
                }
            }
            if (!start)
                return;
            var gen = thumbGeneration;
            var adb = adbExe;
            var serial = selected.Serial;
            Task.Run(delegate { PumpThumbs(gen, adb, serial); });
        }

        void PumpThumbs(int gen, string adb, string serial)
        {
            while (true)
            {
                if (gen != thumbGeneration)
                    break;
                MediaItem item;
                lock (thumbLock)
                {
                    if (thumbQueue.Count == 0)
                    {
                        thumbPumping = false;
                        break;
                    }
                    item = thumbQueue.Dequeue();
                }
                while (busy && gen == thumbGeneration)
                    Thread.Sleep(250);
                if (gen != thumbGeneration)
                    break;
                BitmapSource bmp = null;
                try
                {
                    bmp = BuildThumb(adb, serial, item);
                }
                catch
                {
                    bmp = null;
                }
                var ready = bmp;
                var current = item;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen != thumbGeneration)
                        return;
                    current.ThumbQueued = false;
                    if (ready == null)
                        current.ThumbFailed = true;
                    else
                    {
                        current.Thumb = ready;
                        if (current.ThumbHost != null)
                            current.ThumbHost.Source = ready;
                        if (current.ThumbHint != null)
                            current.ThumbHint.Visibility = Visibility.Collapsed;
                    }
                    if (current.ThumbHint != null && ready == null)
                        current.ThumbHint.Text = "无预览";
                    thumbDone++;
                    UpdateMediaCount();
                }));
            }
        }

        BitmapSource BuildThumb(string adb, string serial, MediaItem item)
        {
            var local = EnsureLocal(adb, serial, item);
            var thumbFile = local + ".thumb.jpg";
            if (!File.Exists(thumbFile))
            {
                if (item.IsVideo)
                {
                    if (!ExtractVideoFrame(local, thumbFile))
                        return ShellThumb(local, 360, 200);
                }
                else
                {
                    try
                    {
                        var shot = LoadPicture(local, 360);
                        SaveJpeg(shot, thumbFile);
                        return shot;
                    }
                    catch
                    {
                        return ShellThumb(local, 360, 200);
                    }
                }
            }
            return LoadPicture(thumbFile, 360);
        }

        string EnsureLocal(string adb, string serial, MediaItem item)
        {
            lock (item.Gate)
            {
                if (!string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
                    return item.LocalPath;
                var dest = CachePath(serial, item);
                if (File.Exists(dest))
                {
                    var length = new FileInfo(dest).Length;
                    if (item.Size <= 0 || length == item.Size)
                    {
                        item.LocalPath = dest;
                        return dest;
                    }
                }
                var tmp = Path.Combine(Path.GetTempPath(), "nanally-pull-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                try
                {
                    var timeout = item.IsVideo ? 300000 : 90000;
                    var pulled = Adb.Run(adb, serial, "pull \"" + item.RemotePath + "\" \"" + tmp + "\"", timeout, null);
                    if (pulled.ExitCode != 0)
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(pulled.Stderr) ? "拉取失败" : pulled.Stderr.Trim());
                    var files = Directory.GetFiles(tmp);
                    if (files.Length == 0)
                        throw new InvalidOperationException("拉取后是空的");
                    var folder = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(files[0], dest);
                    item.LocalPath = dest;
                    return dest;
                }
                finally
                {
                    try { Directory.Delete(tmp, true); } catch { }
                }
            }
        }

        static string CachePath(string serial, MediaItem item)
        {
            var key = serial + "|" + item.RemotePath + "|" + item.Size.ToString(CultureInfo.InvariantCulture);
            var hash = Sha1Hex(key);
            var ext = Path.GetExtension(item.Name);
            if (string.IsNullOrEmpty(ext))
                ext = item.IsVideo ? ".mp4" : ".jpg";
            var safe = serial;
            foreach (var ch in Path.GetInvalidFileNameChars())
                safe = safe.Replace(ch, '_');
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nanally", "preview", safe);
            return Path.Combine(dir, hash + ext);
        }

        static string Sha1Hex(string text)
        {
            byte[] bytes;
            using (var sha = SHA1.Create())
                bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static bool ExtractVideoFrame(string video, string jpg)
        {
            var ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
                return false;
            if (RunFfmpeg(ffmpeg, video, jpg, "1"))
                return File.Exists(jpg);
            return RunFfmpeg(ffmpeg, video, jpg, "0") && File.Exists(jpg);
        }

        static bool RunFfmpeg(string ffmpeg, string video, string jpg, string seconds)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = ffmpeg;
            psi.Arguments = "-y -ss " + seconds + " -i \"" + video + "\" -frames:v 1 -vf scale=360:-2 \"" + jpg + "\"";
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var process = Process.Start(psi);
            if (process == null)
                return false;
            var outDone = new ManualResetEvent(false);
            var errDone = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { process.StandardOutput.ReadToEnd(); } catch { }
                outDone.Set();
            });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { process.StandardError.ReadToEnd(); } catch { }
                errDone.Set();
            });
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(); } catch { }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                return false;
            }
            outDone.WaitOne(5000);
            errDone.WaitOne(5000);
            return process.ExitCode == 0;
        }

        static string ffmpegPath;

        static string FindFfmpeg()
        {
            if (ffmpegPath != null)
                return ffmpegPath.Length == 0 ? null : ffmpegPath;
            var onPath = FindOnPath("ffmpeg.exe");
            if (onPath != null)
            {
                ffmpegPath = onPath;
                return ffmpegPath;
            }
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root, "Gyan.Fmpeg*"))
                {
                    var bins = Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (bins.Length > 0)
                    {
                        ffmpegPath = bins[0];
                        return ffmpegPath;
                    }
                }
            }
            ffmpegPath = "";
            return null;
        }

        static BitmapSource LoadPicture(string path, int maxW)
        {
            using (var src = new System.Drawing.Bitmap(path))
            {
                try
                {
                    var prop = src.GetPropertyItem(0x0112);
                    if (prop != null && prop.Value != null && prop.Value.Length > 0)
                    {
                        if (prop.Value[0] == 6)
                            src.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
                        else if (prop.Value[0] == 8)
                            src.RotateFlip(System.Drawing.RotateFlipType.Rotate270FlipNone);
                        else if (prop.Value[0] == 3)
                            src.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipNone);
                    }
                }
                catch { }
                var w = src.Width;
                var h = src.Height;
                if (w > maxW && w > 0)
                {
                    h = Math.Max(1, (int)Math.Round(h * (maxW / (double)w)));
                    w = maxW;
                }
                using (var dest = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(dest))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    return FromGdi(dest);
                }
            }
        }

        static void SaveJpeg(BitmapSource source, string path)
        {
            var encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = 80;
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
                encoder.Save(stream);
        }

        BitmapSource ShellThumb(string path, int width, int height)
        {
            BitmapSource shot = null;
            Dispatcher.Invoke(new Action(delegate
            {
                shot = TryShellThumb(path, width, height);
            }));
            return shot;
        }

        static BitmapSource TryShellThumb(string path, int width, int height)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                ThumbNative.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                var size = new ThumbSize { cx = width, cy = height };
                IntPtr hbmp;
                var hr = factory.GetImage(size, 0x1, out hbmp);
                if (hr != 0 || hbmp == IntPtr.Zero)
                    return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    ThumbNative.DeleteObject(hbmp);
                }
            }
            catch
            {
                return null;
            }
        }

        static BitmapSource FromGdi(System.Drawing.Bitmap bmp)
        {
            var h = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                ThumbNative.DeleteObject(h);
            }
        }

        void OpenPreview(MediaItem item)
        {
            if (previewLayer == null || selected == null || adbExe == null)
                return;
            previewToken++;
            var token = previewToken;
            previewLayer.Visibility = Visibility.Visible;
            previewTitle.Text = item.Name;
            previewImage.Source = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewVideo.Stop();
            previewVideo.Source = null;
            previewVideo.Visibility = Visibility.Collapsed;
            previewHint.Text = "正在准备预览…";
            previewHint.Visibility = Visibility.Visible;
            var adb = adbExe;
            var serial = selected.Serial;
            Task.Run(delegate
            {
                string path = null;
                string error = null;
                try { path = EnsureLocal(adb, serial, item); }
                catch (Exception ex) { error = ex.Message; }
                var local = path;
                var fail = error;
                var current = item;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (token != previewToken)
                        return;
                    if (string.IsNullOrEmpty(local))
                    {
                        previewHint.Text = string.IsNullOrEmpty(fail) ? "预览失败" : fail;
                        return;
                    }
                    ShowPreviewFile(current, local);
                }));
            });
        }

        void ShowPreviewFile(MediaItem item, string path)
        {
            previewHint.Visibility = Visibility.Collapsed;
            if (item.IsVideo)
            {
                previewImage.Visibility = Visibility.Collapsed;
                previewVideo.Visibility = Visibility.Visible;
                previewVideo.Source = new Uri(path);
                previewPlaying = true;
                previewVideo.Play();
                return;
            }
            previewVideo.Visibility = Visibility.Collapsed;
            try
            {
                previewImage.Source = LoadPicture(path, 1400);
                previewImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                previewHint.Text = ex.Message;
                previewHint.Visibility = Visibility.Visible;
            }
        }

        void ClosePreview()
        {
            previewToken++;
            if (previewLayer != null)
                previewLayer.Visibility = Visibility.Collapsed;
            if (previewVideo != null)
            {
                previewVideo.Stop();
                previewVideo.Source = null;
            }
            if (previewImage != null)
                previewImage.Source = null;
        }

        Grid BuildPreviewLayer()
        {
            var mask = new Grid { Background = B("#CC000000") };
            mask.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == mask)
                    ClosePreview();
            };
            var card = new Border
            {
                Width = 760,
                Height = 520,
                Background = Theme.Card,
                BorderBrush = Theme.Hairline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewTitle = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Ink,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            head.Children.Add(previewTitle);
            var close = SmallButton("关闭");
            close.Click += delegate { ClosePreview(); };
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            grid.Children.Add(head);
            var stage = new Grid { Background = Brushes.Black, Margin = new Thickness(10, 0, 10, 10) };
            previewImage = new Image { Stretch = Stretch.Uniform };
            previewVideo = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform
            };
            previewVideo.MouseLeftButtonUp += delegate
            {
                if (previewPlaying)
                {
                    previewVideo.Pause();
                    previewPlaying = false;
                }
                else
                {
                    previewVideo.Play();
                    previewPlaying = true;
                }
            };
            previewHint = new TextBlock
            {
                Foreground = Theme.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            };
            stage.Children.Add(previewImage);
            stage.Children.Add(previewVideo);
            stage.Children.Add(previewHint);
            Grid.SetRow(stage, 1);
            grid.Children.Add(stage);
            card.Child = grid;
            mask.Children.Add(card);
            return mask;
        }

        static void StoreToken(string appId, string token)
        {
            var cli = FindLarkRunner();
            if (cli == null)
                throw new InvalidOperationException("没有找到 lark-cli");
            var psi = new ProcessStartInfo();
            psi.FileName = cli.FileName;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(cli.Script))
                args.Append("\"").Append(cli.Script).Append("\" ");
            args.Append("config tenant-access-token set --app-id ").Append(appId.Replace("\"", ""));
            psi.Arguments = args.ToString();
            var process = Process.Start(psi);
            process.StandardInput.Write(token);
            process.StandardInput.Close();
            var err = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "exit " + process.ExitCode.ToString(CultureInfo.InvariantCulture) : err.Trim());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ThumbSize
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(ThumbSize size, int flags, out IntPtr phbm);
    }

    static class ThumbNative
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }

    sealed class LarkRunner
    {
        public string FileName;
        public string Script;
    }
}
