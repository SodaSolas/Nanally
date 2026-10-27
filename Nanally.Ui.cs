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
        public CheckBox CheckHost;
        public Border RingHost;
    }

    sealed class AppSettings
    {
        public string LogsDir = "";
        public string RemoteLogsDir = "";
        public string RemoteLogsHistory = "";
        public string MediaCopyDir = "";
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
        public bool ApkUseAdbInstall;

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
                s.RemoteLogsDir = Read(text, "RemoteLogsDir");
                s.RemoteLogsHistory = Read(text, "RemoteLogsHistory");
                s.MediaCopyDir = Read(text, "MediaCopyDir");
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
                s.ApkUseAdbInstall = Read(text, "ApkUseAdbInstall") == "true";
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
                + "\"RemoteLogsDir\":\"" + Esc(RemoteLogsDir) + "\","
                + "\"RemoteLogsHistory\":\"" + Esc(RemoteLogsHistory) + "\","
                + "\"MediaCopyDir\":\"" + Esc(MediaCopyDir) + "\","
                + "\"FeishuAppId\":\"" + Esc(FeishuAppId) + "\","
                + "\"FeishuToken\":\"" + Esc(FeishuToken) + "\","
                + "\"FeishuChatId\":\"" + Esc(FeishuChatId) + "\","
                + "\"ThemeMode\":\"" + Esc(ThemeMode) + "\","
                + "\"Accent\":\"" + Esc(Accent) + "\","
                + "\"FollowSystem\":\"" + (FollowSystem ? "true" : "false") + "\","
                + "\"EnableGlass\":\"" + (EnableGlass ? "true" : "false") + "\","
                + "\"SidebarExpanded\":\"" + (SidebarExpanded ? "true" : "false") + "\","
                + "\"ApkUninstallFirst\":\"" + (ApkUninstallFirst ? "true" : "false") + "\","
                + "\"ApkInstallAllDevices\":\"" + (ApkInstallAllDevices ? "true" : "false") + "\","
                + "\"ApkUseAdbInstall\":\"" + (ApkUseAdbInstall ? "true" : "false") + "\""
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

        const double MediaGap = 12;
        static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");
        Border crumbBorder;
        ScrollViewer crumbScroll;
        StackPanel crumbPanel;
        int logsGeneration;
        ScrollViewer mediaScroller;
        UIElement mediaEmptyHost;
        TextBlock mediaEmptyTitle;
        TextBlock mediaEmptyMark;
        WrapPanel mediaEmptyTips;
        bool mediaLoading;
        string mediaLoadError;
        int mediaVisibleCount;
        Border mediaBar;
        TextBlock mediaBarTitle;
        TextBlock mediaBarStatus;
        Button segAll;
        Button segShots;
        Button segVideos;
        int mediaFilterIndex;
        Button selectAllButton;
        Button mediaCopyButton;
        bool mediaCopying;
        readonly List<Border> mediaTileCards = new List<Border>();
        readonly List<Border> mediaTileFrames = new List<Border>();
        double lastFitWidth = -1;
        List<MediaItem> mediaOrdered = new List<MediaItem>();
        MediaItem previewItem;
        TextBlock previewMeta;
        Border previewCheck;
        TextBlock previewCheckText;
        Button previewPrev;
        Button previewNext;
        Button previewSend;
        TextBlock apkCountText;
        TextBlock apkDropSub;
        Border apkFileChip;
        TextBlock apkFileName;
        TextBlock apkFileMeta;
        TextBlock apkRowsHeader;
        StackPanel apkRowsPanel;
        Button apkLogToggle;
        Border apkLogBox;
        TextBlock apkIdleHint;
        readonly Dictionary<string, ApkRow> apkRows = new Dictionary<string, ApkRow>(StringComparer.OrdinalIgnoreCase);
        readonly List<Button> accentSwatches = new List<Button>();
        TextBlock pathStats;
        TextBlock pathHint;
        int pathStatsGeneration;
        TextBlock tokenPlaceholder;
        readonly System.Windows.Shapes.Ellipse[] larkDots = new System.Windows.Shapes.Ellipse[3];
        readonly TextBlock[] larkValues = new TextBlock[3];
        int larkStatusGeneration;
        Border aboutRemotePill;
        TextBlock aboutRemoteText;

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
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (previewLayer == null || previewLayer.Visibility != Visibility.Visible)
                    return;
                if (e.Key == Key.Left)
                    PreviewStep(-1);
                else if (e.Key == Key.Right)
                    PreviewStep(1);
                else if (e.Key == Key.Space)
                    PreviewToggleCheck();
                else
                    return;
                e.Handled = true;
            };
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
            navToggle = NavButton(IconSidebar, null, false);
            navToggle.Margin = new Thickness(12, 10, 12, 4);
            navToggle.ToolTip = "展开 / 收起侧栏";
            navToggle.Click += delegate { SetSidebar(!settings.SidebarExpanded); };
            DockPanel.SetDock(navToggle, Dock.Top);
            dock.Children.Add(navToggle);

            var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            tabLogs = NavButton(IconLogs, "提取日志", true);
            tabLogs.ToolTip = "提取日志";
            txtNavLogs = LabelOf(tabLogs);
            tabLogs.Click += delegate { ShowNav("logs"); };
            tabMedia = NavButton(IconMedia, "截图录屏", false);
            tabMedia.ToolTip = "截图录屏";
            txtNavMedia = LabelOf(tabMedia);
            tabMedia.Click += delegate { ShowNav("media"); };
            tabApk = NavButton(IconApk, "安装 APK", false);
            tabApk.ToolTip = "安装 APK";
            txtNavApk = LabelOf(tabApk);
            tabApk.Click += delegate { ShowNav("apk"); };
            navSettings = NavButton(IconSettings, "设置", false);
            navSettings.ToolTip = "设置";
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

        // Sidebar icons are drawn on a 20×20 grid as outline strokes, so each page reads by silhouette
        // instead of four near-identical MDL2 rectangles.
        const string IconSidebar = "M4.5,3.5 H15.5 A2,2 0 0 1 17.5,5.5 V14.5 A2,2 0 0 1 15.5,16.5 H4.5 A2,2 0 0 1 2.5,14.5 V5.5 A2,2 0 0 1 4.5,3.5 Z M8,3.5 V16.5";
        const string IconLogs = "M5.5,2.5 H11.5 L15.5,6.5 V16 A1.5,1.5 0 0 1 14,17.5 H5.5 A1.5,1.5 0 0 1 4,16 V4 A1.5,1.5 0 0 1 5.5,2.5 Z M11.5,2.5 V6.5 H15.5 M7,9.5 H12.5 M7,12 H12.5 M7,14.5 H10.5";
        const string IconMedia = "M4,4 H16 A1.5,1.5 0 0 1 17.5,5.5 V14.5 A1.5,1.5 0 0 1 16,16 H4 A1.5,1.5 0 0 1 2.5,14.5 V5.5 A1.5,1.5 0 0 1 4,4 Z M2.5,13.5 L7,9.5 L10.5,12.5 L13,10.5 L17.5,14 M12.1,7.5 A1.4,1.4 0 1 0 14.9,7.5 A1.4,1.4 0 1 0 12.1,7.5 Z";
        const string IconApk = "M10,2.5 V11.5 M6.5,8 L10,11.5 L13.5,8 M3,12.5 V15.5 A2,2 0 0 0 5,17.5 H15 A2,2 0 0 0 17,15.5 V12.5";
        const string IconSettings = "M3,6 H6.5 M10.5,6 H17 M3,14 H10 M14,14 H17 M8.5,4 A2,2 0 1 0 8.5,8 A2,2 0 1 0 8.5,4 Z M12,12 A2,2 0 1 0 12,16 A2,2 0 1 0 12,12 Z";

        static Viewbox StrokeIcon(string data, double size, Brush stroke)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = stroke,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 20,
                Height = 20,
                Stretch = Stretch.None
            };
            var box = new Grid { Width = 20, Height = 20 };
            box.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = box };
        }

        static System.Windows.Shapes.Path IconPathOf(Border tile)
        {
            var view = tile == null ? null : tile.Child as Viewbox;
            var box = view == null ? null : view.Child as Grid;
            if (box == null || box.Children.Count == 0)
                return null;
            return box.Children[0] as System.Windows.Shapes.Path;
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
            var ver = new TextBlock { Text = AppInfo.DisplayVersion, FontSize = 11 };
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

        Button NavButton(string iconData, string label, bool active)
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
                Child = StrokeIcon(iconData, 20, Theme.Ink)
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
            if (tile == null)
                return;
            tile.Tag = active ? "on" : "off";
            tile.Background = active ? Theme.Pink : Brushes.Transparent;
            var icon = IconPathOf(tile);
            if (icon != null)
                icon.Stroke = active ? Brushes.White : Theme.Ink;
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
            // 截图录屏页用浮动操作条代替底栏。
            if (actionFooter != null)
                actionFooter.Visibility = page == "logs" || page == "apk" ? Visibility.Visible : Visibility.Collapsed;
            if (footerButtons != null)
                footerButtons.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
            if (apkProgressHost != null)
                apkProgressHost.Visibility = Visibility.Collapsed;
            if (page != "media")
                ClosePreview();
            if (page == "logs")
                RefreshLogsList();
            if (page == "media")
            {
                FitMediaTiles();
                UpdateMediaEmpty();
                if (!busy)
                    RefreshMediaAsync();
            }
            if (page == "apk")
            {
                UpdateApkHeader();
                SetStatus(settings.ApkInstallAllDevices
                    ? "把 APK 拖进来，或点上面的区域选择；会装到所有已连接手机"
                    : "把 APK 拖进来，或点上面的区域选择；装到顶栏选中的那台", Theme.Label);
            }
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
                Margin = new Thickness(12, 8, 12, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            deviceCard = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "nodrag",
                Child = deviceHost
            };
            Theme.BindElement(deviceCard, BackgroundProperty, "nn.Card");
            Theme.BindElement(deviceCard, Border.BorderBrushProperty, "nn.Hairline");
            row.Children.Add(deviceCard);
            var refresh = IconButton("", "刷新设备");
            refresh.Margin = new Thickness(8, 0, 0, 0);
            refreshText = refresh.Content as TextBlock;
            refresh.Click += delegate
            {
                if (!busy)
                    RefreshAsync();
            };
            Grid.SetColumn(refresh, 1);
            row.Children.Add(refresh);
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

        Button IconButton(string glyph, string tip)
        {
            var text = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.IconFont,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            BindInk(text);
            var btn = new Button
            {
                Content = text,
                Width = 32,
                Height = 32,
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = tip
            };
            btn.Template = OutlineButtonTemplate(new Thickness(0), Theme.Card);
            return btn;
        }

        static Border Pill(string text, out TextBlock label)
        {
            label = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            BindLabel(label);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
            };
            Theme.BindElement(pill, BackgroundProperty, "nn.Chip");
            return pill;
        }

        static void PaintPill(Border pill, TextBlock label, string fgKey, string bgKey)
        {
            if (pill == null || label == null)
                return;
            label.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
            pill.SetResourceReference(Border.BackgroundProperty, bgKey);
        }

        void OnDevicesRendered()
        {
            UpdateApkHeader();
            UpdateMediaEmpty();
        }

        UIElement BuildApkTools()
        {
            TextBlock label;
            var pill = Pill("已连接 0 台", out label);
            apkCountText = label;
            pill.HorizontalAlignment = HorizontalAlignment.Right;
            pill.SetResourceReference(Border.BackgroundProperty, "nn.Card");
            pill.Padding = new Thickness(12, 5, 12, 6);
            pill.CornerRadius = new CornerRadius(14);
            return pill;
        }

        UIElement BuildLogsTools()
        {
            var addr = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var up = IconButton("", "上级目录");
            up.Click += delegate { BrowseUp(); };
            addr.Children.Add(up);

            crumbBorder = new Border
            {
                Height = 32,
                Margin = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.IBeam,
                ToolTip = "点空白处可以直接输入路径",
                Tag = "nodrag"
            };
            Theme.BindElement(crumbBorder, BackgroundProperty, "nn.Field");
            Theme.BindElement(crumbBorder, Border.BorderBrushProperty, "nn.Hairline");
            crumbScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            crumbPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            crumbScroll.Content = crumbPanel;
            crumbBorder.Child = crumbScroll;
            crumbBorder.MouseLeftButtonUp += delegate { EnterPathEdit(); };
            Grid.SetColumn(crumbBorder, 1);
            addr.Children.Add(crumbBorder);

            logsPathBox = new TextBox
            {
                Text = logsBrowsePath,
                Height = 32,
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = MonoFont,
                FontSize = 12,
                Visibility = Visibility.Collapsed
            };
            StyleField(logsPathBox);
            logsPathBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    var typed = logsPathBox.Text;
                    ExitPathEdit();
                    ApplyLogsPath(typed);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    ExitPathEdit();
                }
            };
            logsPathBox.LostKeyboardFocus += delegate { ExitPathEdit(); };
            Grid.SetColumn(logsPathBox, 1);
            addr.Children.Add(logsPathBox);

            var browse = IconButton("", "选择目录");
            browse.Click += delegate { BrowseLogsFolder(); };
            Grid.SetColumn(browse, 2);
            addr.Children.Add(browse);
            return addr;
        }

        void EnterPathEdit()
        {
            if (logsPathBox == null || crumbBorder == null)
                return;
            logsPathBox.Text = logsBrowsePath ?? "";
            crumbBorder.Visibility = Visibility.Collapsed;
            logsPathBox.Visibility = Visibility.Visible;
            logsPathBox.Focus();
            logsPathBox.SelectAll();
        }

        void ExitPathEdit()
        {
            if (logsPathBox == null || crumbBorder == null || logsPathBox.Visibility != Visibility.Visible)
                return;
            logsPathBox.Visibility = Visibility.Collapsed;
            crumbBorder.Visibility = Visibility.Visible;
            logsPathBox.Text = logsBrowsePath ?? "";
        }

        void SetLogsPathText(string path)
        {
            if (logsPathBox != null && logsPathBox.Visibility != Visibility.Visible)
                logsPathBox.Text = path ?? "";
            if (crumbPanel == null)
                return;
            crumbPanel.Children.Clear();
            if (string.IsNullOrEmpty(path))
                return;
            var parts = new List<string>();
            var paths = new List<string>();
            var cursor = path.TrimEnd('\\');
            while (!string.IsNullOrEmpty(cursor))
            {
                var parent = Path.GetDirectoryName(cursor);
                var name = Path.GetFileName(cursor);
                if (string.IsNullOrEmpty(name))
                    name = cursor.TrimEnd('\\');
                parts.Insert(0, name);
                paths.Insert(0, cursor.Length == 2 && cursor[1] == ':' ? cursor + "\\" : cursor);
                if (string.IsNullOrEmpty(parent) || parent == cursor)
                    break;
                cursor = parent.TrimEnd('\\');
            }
            for (var i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    var sep = new TextBlock { Text = "›", FontSize = 12, Margin = new Thickness(1, 0, 1, 1), VerticalAlignment = VerticalAlignment.Center };
                    BindLabel(sep);
                    sep.Opacity = 0.6;
                    crumbPanel.Children.Add(sep);
                }
                var last = i == parts.Count - 1;
                var target = paths[i];
                var text = new TextBlock
                {
                    Text = parts[i],
                    FontFamily = MonoFont,
                    FontSize = 12,
                    Margin = new Thickness(6, 2, 6, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (last)
                    BindInk(text);
                else
                    BindLabel(text);
                var seg = new Button { Content = text, Cursor = Cursors.Hand, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
                seg.Template = FlatButtonTemplate(last ? (Brush)Theme.Chip : Brushes.Transparent, Theme.RowHover, 5);
                seg.Click += delegate
                {
                    if (!Directory.Exists(target))
                        return;
                    logsBrowsePath = target;
                    RefreshLogsList();
                };
                crumbPanel.Children.Add(seg);
            }
            var scroll = crumbScroll;
            if (scroll != null)
                Dispatcher.BeginInvoke(new Action(delegate { scroll.ScrollToRightEnd(); }), DispatcherPriority.Loaded);
        }

        UIElement BuildMediaTools()
        {
            var bar = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mediaCount = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 11
            };
            BindLabel(mediaCount);
            bar.Children.Add(mediaCount);

            var seg = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center
            };
            Theme.BindElement(seg, BackgroundProperty, "nn.Card");
            Theme.BindElement(seg, Border.BorderBrushProperty, "nn.Hairline");
            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            segAll = SegButton(0);
            segShots = SegButton(1);
            segVideos = SegButton(2);
            segRow.Children.Add(segAll);
            segRow.Children.Add(segShots);
            segRow.Children.Add(segVideos);
            seg.Child = segRow;
            Grid.SetColumn(seg, 1);
            bar.Children.Add(seg);
            PaintSegments();

            var reload = IconButton("", "重新从手机读取");
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Click += delegate { RefreshMediaAsync(); };
            Grid.SetColumn(reload, 2);
            bar.Children.Add(reload);
            selectAllButton = SmallButton("全选");
            selectAllButton.Margin = new Thickness(8, 0, 0, 0);
            selectAllButton.Template = OutlineButtonTemplate(new Thickness(12, 0, 12, 0), Theme.Card);
            selectAllButton.Click += delegate { SetAllChecked(!AllVisibleChecked()); };
            Grid.SetColumn(selectAllButton, 3);
            bar.Children.Add(selectAllButton);
            return bar;
        }

        Button SegButton(int index)
        {
            var text = new TextBlock { FontSize = 12, Margin = new Thickness(11, 3, 11, 4), VerticalAlignment = VerticalAlignment.Center };
            var btn = new Button { Content = text, Cursor = Cursors.Hand, Focusable = false };
            btn.Click += delegate
            {
                if (mediaFilterIndex == index)
                    return;
                mediaFilterIndex = index;
                PaintSegments();
                RenderMediaRows();
            };
            return btn;
        }

        void PaintSegments()
        {
            var shots = 0;
            var videos = 0;
            foreach (var item in mediaItems)
            {
                if (item.IsVideo)
                    videos++;
                else
                    shots++;
            }
            PaintSegment(segAll, "全部", shots + videos, mediaFilterIndex == 0);
            PaintSegment(segShots, "截图", shots, mediaFilterIndex == 1);
            PaintSegment(segVideos, "录屏", videos, mediaFilterIndex == 2);
        }

        void PaintSegment(Button btn, string label, int count, bool on)
        {
            if (btn == null)
                return;
            var text = btn.Content as TextBlock;
            if (text != null)
            {
                text.Text = mediaItems.Count == 0 ? label : label + " " + count.ToString(CultureInfo.InvariantCulture);
                if (on)
                    BindInk(text);
                else
                    BindLabel(text);
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            btn.Template = FlatButtonTemplate(on ? (Brush)Theme.Chip : Brushes.Transparent, on ? (Brush)Theme.Chip : Theme.RowHover, 7);
        }

        Grid BuildLogsPage()
        {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 8) };
            var card = GlassCard();
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = ExplorerHeader(false);
            inner.Children.Add(header);
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var host = new Grid();
            logsRows = new StackPanel { Margin = new Thickness(6, 2, 6, 6) };
            logsEmpty = new TextBlock
            {
                Text = "这个目录是空的",
                Margin = new Thickness(16, 18, 0, 0),
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
            var page = new Grid { Margin = new Thickness(12, 10, 12, 12) };
            var card = GlassCard();
            var layer = new Grid();
            mediaScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 12, 10, 12)
            };
            mediaTiles = new StackPanel { Margin = new Thickness(0, 0, 0, 72) };
            mediaTiles.SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                if (e.WidthChanged)
                    FitMediaTiles();
            };
            mediaScroller.Content = mediaTiles;
            layer.Children.Add(mediaScroller);
            mediaEmptyHost = BuildEmptyState();
            layer.Children.Add(mediaEmptyHost);
            card.Child = layer;
            page.Children.Add(card);
            mediaBar = BuildMediaBar();
            page.Children.Add(mediaBar);
            return page;
        }

        UIElement BuildEmptyState()
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 420,
                Margin = new Thickness(24)
            };
            var phone = new Border
            {
                Width = 48,
                Height = 80,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(2.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Theme.BindElement(phone, Border.BorderBrushProperty, "nn.Label");
            var phoneInner = new Grid();
            mediaEmptyMark = new TextBlock
            {
                Text = "!",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            mediaEmptyMark.SetResourceReference(TextBlock.ForegroundProperty, "nn.Orange");
            phoneInner.Children.Add(mediaEmptyMark);
            var home = new Border
            {
                Width = 14,
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Theme.BindElement(home, BackgroundProperty, "nn.Label");
            phoneInner.Children.Add(home);
            phone.Child = phoneInner;
            stack.Children.Add(phone);
            mediaEmptyTitle = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            BindInk(mediaEmptyTitle);
            stack.Children.Add(mediaEmptyTitle);
            mediaEmpty = new TextBlock
            {
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 8, 0, 0)
            };
            BindLabel(mediaEmpty);
            stack.Children.Add(mediaEmpty);
            mediaEmptyTips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            stack.Children.Add(mediaEmptyTips);
            return stack;
        }

        void UpdateMediaEmpty()
        {
            if (mediaEmptyHost == null)
                return;
            string title = null;
            string body = null;
            var warn = false;
            var tips = false;
            if (adbExe == null && !mediaLoading)
            {
                title = "没有找到 adb";
                body = "装好 Android platform-tools，或把 adb 加进 PATH，再点顶栏的刷新。";
            }
            else if (selected == null)
            {
                title = "用数据线连上手机";
                body = "手机打开 USB 调试，连上后点顶栏的刷新。";
            }
            else if (!selected.IsReady)
            {
                title = "在手机上点「允许 USB 调试」";
                body = selected.DisplayName + " 已经插上，但还没允许这台电脑调试。手机上点「允许」之后，再点顶栏的刷新。";
                warn = true;
                tips = true;
            }
            else if (mediaLoading && mediaItems.Count == 0)
            {
                title = "正在读取…";
                body = "从 " + selected.DisplayName + " 列出截图和录屏";
            }
            else if (mediaItems.Count == 0)
            {
                title = mediaLoadError != null ? "读取失败" : "手机上没有截图或录屏";
                body = mediaLoadError ?? "常见的截图、录屏目录里都没找到文件。";
                warn = mediaLoadError != null;
            }
            else if (mediaVisibleCount == 0)
            {
                title = "这个筛选下没有文件";
                body = "切回「全部」看看。";
            }
            if (title == null)
            {
                mediaEmptyHost.Visibility = Visibility.Collapsed;
                return;
            }
            mediaEmptyHost.Visibility = Visibility.Visible;
            mediaEmptyTitle.Text = title;
            mediaEmpty.Text = body;
            mediaEmptyMark.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
            mediaEmptyTips.Children.Clear();
            if (tips)
            {
                mediaEmptyTips.Children.Add(TipChip("没看到弹窗？拔插一次数据线"));
                mediaEmptyTips.Children.Add(TipChip("勾上「一律允许」，下次就不问了"));
            }
        }

        static Border TipChip(string text)
        {
            var label = new TextBlock { Text = text, FontSize = 11 };
            BindLabel(label);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 3, 10, 4),
                Margin = new Thickness(4),
                Child = label
            };
            Theme.BindElement(chip, Border.BorderBrushProperty, "nn.Hairline");
            return chip;
        }

        Border BuildMediaBar()
        {
            var bar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 16),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 7, 7, 7),
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.35,
                    Color = Colors.Black
                }
            };
            Theme.BindElement(bar, BackgroundProperty, "nn.Menu");
            Theme.BindElement(bar, Border.BorderBrushProperty, "nn.Hairline");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), MinWidth = 140 };
            mediaBarTitle = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            BindInk(mediaBarTitle);
            info.Children.Add(mediaBarTitle);
            mediaBarStatus = new TextBlock
            {
                FontSize = 11,
                MaxWidth = 260,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 1, 0, 0)
            };
            BindLabel(mediaBarStatus);
            info.Children.Add(mediaBarStatus);
            grid.Children.Add(info);
            var cancel = SmallButton("取消");
            cancel.Template = OutlineButtonTemplate(new Thickness(12, 0, 12, 0), Brushes.Transparent);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += delegate
            {
                if (!busy)
                    SetAllChecked(false);
            };
            Grid.SetColumn(cancel, 1);
            grid.Children.Add(cancel);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mediaCopyButton = SmallButton("拷贝到目录");
            mediaCopyButton.Margin = new Thickness(0, 0, 8, 0);
            mediaCopyButton.ToolTip = "保存勾选的截图或录屏，同名文件自动改名";
            mediaCopyButton.Click += delegate { CopySelectedMediaAsync(); };
            Grid.SetColumn(mediaCopyButton, 2);
            grid.Children.Add(mediaCopyButton);
            sendButton = MakePrimary("发给自己");
            sendButton.Click += delegate { SendSelectedAsync(); };
            Grid.SetColumn(sendButton, 3);
            grid.Children.Add(sendButton);
            bar.Child = grid;
            return bar;
        }

        void MirrorMediaStatus(string text, Brush brush)
        {
            if (mediaBarStatus == null || currentPage != "media")
                return;
            mediaBarStatus.Text = text ?? "";
            mediaBarStatus.ToolTip = text;
            mediaBarStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            var key = Theme.Key(brush ?? Theme.Label);
            if (key != null)
                mediaBarStatus.SetResourceReference(TextBlock.ForegroundProperty, key);
            else
                mediaBarStatus.Foreground = brush;
        }

        Grid BuildApkPage()
        {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 8) };
            page.AllowDrop = true;
            page.DragOver += OnApkDragOver;
            page.Drop += OnApkDrop;
            var card = GlassCard();
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18, 16, 18, 16) };
            var stack = new StackPanel();

            var drop = new Grid { MinHeight = 104, Cursor = Cursors.Hand, AllowDrop = true, Background = Brushes.Transparent };
            var dash = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 14,
                RadiusY = 14,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
                Opacity = 0.6
            };
            dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "nn.Pink");
            dash.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "nn.AccentSoft");
            drop.Children.Add(dash);
            var dropRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 16, 22, 16) };
            var iconTile = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Center,
                Child = StrokeIcon(IconApk, 22, Theme.Pink)
            };
            Theme.BindElement(iconTile, BackgroundProperty, "nn.AccentSoft");
            dropRow.Children.Add(iconTile);
            var dropText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            apkHint = new TextBlock { Text = "把 .apk 拖到这里", FontSize = 14, FontWeight = FontWeights.SemiBold };
            BindInk(apkHint);
            dropText.Children.Add(apkHint);
            apkDropSub = new TextBlock { FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            BindLabel(apkDropSub);
            dropText.Children.Add(apkDropSub);
            dropRow.Children.Add(dropText);
            drop.Children.Add(dropRow);
            drop.DragOver += OnApkDragOver;
            drop.Drop += OnApkDrop;
            drop.MouseEnter += delegate { dash.Opacity = 1; };
            drop.MouseLeave += delegate { dash.Opacity = 0.6; };
            drop.MouseLeftButtonUp += delegate
            {
                if (!busy)
                    PickApkFile();
            };
            stack.Children.Add(drop);

            var opts = new WrapPanel { Margin = new Thickness(2, 14, 0, 0) };
            chkApkInstallAll = IosSwitch();
            chkApkInstallAll.IsChecked = settings.ApkInstallAllDevices;
            chkApkInstallAll.Checked += delegate { OnApkInstallAllToggled(); };
            chkApkInstallAll.Unchecked += delegate { OnApkInstallAllToggled(); };
            opts.Children.Add(ApkOption(chkApkInstallAll, "装到全部已连接手机", "多台时并行安装"));
            chkApkUninstall = IosSwitch();
            chkApkUninstall.IsChecked = settings.ApkUninstallFirst;
            chkApkUninstall.Checked += delegate { OnApkUninstallToggled(); };
            chkApkUninstall.Unchecked += delegate { OnApkUninstallToggled(); };
            opts.Children.Add(ApkOption(chkApkUninstall, "先卸载同包名再安装", "需要 build-tools 里的 aapt 读包名"));
            chkApkUseAdbInstall = IosSwitch();
            chkApkUseAdbInstall.IsChecked = settings.ApkUseAdbInstall;
            chkApkUseAdbInstall.Checked += delegate { OnApkUseAdbInstallToggled(); };
            chkApkUseAdbInstall.Unchecked += delegate { OnApkUseAdbInstallToggled(); };
            opts.Children.Add(ApkOption(chkApkUseAdbInstall, "用 adb install", "关闭时走 push + pm install"));
            stack.Children.Add(opts);

            apkFileChip = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 12, 8),
                Margin = new Thickness(0, 14, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Theme.BindElement(apkFileChip, BackgroundProperty, "nn.RowHover");
            var chipRow = new Grid();
            chipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = B("#3DDC84"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "APK",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = B("#0B3B22"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            chipRow.Children.Add(badge);
            var chipText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            apkFileName = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            BindInk(apkFileName);
            chipText.Children.Add(apkFileName);
            apkFileMeta = new TextBlock { FontSize = 11, FontFamily = MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            BindLabel(apkFileMeta);
            chipText.Children.Add(apkFileMeta);
            Grid.SetColumn(chipText, 1);
            chipRow.Children.Add(chipText);
            apkFileChip.Child = chipRow;
            stack.Children.Add(apkFileChip);

            apkRowsHeader = new TextBlock
            {
                Text = "安装进度",
                FontSize = 12,
                Margin = new Thickness(2, 16, 0, 8),
                Visibility = Visibility.Collapsed
            };
            BindLabel(apkRowsHeader);
            stack.Children.Add(apkRowsHeader);
            apkRowsPanel = new StackPanel();
            stack.Children.Add(apkRowsPanel);
            apkIdleHint = new TextBlock
            {
                Text = "选好 APK 之后，这里会按手机逐台显示推送和安装进度。",
                FontSize = 12,
                Margin = new Thickness(2, 22, 0, 0)
            };
            BindLabel(apkIdleHint);
            apkIdleHint.Opacity = 0.7;
            stack.Children.Add(apkIdleHint);

            apkLogToggle = new Button
            {
                Cursor = Cursors.Hand,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var toggleText = new TextBlock { Text = "显示详细日志", FontSize = 12, Margin = new Thickness(4, 3, 4, 3) };
            toggleText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Pink");
            apkLogToggle.Content = toggleText;
            apkLogToggle.Template = FlatButtonTemplate(Brushes.Transparent, Theme.RowHover, 6);
            apkLogToggle.Click += delegate
            {
                var show = apkLogBox.Visibility != Visibility.Visible;
                apkLogBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggleText.Text = show ? "收起详细日志" : "显示详细日志";
            };
            stack.Children.Add(apkLogToggle);
            apkLogBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Theme.BindElement(apkLogBox, BackgroundProperty, "nn.Field");
            apkLogScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 220
            };
            apkLogPanel = new StackPanel();
            apkLogScroller.Content = apkLogPanel;
            apkLogBox.Child = apkLogScroller;
            stack.Children.Add(apkLogBox);

            scroller.Content = stack;
            card.Child = scroller;
            page.Children.Add(card);
            UpdateApkHeader();
            return page;
        }

        void UpdateApkHeader()
        {
            var ready = 0;
            foreach (var item in devices)
            {
                if (item != null && item.IsReady)
                    ready++;
            }
            if (apkCountText != null)
                apkCountText.Text = "已连接 " + ready.ToString(CultureInfo.InvariantCulture) + " 台";
            if (apkDropSub != null)
            {
                string target;
                if (settings.ApkInstallAllDevices)
                    target = "会装到全部 " + ready.ToString(CultureInfo.InvariantCulture) + " 台已连接手机";
                else if (selected != null && selected.IsReady)
                    target = "装到 " + selected.DisplayName;
                else
                    target = "先连上一台已授权的手机";
                apkDropSub.Text = "或点这里选择文件  ·  " + target;
            }
        }

        static UIElement ApkOption(CheckBox box, string label, string sub)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 28, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(box);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = label, FontSize = 12.5 };
            BindInk(title);
            text.Children.Add(title);
            var note = new TextBlock { Text = sub, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
            BindLabel(note);
            note.Opacity = 0.85;
            text.Children.Add(note);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
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
            UpdateApkHeader();
        }

        void OnApkUseAdbInstallToggled()
        {
            if (applyingTheme || chkApkUseAdbInstall == null)
                return;
            settings.ApkUseAdbInstall = chkApkUseAdbInstall.IsChecked == true;
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
                FontSize = 11.5,
                FontFamily = MonoFont,
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
            if (apkLogToggle != null)
                apkLogToggle.Visibility = Visibility.Visible;
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

        sealed class ApkRow
        {
            public TextBlock Phase;
            public TextBlock Pct;
            public Border Track;
            public Border Fill;
            public double Value;
            public int State;
            public DateTime Started;
        }

        void ResetApkRows(List<DeviceInfo> targets)
        {
            apkRows.Clear();
            if (apkRowsPanel == null)
                return;
            apkRowsPanel.Children.Clear();
            foreach (var device in targets)
            {
                var row = new ApkRow { Started = DateTime.UtcNow };
                var card = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                Theme.BindElement(card, BackgroundProperty, "nn.RowHover");
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var name = new TextBlock { Text = device.DisplayName, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                BindInk(name);
                names.Children.Add(name);
                row.Phase = new TextBlock { Text = "等待开始", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
                BindLabel(row.Phase);
                names.Children.Add(row.Phase);
                grid.Children.Add(names);
                row.Track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                Theme.BindElement(row.Track, BackgroundProperty, "nn.Chip");
                row.Fill = new Border { Width = 0, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left };
                Theme.BindElement(row.Fill, BackgroundProperty, "nn.Pink");
                row.Track.Child = row.Fill;
                var current = row;
                row.Track.SizeChanged += delegate { SyncApkRowFill(current); };
                Grid.SetColumn(row.Track, 1);
                grid.Children.Add(row.Track);
                row.Pct = new TextBlock { Text = "0%", FontSize = 11, FontFamily = MonoFont, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                BindInk(row.Pct);
                Grid.SetColumn(row.Pct, 2);
                grid.Children.Add(row.Pct);
                card.Child = grid;
                apkRowsPanel.Children.Add(card);
                apkRows[device.Serial] = row;
            }
            if (apkRowsHeader != null)
                apkRowsHeader.Visibility = targets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (apkIdleHint != null)
                apkIdleHint.Visibility = targets.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        static void SyncApkRowFill(ApkRow row)
        {
            if (row == null || row.Track == null || row.Fill == null)
                return;
            var w = row.Track.ActualWidth;
            row.Fill.Width = w <= 0 ? 0 : w * (row.Value / 100.0);
        }

        // state: 0 进行中，1 成功，2 失败。pct < 0 表示只更新文案。
        void UpdateApkRow(string serial, double pct, string text, int state)
        {
            ApkRow row;
            if (string.IsNullOrEmpty(serial) || !apkRows.TryGetValue(serial, out row))
                return;
            if (row.State != 0 && state == 0)
                return;
            if (pct >= 0)
            {
                if (pct > 100)
                    pct = 100;
                if (pct > row.Value)
                    row.Value = pct;
            }
            if (state != 0)
            {
                row.State = state;
                row.Value = 100;
            }
            SyncApkRowFill(row);
            if (row.State == 1)
            {
                Theme.BindElement(row.Fill, BackgroundProperty, "nn.Ok");
                row.Pct.Text = "✓";
                row.Pct.SetResourceReference(TextBlock.ForegroundProperty, "nn.Ok");
            }
            else if (row.State == 2)
            {
                Theme.BindElement(row.Fill, BackgroundProperty, "nn.Red");
                row.Pct.Text = "✕";
                row.Pct.SetResourceReference(TextBlock.ForegroundProperty, "nn.Red");
            }
            else
            {
                row.Pct.Text = ((int)Math.Round(row.Value)).ToString(CultureInfo.InvariantCulture) + "%";
            }
            if (text != null)
            {
                row.Phase.Text = text;
                row.Phase.SetResourceReference(TextBlock.ForegroundProperty, row.State == 1 ? "nn.Ok" : row.State == 2 ? "nn.Red" : "nn.Label");
            }
        }

        string ApkRowElapsed(string serial)
        {
            ApkRow row;
            if (string.IsNullOrEmpty(serial) || !apkRows.TryGetValue(serial, out row))
                return "";
            var sec = (int)(DateTime.UtcNow - row.Started).TotalSeconds;
            if (sec < 60)
                return sec.ToString(CultureInfo.InvariantCulture) + " 秒";
            return (sec / 60).ToString(CultureInfo.InvariantCulture) + " 分 " + (sec % 60).ToString(CultureInfo.InvariantCulture) + " 秒";
        }

        void RowText(string serial, string text, int state)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (state == 1)
                    text = text + " · " + ApkRowElapsed(serial);
                UpdateApkRow(serial, -1, text, state);
            }));
        }

        void ShowApkFile(string path, int index, int total)
        {
            if (apkFileChip == null)
                return;
            apkFileChip.Visibility = Visibility.Visible;
            apkFileName.Text = Path.GetFileName(path) + (total > 1 ? "（" + index.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + "）" : "");
            long size = 0;
            try { size = new FileInfo(path).Length; }
            catch { }
            apkFileMeta.Text = Paths.FormatSize(size) + " · " + (Path.GetDirectoryName(path) ?? "");
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
            var useAdbInstall = settings.ApkUseAdbInstall;
            var adb = adbExe;
            var paths = new List<string>(apkPaths);
            var parallel = ready.Count > 1;
            busy = true;
            UpdateButtons();
            ClearApkLog();
            ResetApkRows(ready);
            apkProgressStartedUtc = DateTime.UtcNow;
            WhatHappened.Info("安装 APK 开始 files=" + paths.Count.ToString(CultureInfo.InvariantCulture)
                + " devices=" + ready.Count.ToString(CultureInfo.InvariantCulture)
                + " uninstallFirst=" + (uninstallFirst ? "1" : "0")
                + " allDevices=" + (settings.ApkInstallAllDevices ? "1" : "0")
                + " useAdbInstall=" + (useAdbInstall ? "1" : "0") + " adb=" + adb);
            ShowApkProgress(null, 1, false);
            SetStatus("正在安装到 " + ready.Count.ToString(CultureInfo.InvariantCulture) + " 台手机…", Theme.Label);

            Task.Run(delegate
            {
                var counts = new int[2]; // 0=ok, 1=fail
                var countLock = new object();
                var apkIndex = 0;
                foreach (var apkPath in paths)
                {
                    apkIndex++;
                    var fileName = Path.GetFileName(apkPath);
                    var shownPath = apkPath;
                    var shownIndex = apkIndex;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (shownIndex > 1)
                            ResetApkRows(ready);
                        ShowApkFile(shownPath, shownIndex, paths.Count);
                    }));
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
                            foreach (var d in ready)
                                RowText(d.Serial, "读包名失败：" + (packageError ?? "未知错误"), 2);
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
                        var rowSerial = serial;
                        var rowPct = pct;
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            ShowApkProgress(null, avg, false);
                            UpdateApkRow(rowSerial, rowPct, null, 0);
                        }));
                    };

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog("开始 " + fileName + (string.IsNullOrEmpty(pkg) ? "" : "（" + pkg + "）")
                            + " → " + deviceCount.ToString(CultureInfo.InvariantCulture) + " 台"
                            + (deviceCount > 1 ? (useAdbInstall ? "（排队安装）" : "（排队推送 · 并行安装）") : "")
                            + " · " + (useAdbInstall ? "adb install" : "push + pm"), Theme.Ink);
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
                        var adbMode = useAdbInstall;
                        tasks.Add(Task.Factory.StartNew(delegate
                        {
                            RunInstallOnDevice(adb, target, apk, uninstall, package, counts, countLock, parallel, report, adbMode);
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
                        SetStatus("全部完成：成功 " + counts[0].ToString(CultureInfo.InvariantCulture) + " 次", Theme.Ok);
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

        void RunInstallOnDevice(string adb, DeviceInfo device, string apkPath, bool uninstallFirst, string packageName, int[] counts, object countLock, bool parallel, Action<string, double> reportProgress, bool useAdbInstall)
        {
            var label = device.DisplayName + " [" + device.Serial + "]";
            var serial = device.Serial;
            var phase = useAdbInstall ? "install" : "push";
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
                    RowText(serial, "推送中 · 已等 " + sec.ToString(CultureInfo.InvariantCulture) + "s"
                        + (lastPct >= 0 ? " · " + lastPct.ToString(CultureInfo.InvariantCulture) + "%" : ""), 0);
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
                    bump(useAdbInstall && lastPct >= 0 ? (10 + lastPct * 0.8) : 88);
                    RowText(serial, (useAdbInstall ? "adb install" : "pm install") + " · 已等 " + sec.ToString(CultureInfo.InvariantCulture) + "s，可能在等手机确认", 0);
                    if (!parallel)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            var waitLabel = useAdbInstall ? "等待 adb install / 手机确认…" : "等待 pm install / 手机确认…";
                            SetStatus(label + " " + waitLabel + "已等 " + sec.ToString(CultureInfo.InvariantCulture) + "s"
                                + (useAdbInstall && lastPct >= 0 ? "（" + lastPct.ToString(CultureInfo.InvariantCulture) + "%）" : ""), Theme.Label);
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
                var progressPhase = string.Equals(phase, "push", StringComparison.Ordinal)
                    || (useAdbInstall && string.Equals(phase, "install", StringComparison.Ordinal));
                if (pct >= 0 && progressPhase)
                {
                    var now = DateTime.UtcNow;
                    if (pct > lastPct && (pct >= lastPct + 2 || (now - lastUi).TotalMilliseconds >= 350 || pct == 100))
                    {
                        lastPct = pct;
                        lastUi = now;
                        var mapped = useAdbInstall ? (10 + pct * 0.8) : (10 + pct * 0.65);
                        bump(mapped);
                        RowText(serial, (useAdbInstall ? "安装中 " : "推送中 ") + pct.ToString(CultureInfo.InvariantCulture) + "%", 0);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            var verb = useAdbInstall ? "安装" : "推送";
                            SetStatus(label + " " + verb + " " + pct.ToString(CultureInfo.InvariantCulture) + "%", Theme.Label);
                            if (pct >= apkLastLoggedPercent + 10 || pct == 100)
                            {
                                apkLastLoggedPercent = pct;
                                AppendApkLog(label + " · " + verb + " " + pct.ToString(CultureInfo.InvariantCulture) + "%", Theme.Label);
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
                if (string.Equals(phase, "queued", StringComparison.Ordinal))
                {
                    RowText(serial, "排队等待传输", 0);
                }
                else if (string.Equals(phase, "push", StringComparison.Ordinal))
                {
                    lastPct = -1;
                    bump(10);
                    RowText(serial, "开始推送", 0);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog(label + " · 开始推送（push）", Theme.Ink);
                    }));
                }
                else if (string.Equals(phase, "install", StringComparison.Ordinal))
                {
                    bump(useAdbInstall ? 10 : 85);
                    RowText(serial, useAdbInstall ? "adb install 中" : "推送完成，pm install 中", 0);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (useAdbInstall)
                        {
                            AppendApkLog(label + " · 开始 adb install（无输出时看手机确认框）", Theme.Ink);
                            if (!parallel)
                                SetStatus(label + " adb install 中…", Theme.Label);
                        }
                        else
                        {
                            AppendApkLog(label + " · 推送完成，开始 pm install（无输出时看手机确认框）", Theme.Ink);
                            if (!parallel)
                                SetStatus(label + " pm install 中…", Theme.Label);
                        }
                    }));
                }
            };

            heartbeat = new Timer(delegate { refreshHeartbeat(); }, null, 1000, 1500);

            try
            {
                bump(4);
                RowText(serial, "准备中", 0);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    AppendApkLog(label + " · 开始（" + (useAdbInstall ? "adb install" : "push + pm install") + "）", Theme.Ink);
                    if (!parallel)
                        SetStatus("正在安装到 " + label + "…", Theme.Label);
                }));

                phase = "check";
                RowText(serial, "检查设备连接…", 0);
                var probe = Adb.Run(adb, serial, "shell echo NANALLY_READY", 8000, null);
                if (probe.TimedOut || probe.ExitCode != 0 || (probe.Stdout ?? "").Trim() != "NANALLY_READY")
                {
                    lock (countLock) { counts[1]++; }
                    bump(100);
                    var probeMessage = label + " ADB 通道无响应，请解锁手机并重新插拔 USB 后重试";
                    RowText(serial, "ADB 通道无响应，请重新插拔 USB", 2);
                    Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(probeMessage, Theme.Red); }));
                    return;
                }

                if (uninstallFirst && !string.IsNullOrEmpty(packageName))
                {
                    if (Apk.IsInstalled(adb, device.Serial, packageName))
                    {
                        phase = "uninstall";
                        phaseStarted = DateTime.UtcNow;
                        bump(8);
                        RowText(serial, "卸载旧包…", 0);
                        var un = Apk.Uninstall(adb, device.Serial, packageName, onAdbLine);
                        if (un.TimedOut || un.ExitCode != 0)
                        {
                            lock (countLock) { counts[1]++; }
                            bump(100);
                            var detail = Adb.FirstLine(un.Stderr.Length > 0 ? un.Stderr : un.Stdout);
                            var msg = label + " 卸载失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
                            RowText(serial, "卸载失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail), 2);
                            Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(msg, Theme.Red); }));
                            return;
                        }
                        Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(label + " 已卸载旧包", Theme.Label); }));
                    }
                }

                var installed = Apk.Install(adb, device.Serial, apkPath, !uninstallFirst, onAdbLine, onPhase, useAdbInstall);
                if (installed.TimedOut)
                {
                    lock (countLock) { counts[1]++; }
                    bump(100);
                    var timeoutMessage = phase == "push"
                        ? "推送超时，请检查 USB 连接并重新插拔"
                        : "安装超时，请检查手机确认框和 USB 连接";
                    Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(label + " " + timeoutMessage, Theme.Red); }));
                    RowText(serial, timeoutMessage, 2);
                    return;
                }
                var success = Apk.InstallSucceeded(installed);
                bump(100);
                if (success)
                {
                    lock (countLock) { counts[0]++; }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        AppendApkLog(label + " 安装成功", Theme.Ok);
                    }));
                    RowText(serial, "安装成功", 1);
                }
                else
                {
                    lock (countLock) { counts[1]++; }
                    var detail = Adb.FirstLine(installed.Stderr.Length > 0 ? installed.Stderr : installed.Stdout);
                    if (string.IsNullOrEmpty(detail))
                        detail = "exit " + installed.ExitCode.ToString(CultureInfo.InvariantCulture);
                    var msg = label + " 安装失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
                    Dispatcher.BeginInvoke(new Action(delegate { AppendApkLog(msg, Theme.Red); }));
                    RowText(serial, "安装失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail), 2);
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
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14)
            };
            Theme.BindElement(card, BackgroundProperty, "nn.Card");
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
                Height = 32,
                Padding = new Thickness(6, 0, 6, 0),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Theme.BindElement(header, Border.BorderBrushProperty, "nn.Hairline");
            var grid = HeaderGrid(media);
            AddHeader(grid, 1, "名称", false);
            AddHeader(grid, 2, "修改时间", false);
            AddHeader(grid, 3, "大小", true);
            AddHeader(grid, 4, "文件数", true);
            header.Child = grid;
            return header;
        }

        static Grid HeaderGrid(bool media)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            return grid;
        }

        static void AddHeader(Grid grid, int col, string text, bool right)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(8, 0, right ? 12 : 8, 0)
            };
            BindLabel(block);
            block.Opacity = 0.85;
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        UIElement BuildActionFooter()
        {
            var footer = new Border
            {
                Margin = new Thickness(12, 0, 12, 12),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 8, 8, 8),
                MinHeight = 48
            };
            Theme.BindElement(footer, BackgroundProperty, "nn.Card");
            ClipRound(footer, 14);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock
            {
                Text = "连上手机，点一下就拷贝",
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            row.Children.Add(statusText);
            footerRightHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            footerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            openCurrentButton = SmallButton("打开文件夹");
            openCurrentButton.Margin = new Thickness(0, 0, 8, 0);
            openCurrentButton.Click += delegate { OpenCurrentFolder(); };
            copyButton = MakePrimary("拷贝日志");
            copyButton.IsEnabled = false;
            copyButton.Click += delegate { StartCopy(); };
            footerButtons.Children.Add(openCurrentButton);
            footerButtons.Children.Add(copyButton);
            footerRightHost.Children.Add(footerButtons);

            // 总进度条保留（安装逻辑仍在算），界面上改由每台手机一行进度展示。
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
            apkProgressHost.Children.Add(apkProgressTrack);
            apkProgressText = new TextBlock { Text = "0%", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Right };
            BindInk(apkProgressText);
            Grid.SetColumn(apkProgressText, 1);
            apkProgressHost.Children.Add(apkProgressText);
            footerRightHost.Children.Add(apkProgressHost);

            Grid.SetColumn(footerRightHost, 1);
            row.Children.Add(footerRightHost);
            footer.Child = row;
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
                Focusable = false,
                FontSize = 12
            };
            btn.SetResourceReference(Control.ForegroundProperty, "nn.Ink");
            btn.SetResourceReference(Control.BackgroundProperty, "nn.CardSoft");
            btn.Template = OutlineButtonTemplate();
            return btn;
        }

        static ControlTemplate OutlineButtonTemplate()
        {
            return OutlineButtonTemplate(new Thickness(12, 0, 12, 0), Theme.CardSoft);
        }

        static ControlTemplate OutlineButtonTemplate(Thickness padding, Brush normal)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            Theme.Bind(border, Border.BackgroundProperty, normal);
            Theme.Bind(border, Border.BorderBrushProperty, Theme.Hairline);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, padding);
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
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path.Trim()))
            {
                SetStatus("目录不存在", Theme.Red);
                return;
            }
            path = path.Trim();
            logsBrowsePath = path;
            settings.LogsDir = path;
            settings.Save();
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
            RefreshLogsList();
        }

        void RefreshLogsList()
        {
            if (logsRows == null)
                return;
            logsRows.Children.Clear();
            var gen = ++logsGeneration;
            SetLogsPathText(logsBrowsePath);
            if (string.IsNullOrEmpty(logsBrowsePath) || !Directory.Exists(logsBrowsePath))
            {
                logsEmpty.Text = "目录不存在";
                logsEmpty.Visibility = Visibility.Visible;
                return;
            }
            List<DirectoryInfo> dirs;
            List<FileInfo> files;
            try
            {
                dirs = new List<DirectoryInfo>(new DirectoryInfo(logsBrowsePath).GetDirectories());
                files = new List<FileInfo>(new DirectoryInfo(logsBrowsePath).GetFiles());
            }
            catch (Exception ex)
            {
                logsEmpty.Text = "打不开这个目录：" + ex.Message;
                logsEmpty.Visibility = Visibility.Visible;
                return;
            }
            dirs.RemoveAll(delegate(DirectoryInfo d) { return d.Name.StartsWith(".nanally", StringComparison.OrdinalIgnoreCase); });
            dirs.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            var pending = new List<KeyValuePair<string, TextBlock[]>>();
            foreach (var dir in dirs)
            {
                TextBlock sizeCell;
                TextBlock countCell;
                logsRows.Children.Add(LocalRow(dir.Name, dir.LastWriteTime, "", dir.FullName, true, out sizeCell, out countCell));
                // 只统计「设备名+时间」快照目录，避免浏览到盘根时递归整个磁盘。
                if (SnapshotPattern.IsMatch(dir.Name))
                    pending.Add(new KeyValuePair<string, TextBlock[]>(dir.FullName, new[] { sizeCell, countCell }));
            }
            foreach (var file in files)
            {
                TextBlock sizeCell;
                TextBlock countCell;
                logsRows.Children.Add(LocalRow(file.Name, file.LastWriteTime, Paths.FormatSize(file.Length), file.FullName, false, out sizeCell, out countCell));
            }
            logsEmpty.Visibility = logsRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            logsEmpty.Text = "这个目录是空的";
            if (pending.Count == 0)
                return;
            Task.Run(delegate
            {
                foreach (var entry in pending)
                {
                    if (gen != logsGeneration)
                        return;
                    long bytes = 0;
                    var count = 0;
                    try
                    {
                        foreach (var path in Directory.GetFiles(entry.Key, "*", SearchOption.AllDirectories))
                        {
                            count++;
                            try { bytes += new FileInfo(path).Length; }
                            catch { }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                    var cells = entry.Value;
                    var sizeText = Paths.FormatSize(bytes);
                    var countText = count.ToString(CultureInfo.InvariantCulture);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (gen != logsGeneration)
                            return;
                        cells[0].Text = sizeText;
                        cells[1].Text = countText;
                    }));
                }
            });
        }

        static readonly Regex SnapshotPattern = new Regex(@"^(.+)\+(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(_\d+)?$");

        Border LocalRow(string name, DateTime when, string size, string full, bool folder, out TextBlock sizeCell, out TextBlock countCell)
        {
            var row = MakeRow(false);
            var grid = row.Child as Grid;
            var highlight = folder && !string.IsNullOrEmpty(logsHighlight)
                && string.Equals(full.TrimEnd('\\'), logsHighlight.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (highlight)
            {
                row.Tag = Theme.Selected;
                row.Background = Theme.Selected;
            }

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            nameRow.Children.Add(folder ? FolderGlyph() : FileGlyph());
            var match = folder ? SnapshotPattern.Match(name) : null;
            if (match != null && match.Success)
            {
                var device = new TextBlock { Text = match.Groups[1].Value, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                BindInk(device);
                nameRow.Children.Add(device);
                var stamp = new TextBlock
                {
                    Text = match.Groups[2].Value + match.Groups[3].Value,
                    FontFamily = MonoFont,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 1, 0, 0)
                };
                BindLabel(stamp);
                nameRow.Children.Add(stamp);
            }
            else
            {
                var plain = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) };
                BindInk(plain);
                nameRow.Children.Add(plain);
            }
            if (highlight)
            {
                TextBlock pillText;
                var pill = Pill("刚刚", out pillText);
                pill.Margin = new Thickness(10, 0, 0, 0);
                PaintPill(pill, pillText, "nn.Pink", "nn.AccentSoft");
                nameRow.Children.Add(pill);
            }
            Grid.SetColumn(nameRow, 1);
            grid.Children.Add(nameRow);

            var whenText = when.Year == DateTime.Now.Year
                ? when.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            grid.Children.Add(Cell(whenText, 2, false));
            sizeCell = Cell(size, 3, true);
            grid.Children.Add(sizeCell);
            countCell = Cell("", 4, true);
            grid.Children.Add(countCell);
            row.MouseLeftButtonUp += delegate
            {
                if (folder)
                {
                    logsBrowsePath = full;
                    RefreshLogsList();
                    return;
                }
                try { Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true }); }
                catch (Exception ex) { SetStatus("打不开：" + ex.Message, Theme.Orange); }
            };
            return row;
        }

        static TextBlock Cell(string text, int col, bool right)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = MonoFont,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(8, 0, right ? 12 : 8, 0)
            };
            BindLabel(block);
            Grid.SetColumn(block, col);
            return block;
        }

        static UIElement FolderGlyph()
        {
            var host = new Grid { Width = 16, Height = 13, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(new Border { Width = 7, Height = 4, CornerRadius = new CornerRadius(1.5, 1.5, 0, 0), Background = B("#E8B04B"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            host.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(2), Background = B("#E8B04B"), VerticalAlignment = VerticalAlignment.Bottom });
            return host;
        }

        static UIElement FileGlyph()
        {
            var glyph = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 14, Width = 16, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            BindLabel(glyph);
            return glyph;
        }

        void RefreshMediaAsync()
        {
            if (busy)
                return;
            if (selected == null || !selected.IsReady || adbExe == null)
            {
                UpdateMediaEmpty();
                return;
            }
            busy = true;
            mediaLoading = true;
            mediaLoadError = null;
            UpdateButtons();
            UpdateMediaEmpty();
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
                    mediaLoading = false;
                    mediaLoadError = error;
                    if (selected == null || selected.Serial != serial)
                    {
                        UpdateButtons();
                        UpdateMediaEmpty();
                        return;
                    }
                    var keep = new Dictionary<string, bool>(StringComparer.Ordinal);
                    foreach (var old in mediaItems)
                    {
                        if (old.IsChecked)
                            keep[old.RemotePath] = true;
                    }
                    foreach (var item in found)
                    {
                        if (keep.ContainsKey(item.RemotePath))
                            item.IsChecked = true;
                    }
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
            mediaTileCards.Clear();
            mediaTileFrames.Clear();
            var ordered = new List<MediaItem>();
            foreach (var item in mediaItems)
            {
                item.CheckHost = null;
                item.RingHost = null;
                if (mediaFilterIndex == 1 && item.IsVideo)
                    continue;
                if (mediaFilterIndex == 2 && !item.IsVideo)
                    continue;
                ordered.Add(item);
            }
            ordered.Sort(CompareMediaNewestFirst);
            mediaOrdered = ordered;
            // 按月分组；相邻的稀疏月份合并成一段，避免每个月只有一两张时一行一个标题。
            var bucketOf = new Dictionary<MediaItem, int>();
            var bucketLabels = new List<string>();
            var bucketCounts = new List<int>();
            string firstKey = null;
            string lastKeySeen = null;
            var run = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var key = MediaGroupKey(ordered[i].DateTicks);
                if (key != lastKeySeen && lastKeySeen != null && run >= 4)
                {
                    bucketLabels.Add(firstKey == lastKeySeen ? firstKey : firstKey + " – " + lastKeySeen);
                    bucketCounts.Add(run);
                    run = 0;
                    firstKey = null;
                }
                if (firstKey == null)
                {
                    firstKey = key;
                }
                lastKeySeen = key;
                run++;
                bucketOf[ordered[i]] = bucketLabels.Count;
            }
            if (run > 0)
            {
                bucketLabels.Add(firstKey == lastKeySeen ? firstKey : firstKey + " – " + lastKeySeen);
                bucketCounts.Add(run);
            }
            var lastBucket = -1;
            WrapPanel wrap = null;
            foreach (var item in ordered)
            {
                var bucket = bucketOf[item];
                if (bucket != lastBucket)
                {
                    lastBucket = bucket;
                    var head = new TextBlock { Margin = new Thickness(2, mediaTiles.Children.Count == 0 ? 2 : 10, 0, 10) };
                    head.Inlines.Add(new Run(bucketLabels[bucket]) { FontSize = 13, FontWeight = FontWeights.SemiBold });
                    var countRun = new Run("   " + bucketCounts[bucket].ToString(CultureInfo.InvariantCulture) + " 项") { FontSize = 11.5 };
                    countRun.SetResourceReference(TextElement.ForegroundProperty, "nn.Label");
                    head.Inlines.Add(countRun);
                    BindInk(head);
                    mediaTiles.Children.Add(head);
                    wrap = new WrapPanel { Margin = new Thickness(0, 0, -MediaGap, 0) };
                    mediaTiles.Children.Add(wrap);
                }
                wrap.Children.Add(MediaTile(item));
            }
            mediaVisibleCount = ordered.Count;
            PaintSegments();
            UpdateMediaEmpty();
            UpdateMediaCount();
            lastFitWidth = -1;
            FitMediaTiles();
            foreach (var item in ordered)
                EnqueueThumb(item);
        }

        static string MediaGroupKey(long ticks)
        {
            if (ticks <= 0)
                return "更早";
            var day = new DateTime(ticks).Date;
            var today = DateTime.Today;
            if (day == today)
                return "今天";
            if (day == today.AddDays(-1))
                return "昨天";
            if (day.Year == today.Year)
                return day.Month.ToString(CultureInfo.InvariantCulture) + " 月";
            return day.Year.ToString(CultureInfo.InvariantCulture) + " 年 " + day.Month.ToString(CultureInfo.InvariantCulture) + " 月";
        }

        Border MediaTile(MediaItem item)
        {
            var tile = new Border
            {
                Width = 180,
                Margin = new Thickness(0, 0, MediaGap, MediaGap),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = BuildMediaTip(item)
            };
            var stack = new StackPanel();
            var ring = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                BorderBrush = Brushes.Transparent
            };
            var frame = new Border
            {
                Height = 78,
                Background = B("#111111"),
                CornerRadius = new CornerRadius(6)
            };
            ClipRound(frame, 6);
            var box = new Grid();
            var hint = new TextBlock
            {
                Text = item.IsVideo ? "▶" : "图",
                Foreground = Theme.Label,
                FontSize = item.IsVideo ? 18 : 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var image = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = B("#99000000"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 5),
                    ToolTip = "预览",
                    Child = new TextBlock
                    {
                        Text = "▶",
                        Foreground = Brushes.White,
                        FontSize = 8,
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
            check.Checked += delegate { item.IsChecked = true; PaintDot(check); PaintTile(ring, item, tile.IsMouseOver); UpdateMediaCount(); };
            check.Unchecked += delegate { item.IsChecked = false; PaintDot(check); PaintTile(ring, item, tile.IsMouseOver); UpdateMediaCount(); };
            check.Loaded += delegate { PaintDot(check); };
            check.IsChecked = item.IsChecked;
            item.CheckHost = check;
            item.RingHost = ring;
            box.Children.Add(check);
            frame.Child = box;
            ring.Child = frame;
            stack.Children.Add(ring);

            var caption = new Grid { Margin = new Thickness(4, 3, 4, 0) };
            var when = new TextBlock { Text = ShortMediaTime(item), FontFamily = MonoFont, FontSize = 11 };
            BindLabel(when);
            caption.Children.Add(when);
            var size = new TextBlock { Text = Paths.FormatSize(item.Size), FontFamily = MonoFont, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            BindLabel(size);
            size.Opacity = 0.8;
            caption.Children.Add(size);
            stack.Children.Add(caption);
            tile.Child = stack;
            PaintTile(ring, item, false);
            mediaTileCards.Add(tile);
            mediaTileFrames.Add(frame);

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
                check.IsChecked = !item.IsChecked;
            };
            tile.MouseEnter += delegate { PaintTile(ring, item, true); };
            tile.MouseLeave += delegate { PaintTile(ring, item, false); };
            return tile;
        }

        static string ShortMediaTime(MediaItem item)
        {
            if (item.DateTicks <= 0)
                return item.DateText ?? "";
            var when = new DateTime(item.DateTicks);
            return when.Year == DateTime.Now.Year
                ? when.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

        static void PaintTile(Border ring, MediaItem item, bool hover)
        {
            if (ring == null)
                return;
            if (item.IsChecked)
                ring.BorderBrush = Theme.Pink;
            else if (hover)
                ring.BorderBrush = Theme.Stroke;
            else
                ring.BorderBrush = Brushes.Transparent;
        }

        static ToolTip BuildMediaTip(MediaItem item)
        {
            var stack = new StackPanel();
            stack.Children.Add(TipLine("名称：", item.Name));
            stack.Children.Add(TipLine("类型：", KindLabel(item)));
            stack.Children.Add(TipLine("大小：", Paths.FormatSize(item.Size)));
            stack.Children.Add(TipLine("修改日期：", FormatMediaDate(item.DateText)));
            return new ToolTip
            {
                Background = Theme.Menu,
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
            long bytes = 0;
            foreach (var item in mediaItems)
            {
                if (!item.IsChecked)
                    continue;
                picked++;
                bytes += item.Size;
            }
            if (mediaCount != null)
            {
                mediaCount.Text = thumbTotal > 0 && thumbDone < thumbTotal
                    ? "缩略图 " + thumbDone.ToString(CultureInfo.InvariantCulture) + "/" + thumbTotal.ToString(CultureInfo.InvariantCulture)
                    : "";
            }
            if (mediaBarTitle != null)
                mediaBarTitle.Text = "已选 " + picked.ToString(CultureInfo.InvariantCulture) + " 项 · " + Paths.FormatSize(bytes);
            if (mediaBar != null)
            {
                var show = picked > 0;
                if (show && mediaBar.Visibility != Visibility.Visible && mediaBarStatus != null)
                {
                    mediaBarStatus.Text = "";
                    mediaBarStatus.Visibility = Visibility.Collapsed;
                }
                mediaBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
            var label = picked > 0 ? "发给自己（" + picked.ToString(CultureInfo.InvariantCulture) + "）" : "发给自己";
            if (sendButton != null)
                sendButton.Content = busy && !mediaCopying && currentPage == "media" ? "正在发送…" : label;
            if (mediaCopyButton != null)
                mediaCopyButton.Content = mediaCopying ? "正在拷贝…" : "拷贝到目录";
            if (previewSend != null)
                previewSend.Content = label;
            if (selectAllButton != null)
                selectAllButton.Content = AllVisibleChecked() ? "取消全选" : "全选";
        }

        bool AllVisibleChecked()
        {
            var any = false;
            foreach (var item in mediaOrdered)
            {
                if (!item.IsChecked)
                    return false;
                any = true;
            }
            return any;
        }

        Border MakeRow(bool media)
        {
            var grid = HeaderGrid(media);
            var row = new Border
            {
                Child = grid,
                Height = 38,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand
            };
            // Tag 存行的常态底色（高亮行用 Selected），悬停离开时恢复它。
            row.MouseEnter += delegate
            {
                if (row.Tag == null)
                    row.Background = Theme.RowHover;
            };
            row.MouseLeave += delegate
            {
                var keep = row.Tag as Brush;
                row.Background = keep ?? Brushes.Transparent;
            };
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
            foreach (var item in mediaItems)
            {
                if (mediaFilterIndex == 1 && item.IsVideo)
                    continue;
                if (mediaFilterIndex == 2 && !item.IsVideo)
                    continue;
                item.IsChecked = value;
                if (item.CheckHost != null)
                    item.CheckHost.IsChecked = value;
            }
            UpdateMediaCount();
        }

        void CopySelectedMediaAsync()
        {
            if (busy)
                return;
            var chosen = new List<MediaItem>();
            foreach (var item in mediaItems)
                if (item.IsChecked)
                    chosen.Add(item);
            if (chosen.Count == 0)
            {
                SetStatus("先勾选要拷贝的截图或录屏", Theme.Orange);
                return;
            }
            if (selected == null || !selected.IsReady || adbExe == null)
            {
                SetStatus("手机不在线", Theme.Red);
                return;
            }
            string destination;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择截图 / 录屏保存目录";
                dialog.SelectedPath = Directory.Exists(settings.MediaCopyDir) ? settings.MediaCopyDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                destination = dialog.SelectedPath;
            }
            settings.MediaCopyDir = destination;
            try { settings.Save(); }
            catch (Exception ex) { WhatHappened.Error("保存媒体目录设置失败: " + ex.Message); }
            var adb = adbExe;
            var serial = selected.Serial;
            busy = true;
            mediaCopying = true;
            UpdateButtons();
            SetStatus("正在准备拷贝…", Theme.Label);
            WhatHappened.Info("媒体拷贝开始 count=" + chosen.Count.ToString(CultureInfo.InvariantCulture));
            Task.Run(delegate
            {
                var copied = 0;
                var failed = 0;
                var lastError = "";
                for (var i = 0; i < chosen.Count; i++)
                {
                    var item = chosen[i];
                    var progress = "正在拷贝 " + (i + 1).ToString(CultureInfo.InvariantCulture) + "/"
                        + chosen.Count.ToString(CultureInfo.InvariantCulture) + " " + item.Name;
                    Dispatcher.BeginInvoke(new Action(delegate { SetStatus(progress, Theme.Label); }));
                    try
                    {
                        var local = EnsureLocal(adb, serial, item);
                        if (item.Size > 0 && new FileInfo(local).Length != item.Size)
                            throw new IOException("文件大小不完整，请重新读取设备后重试");
                        Paths.CopyMediaFile(local, destination, item.Name);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        lastError = item.Name + "：" + ex.Message;
                        WhatHappened.Error("媒体拷贝失败: " + lastError);
                    }
                }
                var message = "已拷贝 " + copied.ToString(CultureInfo.InvariantCulture) + " 项到 " + destination;
                if (failed > 0)
                    message += "；失败 " + failed.ToString(CultureInfo.InvariantCulture) + " 项，" + lastError;
                WhatHappened.Info("媒体拷贝完成: " + message);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    mediaCopying = false;
                    UpdateButtons();
                    SetStatus(message, failed == 0 ? Theme.Ok : Theme.Orange);
                    if (mediaBarStatus != null)
                        mediaBarStatus.ToolTip = message;
                }));
            });
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
                    SetStatus(message, ok ? Theme.Ok : Theme.Red);
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
                    SetStatus(message, ok ? Theme.Ok : Theme.Red);
                    SetSettingsHint(message, ok ? Theme.Ok : Theme.Red);
                    UpdateButtons();
                    RefreshLarkStatus();
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
            if (ExtractVideoFrame(localVideo, coverPath, true) && File.Exists(coverPath))
                return true;
            try
            {
                var shot = ShellThumb(localVideo, 2560, 2560, 0x81);
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
            setRemoteLogsBox.Text = Adb.NormalizeRemoteLogs(settings.RemoteLogsDir);
            setAppIdBox.Text = settings.FeishuAppId;
            setTokenBox.Password = "";
            UpdateTokenPlaceholder();
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
            PaintSwatches();
            applyingTheme = false;
            SetSettingsHint("", null);
            ShowSettingsSection("look");
        }

        Grid BuildSettingsPage()
        {
            var page = new Grid { Margin = new Thickness(12, 10, 12, 12) };
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            settingsNavCard = new Border
            {
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8)
            };
            Theme.BindElement(settingsNavCard, BackgroundProperty, "nn.Card");
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
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14)
            };
            Theme.BindElement(settingsContentCard, BackgroundProperty, "nn.Card");
            ClipRound(settingsContentCard, 14);
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 20, 24, 20) };
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
                Margin = new Thickness(0, 0, 0, 2),
                Height = 36,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                FontSize = 13,
                Focusable = false,
                Cursor = Cursors.Hand
            };
            var grid = new Grid { Height = 36 };
            var mark = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 9, 0, 9)
            };
            Theme.BindElement(mark, BackgroundProperty, "nn.Pink");
            grid.Children.Add(mark);
            grid.Children.Add(new TextBlock { Text = text, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            btn.Content = grid;
            PaintSettingsNav(btn, active);
            return btn;
        }

        static void PaintSettingsNav(Button btn, bool active)
        {
            if (btn == null)
                return;
            btn.Template = LeftButtonTemplate(active ? Theme.AccentSoft : Brushes.Transparent, active ? Theme.AccentSoft : Theme.RowHover, 8);
            var grid = btn.Content as Grid;
            if (grid == null || grid.Children.Count < 2)
                return;
            grid.Children[0].Visibility = active ? Visibility.Visible : Visibility.Hidden;
            var label = grid.Children[1] as TextBlock;
            if (label != null)
            {
                if (active)
                    BindInk(label);
                else
                    BindLabel(label);
                label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        void ShowSettingsSection(string section)
        {
            if (panelSetLook != null)
                panelSetLook.Visibility = section == "look" ? Visibility.Visible : Visibility.Collapsed;
            if (panelSetPath != null)
                panelSetPath.Visibility = section == "path" ? Visibility.Visible : Visibility.Collapsed;
            if (panelSetFeishu != null)
                panelSetFeishu.Visibility = section == "feishu" ? Visibility.Visible : Visibility.Collapsed;
            PaintSettingsNav(btnSetLook, section == "look");
            PaintSettingsNav(btnSetPath, section == "path");
            PaintSettingsNav(btnSetFeishu, section == "feishu");
            if (section == "path")
                UpdatePathStats();
            if (section == "feishu")
                RefreshLarkStatus();
        }

        StackPanel BuildLookSettings()
        {
            var stack = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(GroupTitle("主题色"));
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            swatches.Children.Add(AccentSwatch("Blue", "蓝", "#3794FF"));
            swatches.Children.Add(AccentSwatch("Pink", "粉", "#EC4899"));
            swatches.Children.Add(AccentSwatch("Green", "绿", "#22C55E"));
            stack.Children.Add(swatches);
            stack.Children.Add(Divider());
            stack.Children.Add(GroupTitle("外观"));
            chkFollowSystem = IosSwitch();
            chkFollowSystem.Checked += delegate { OnThemeToggled(); };
            chkFollowSystem.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(SwitchRow(chkFollowSystem, "跟随系统主题", "按 Windows 的浅色 / 深色自动切换"));
            chkThemeDark = IosSwitch();
            chkThemeDark.Checked += delegate { OnThemeToggled(); };
            chkThemeDark.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(SwitchRow(chkThemeDark, "暗色", "半透明炭黑色板；打开「跟随系统主题」时不可用"));
            chkGlass = IosSwitch();
            chkGlass.Checked += delegate { OnThemeToggled(); };
            chkGlass.Unchecked += delegate { OnThemeToggled(); };
            stack.Children.Add(SwitchRow(chkGlass, "毛玻璃", "窗口和侧栏透出桌面；窗口失焦时系统可能收成实色"));
            return stack;
        }

        Button AccentSwatch(string key, string label, string hex)
        {
            var ring = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new System.Windows.Shapes.Ellipse { Fill = B(hex) }
            };
            var text = new TextBlock { Text = label, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(ring);
            stack.Children.Add(text);
            var btn = new Button { Content = stack, Width = 56, Cursor = Cursors.Hand, Focusable = false, Tag = key, ToolTip = label + "色" };
            btn.Template = FlatButtonTemplate(Brushes.Transparent, Brushes.Transparent, 8);
            btn.Click += delegate
            {
                if (settings.Accent == key)
                    return;
                settings.Accent = key;
                ApplyAppearance();
                PaintSwatches();
            };
            accentSwatches.Add(btn);
            return btn;
        }

        void PaintSwatches()
        {
            foreach (var btn in accentSwatches)
            {
                var stack = btn.Content as StackPanel;
                if (stack == null || stack.Children.Count < 2)
                    continue;
                var on = Equals(btn.Tag, settings.Accent);
                var ring = stack.Children[0] as Border;
                if (ring != null)
                {
                    if (on)
                        ring.SetResourceReference(Border.BorderBrushProperty, "nn.Ink");
                    else
                        ring.BorderBrush = Brushes.Transparent;
                }
                var text = stack.Children[1] as TextBlock;
                if (text != null)
                {
                    if (on)
                        BindInk(text);
                    else
                        BindLabel(text);
                    text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }

        static TextBlock GroupTitle(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            BindInk(block);
            return block;
        }

        static Border Divider()
        {
            var line = new Border { Height = 1, Margin = new Thickness(0, 18, 0, 18) };
            Theme.BindElement(line, BackgroundProperty, "nn.Hairline");
            return line;
        }

        // 开关靠左（AGENTS 约定），标题和说明在右边。
        static UIElement SwitchRow(CheckBox box, string title, string desc)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            box.Margin = new Thickness(0, 1, 14, 0);
            box.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(box);
            var text = new StackPanel();
            var head = new TextBlock { Text = title, FontSize = 13 };
            BindInk(head);
            text.Children.Add(head);
            var note = new TextBlock { Text = desc, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            BindLabel(note);
            text.Children.Add(note);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }

        static TextBlock FieldTitle(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) };
            BindInk(block);
            return block;
        }

        static TextBlock FieldNote(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            BindLabel(block);
            return block;
        }

        StackPanel BuildPathSettings()
        {
            var stack = new StackPanel { Visibility = Visibility.Collapsed, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(FieldTitle("游戏日志目录"));
            stack.Children.Add(FieldNote("「拷贝日志」会在这里建「设备名+时间」文件夹。"));
            var logsRow = new Grid();
            logsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            logsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            setLogsBox = FieldBox(settings.LogsDir);
            setLogsBox.Margin = new Thickness(0);
            setLogsBox.FontFamily = MonoFont;
            setLogsBox.FontSize = 12;
            logsRow.Children.Add(setLogsBox);
            var browse = SmallButton("浏览");
            browse.Margin = new Thickness(8, 0, 0, 0);
            browse.Click += delegate
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = "选择游戏日志保存目录";
                dlg.SelectedPath = Directory.Exists(setLogsBox.Text) ? setLogsBox.Text : settings.LogsDir;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    setLogsBox.Text = dlg.SelectedPath;
            };
            Grid.SetColumn(browse, 1);
            logsRow.Children.Add(browse);
            var open = SmallButton("打开");
            open.Margin = new Thickness(8, 0, 0, 0);
            open.Click += delegate
            {
                var path = setLogsBox.Text == null ? "" : setLogsBox.Text.Trim();
                if (Directory.Exists(path))
                    OpenFolder(path);
                else
                    SetSettingsHint("这个目录还不存在，保存后会自动创建", Theme.Orange);
            };
            Grid.SetColumn(open, 2);
            logsRow.Children.Add(open);
            stack.Children.Add(logsRow);
            pathStats = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0) };
            BindLabel(pathStats);
            pathStats.Opacity = 0.85;
            stack.Children.Add(pathStats);

            stack.Children.Add(Divider());
            stack.Children.Add(FieldTitle("手机上的日志目录"));
            stack.Children.Add(FieldNote("「拷贝日志」从手机的这个目录拉。换项目时改包名即可，可以只写 Android/data/包名/files/logs，会自动补 /sdcard/。留空恢复默认。"));
            var remoteRow = new Grid();
            remoteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            remoteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            setRemoteLogsBox = FieldBox(Adb.NormalizeRemoteLogs(settings.RemoteLogsDir));
            setRemoteLogsBox.Margin = new Thickness(0);
            setRemoteLogsBox.FontFamily = MonoFont;
            setRemoteLogsBox.FontSize = 12;
            remoteRow.Children.Add(setRemoteLogsBox);
            remoteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var historyRemote = SmallButton("历史");
            historyRemote.Margin = new Thickness(8, 0, 0, 0);
            historyRemote.ToolTip = "选择用过的目录";
            historyRemote.Click += delegate
            {
                if ((DateTime.UtcNow - remoteHistoryClosedAt).TotalMilliseconds < 250)
                    return;
                OpenRemoteHistory(remoteRow);
            };
            Grid.SetColumn(historyRemote, 1);
            remoteRow.Children.Add(historyRemote);
            var resetRemote = SmallButton("恢复默认");
            resetRemote.Margin = new Thickness(8, 0, 0, 0);
            resetRemote.Click += delegate { setRemoteLogsBox.Text = Adb.RemoteLogs; };
            Grid.SetColumn(resetRemote, 2);
            remoteRow.Children.Add(resetRemote);
            stack.Children.Add(remoteRow);

            stack.Children.Add(Divider());
            stack.Children.Add(FieldTitle("Nanally 运行日志"));
            stack.Children.Add(FieldNote("安装失败、发飞书失败时，把这个文件夹打包发给维护的人。它记的是 Nanally 自己，和上面的游戏日志不是一回事。"));
            var whRow = new Grid();
            whRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            whRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var whBox = FieldBox(WhatHappened.Dir());
            whBox.Margin = new Thickness(0);
            whBox.IsReadOnly = true;
            whBox.FontFamily = MonoFont;
            whBox.FontSize = 12;
            whRow.Children.Add(whBox);
            var openWh = SmallButton("打开");
            openWh.Margin = new Thickness(8, 0, 0, 0);
            openWh.Click += delegate { OpenFolder(WhatHappened.Dir()); };
            Grid.SetColumn(openWh, 1);
            whRow.Children.Add(openWh);
            stack.Children.Add(whRow);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
            var save = MakePrimary("保存");
            save.Click += delegate { SaveSettings(); };
            actions.Children.Add(save);
            pathHint = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            BindLabel(pathHint);
            actions.Children.Add(pathHint);
            stack.Children.Add(actions);
            return stack;
        }

        List<string> RemoteLogsHistory()
        {
            var list = new List<string>();
            foreach (var line in (settings.RemoteLogsHistory ?? "").Split('\n'))
            {
                var p = line.Trim();
                if (p.Length > 0 && p != Adb.RemoteLogs && !list.Contains(p))
                    list.Add(p);
            }
            return list;
        }

        // 最近用的排最前，最多 10 条；默认目录不进历史，下拉里固定放最后。
        void RememberRemoteLogs(string remote)
        {
            var list = RemoteLogsHistory();
            list.Remove(remote);
            if (remote != Adb.RemoteLogs)
                list.Insert(0, remote);
            if (list.Count > 10)
                list.RemoveRange(10, list.Count - 10);
            settings.RemoteLogsHistory = string.Join("\n", list.ToArray());
        }

        void OpenRemoteHistory(FrameworkElement anchor)
        {
            if (remoteHistoryPopup == null)
            {
                remoteHistoryPopup = new Popup
                {
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade,
                    VerticalOffset = 4
                };
                remoteHistoryPopup.Closed += delegate { remoteHistoryClosedAt = DateTime.UtcNow; };
            }
            var list = new StackPanel();
            foreach (var path in RemoteLogsHistory())
                list.Children.Add(RemoteHistoryRow(path, false));
            list.Children.Add(RemoteHistoryRow(Adb.RemoteLogs, true));
            remoteHistoryPopup.Child = new Border
            {
                Background = Theme.Card,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Child = new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = list
                },
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Colors.Black
                }
            };
            remoteHistoryPopup.PlacementTarget = anchor;
            remoteHistoryPopup.Width = anchor.ActualWidth > 0 ? anchor.ActualWidth : 480;
            remoteHistoryPopup.IsOpen = true;
        }

        UIElement RemoteHistoryRow(string path, bool isDefault)
        {
            var current = Adb.NormalizeRemoteLogs(setRemoteLogsBox.Text) == path;
            var row = new Border
            {
                Padding = new Thickness(10, 7, 6, 7),
                CornerRadius = new CornerRadius(6),
                Background = current ? Theme.MenuHover : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 1, 0, 1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock
            {
                Text = path,
                FontFamily = MonoFont,
                FontSize = 12,
                Foreground = Theme.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = path
            };
            grid.Children.Add(text);
            if (isDefault)
            {
                var tag = new TextBlock { Text = "默认", FontSize = 11.5, Foreground = Theme.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
                Grid.SetColumn(tag, 1);
                grid.Children.Add(tag);
            }
            else
            {
                var remove = new TextBlock
                {
                    Text = "",
                    FontFamily = Theme.IconFont,
                    FontSize = 10,
                    Foreground = Theme.Label,
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brushes.Transparent,
                    ToolTip = "从历史里删掉"
                };
                remove.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    var list = RemoteLogsHistory();
                    list.Remove(path);
                    settings.RemoteLogsHistory = string.Join("\n", list.ToArray());
                    try { settings.Save(); } catch { }
                    OpenRemoteHistory((FrameworkElement)remoteHistoryPopup.PlacementTarget);
                };
                Grid.SetColumn(remove, 1);
                grid.Children.Add(remove);
            }
            row.Child = grid;
            row.MouseEnter += delegate { if (!current) row.Background = Theme.RowHover; };
            row.MouseLeave += delegate { row.Background = current ? Theme.MenuHover : Brushes.Transparent; };
            row.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                setRemoteLogsBox.Text = path;
                remoteHistoryPopup.IsOpen = false;
                SetSettingsHint("已选好，点「保存」生效", Theme.Label);
            };
            return row;
        }

        void UpdatePathStats()
        {
            if (pathStats == null)
                return;
            var root = settings.LogsDir;
            pathStats.Text = "";
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            var gen = ++pathStatsGeneration;
            Task.Run(delegate
            {
                var snaps = 0;
                long bytes = 0;
                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        if (!SnapshotPattern.IsMatch(Path.GetFileName(dir)))
                            continue;
                        snaps++;
                        bytes += Paths.DirectorySize(dir);
                    }
                }
                catch { }
                var text = snaps == 0 ? "还没有拷贝过" : "已有 " + snaps.ToString(CultureInfo.InvariantCulture) + " 次拷贝 · " + Paths.FormatSize(bytes);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen == pathStatsGeneration && pathStats != null)
                        pathStats.Text = text;
                }));
            });
        }

        StackPanel BuildFeishuSettings()
        {
            var stack = new StackPanel { Visibility = Visibility.Collapsed, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left };
            var status = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            for (var i = 0; i < 3; i++)
            {
                status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 8, 12, 9),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0)
                };
                Theme.BindElement(cell, BackgroundProperty, "nn.RowHover");
                Theme.BindElement(cell, Border.BorderBrushProperty, "nn.Hairline");
                var inner = new StackPanel();
                var caption = new TextBlock { Text = i == 0 ? "lark-cli" : i == 1 ? "机器人" : "用户登录", FontSize = 11 };
                BindLabel(caption);
                inner.Children.Add(caption);
                var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                larkDots[i] = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                valueRow.Children.Add(larkDots[i]);
                larkValues[i] = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                BindInk(larkValues[i]);
                valueRow.Children.Add(larkValues[i]);
                inner.Children.Add(valueRow);
                cell.Child = inner;
                Grid.SetColumn(cell, i);
                status.Children.Add(cell);
            }
            stack.Children.Add(status);

            var creds = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            creds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            creds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            creds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var appCol = new StackPanel();
            appCol.Children.Add(FieldTitle("App ID"));
            setAppIdBox = FieldBox(settings.FeishuAppId);
            setAppIdBox.Margin = new Thickness(0);
            setAppIdBox.FontFamily = MonoFont;
            setAppIdBox.FontSize = 12;
            appCol.Children.Add(setAppIdBox);
            creds.Children.Add(appCol);
            var tokenCol = new StackPanel();
            tokenCol.Children.Add(FieldTitle("CLI Token"));
            var tokenHost = new Grid();
            setTokenBox = new PasswordBox
            {
                Height = 32,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            StylePassword(setTokenBox);
            setTokenBox.PasswordChanged += delegate { UpdateTokenPlaceholder(); };
            tokenHost.Children.Add(setTokenBox);
            tokenPlaceholder = new TextBlock { FontSize = 12, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            BindLabel(tokenPlaceholder);
            tokenPlaceholder.Opacity = 0.75;
            tokenHost.Children.Add(tokenPlaceholder);
            tokenCol.Children.Add(tokenHost);
            Grid.SetColumn(tokenCol, 2);
            creds.Children.Add(tokenCol);
            stack.Children.Add(creds);
            stack.Children.Add(FieldNote("App ID 和 Token 一起保存时，会写进本机飞书 CLI 的安全存储。Token 不会显示在窗口里。"));

            var chatTitle = FieldTitle("发给自己的会话");
            chatTitle.Margin = new Thickness(0, 6, 0, 3);
            stack.Children.Add(chatTitle);
            stack.Children.Add(FieldNote("截图和录屏都发到这里；会话里要有这个应用的机器人。"));
            setChatBox = FieldBox(settings.FeishuChatId);
            setChatBox.Margin = new Thickness(0, 0, 0, 14);
            setChatBox.FontFamily = MonoFont;
            setChatBox.FontSize = 12;
            stack.Children.Add(setChatBox);

            var help = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10)
            };
            Theme.BindElement(help, Border.BorderBrushProperty, "nn.Hairline");
            var helpStack = new StackPanel();
            var helpHead = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var helpTitle = new TextBlock { Text = "怎么拿到会话 ID", FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            BindInk(helpTitle);
            helpHead.Children.Add(helpTitle);
            var chevron = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            BindLabel(chevron);
            helpHead.Children.Add(chevron);
            helpStack.Children.Add(helpHead);
            var helpBody = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            helpBody.Children.Add(FieldNote("1. 飞书桌面端打开要发到的会话 → 右上角「更多」→ 复制链接"));
            helpBody.Children.Add(FieldNote("2. 取链接里 openChatId= 后面、以 oc_ 开头的那一段"));
            helpBody.Children.Add(FieldNote("也可以用 lark-cli 按会话名搜，结果里取 chat_id："));
            var cmdText = "lark-cli im +chat-search --query \"会话名\" --as bot";
            var cmd = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 10) };
            Theme.BindElement(cmd, BackgroundProperty, "nn.Field");
            var cmdRow = new Grid();
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cmdBlock = new TextBlock { Text = cmdText, FontFamily = MonoFont, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            BindInk(cmdBlock);
            cmdRow.Children.Add(cmdBlock);
            var copyText = new TextBlock { Text = "复制", FontSize = 11.5, Margin = new Thickness(6, 2, 6, 2) };
            copyText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Pink");
            var copy = new Button { Content = copyText, Cursor = Cursors.Hand, Focusable = false };
            copy.Template = FlatButtonTemplate(Brushes.Transparent, Theme.RowHover, 5);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(cmdText);
                    copyText.Text = "已复制";
                }
                catch
                {
                    copyText.Text = "复制失败";
                }
            };
            Grid.SetColumn(copy, 1);
            cmdRow.Children.Add(copy);
            cmd.Child = cmdRow;
            helpBody.Children.Add(cmd);
            helpBody.Children.Add(FieldNote("不要填别人的会话。「登录飞书 CLI」只申请基础消息权限，不会申请「以用户身份发送消息」；发送固定走机器人。"));
            helpStack.Children.Add(helpBody);
            helpHead.MouseLeftButtonUp += delegate
            {
                var show = helpBody.Visibility != Visibility.Visible;
                helpBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "" : "";
            };
            help.Child = helpStack;
            stack.Children.Add(help);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            var save = MakePrimary("保存");
            save.Click += delegate { SaveSettings(); };
            actions.Children.Add(save);
            var login = SmallButton("登录飞书 CLI");
            login.Margin = new Thickness(8, 0, 0, 0);
            login.Click += delegate { LoginFeishuCliAsync(); };
            actions.Children.Add(login);
            stack.Children.Add(actions);
            settingsHint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            BindLabel(settingsHint);
            stack.Children.Add(settingsHint);
            return stack;
        }

        void UpdateTokenPlaceholder()
        {
            if (tokenPlaceholder == null || setTokenBox == null)
                return;
            tokenPlaceholder.Text = string.IsNullOrWhiteSpace(settings.FeishuToken) ? "粘贴 tenant access token" : "已保存，留空不覆盖";
            tokenPlaceholder.Visibility = setTokenBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetSettingsHint(string text, Brush color)
        {
            var key = Theme.Key(color ?? Theme.Label) ?? "nn.Label";
            if (settingsHint != null)
            {
                settingsHint.Text = text ?? "";
                settingsHint.SetResourceReference(TextBlock.ForegroundProperty, key);
            }
            if (pathHint != null)
            {
                pathHint.Text = text ?? "";
                pathHint.SetResourceReference(TextBlock.ForegroundProperty, key);
            }
        }

        void SetLarkCell(int index, string text, string dotKey)
        {
            if (larkValues[index] == null)
                return;
            larkValues[index].Text = text;
            larkDots[index].SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dotKey);
        }

        void RefreshLarkStatus()
        {
            if (larkValues[0] == null)
                return;
            for (var i = 0; i < 3; i++)
                SetLarkCell(i, "检查中…", "nn.Label");
            var gen = ++larkStatusGeneration;
            Task.Run(delegate
            {
                var cli = FindLarkRunner();
                var bot = false;
                var user = false;
                var failed = false;
                if (cli != null)
                {
                    try
                    {
                        var result = RunLarkCli(cli, "auth status --json", null, false, 30000);
                        var json = ((result.Stdout ?? "") + "\n" + (result.Stderr ?? "")).Trim();
                        bot = IdentityReady(json, "bot");
                        user = IdentityReady(json, "user");
                    }
                    catch
                    {
                        failed = true;
                    }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen != larkStatusGeneration)
                        return;
                    if (cli == null)
                    {
                        SetLarkCell(0, "没找到", "nn.Red");
                        SetLarkCell(1, "—", "nn.Label");
                        SetLarkCell(2, "—", "nn.Label");
                        return;
                    }
                    SetLarkCell(0, "已找到", "nn.Ok");
                    if (failed)
                    {
                        SetLarkCell(1, "查不到状态", "nn.Orange");
                        SetLarkCell(2, "查不到状态", "nn.Orange");
                        return;
                    }
                    SetLarkCell(1, bot ? "可以发送" : "未就绪", bot ? "nn.Ok" : "nn.Orange");
                    SetLarkCell(2, user ? "已登录" : "未登录（不影响发送）", user ? "nn.Ok" : "nn.Label");
                }));
            });
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
            Theme.BindElement(settingsContentCard, BackgroundProperty, "nn.Card");
            ApplyWindowChrome();
            ApplyChromeLook();
            PaintNav(tabLogs, currentPage == "logs");
            PaintNav(tabMedia, currentPage == "media");
            PaintNav(tabApk, currentPage == "apk");
            PaintNav(navSettings, currentPage == "settings");
            PaintNav(navToggle, false);
            if (btnSetLook != null)
            {
                PaintSettingsNav(btnSetLook, panelSetLook != null && panelSetLook.Visibility == Visibility.Visible);
                PaintSettingsNav(btnSetPath, panelSetPath != null && panelSetPath.Visibility == Visibility.Visible);
                PaintSettingsNav(btnSetFeishu, panelSetFeishu != null && panelSetFeishu.Visibility == Visibility.Visible);
            }
            if (copyButton != null)
            {
                copyButton.SetResourceReference(Control.ForegroundProperty, "nn.OnAccent");
                copyButton.Template = FlatButtonTemplate(Theme.Pink, Theme.PinkPressed, 8);
            }
            if (sendButton != null)
            {
                sendButton.SetResourceReference(Control.ForegroundProperty, "nn.OnAccent");
                sendButton.Template = FlatButtonTemplate(Theme.Pink, Theme.PinkPressed, 8);
            }
            if (statusText != null && statusText.Text != null && Theme.Key(statusText.Foreground) == null)
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            RestyleSwitches();
            PaintSwatches();
            PaintSegments();
            if (pageLogs != null)
                pageLogs.Background = Brushes.Transparent;
            if (pageMedia != null)
                pageMedia.Background = Brushes.Transparent;
            if (pageSettings != null)
                pageSettings.Background = Brushes.Transparent;
            if (pageAbout != null)
                Theme.BindElement(pageAbout, BackgroundProperty, "nn.Bg");
            if (actionFooter != null)
            {
                var foot = actionFooter as Border;
                if (foot != null)
                    Theme.BindElement(foot, BackgroundProperty, "nn.Card");
            }
            if (deviceCard != null)
                Theme.BindElement(deviceCard, BackgroundProperty, "nn.Card");
            settings.Save();
        }

        void RestyleSwitches()
        {
            RestyleSwitch(chkFollowSystem);
            RestyleSwitch(chkThemeDark);
            RestyleSwitch(chkGlass);
            RestyleSwitch(chkApkUninstall);
            RestyleSwitch(chkApkInstallAll);
            RestyleSwitch(chkApkUseAdbInstall);
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
                Margin = new Thickness(40, 20, 40, 40)
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

            var versions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
            TextBlock current;
            var currentPill = Pill("当前 " + AppInfo.DisplayVersion, out current);
            txtAboutVersion = current;
            versions.Children.Add(currentPill);
            TextBlock remote;
            aboutRemotePill = Pill("", out remote);
            aboutRemoteText = remote;
            aboutRemotePill.Margin = new Thickness(8, 0, 0, 0);
            aboutRemotePill.Visibility = Visibility.Collapsed;
            versions.Children.Add(aboutRemotePill);
            btnAboutUpdate = MakePrimary("更新并重启");
            btnAboutUpdate.Margin = new Thickness(12, 0, 0, 0);
            btnAboutUpdate.Visibility = Visibility.Collapsed;
            btnAboutUpdate.Click += delegate { StartAboutUpdate(); };
            versions.Children.Add(btnAboutUpdate);
            mid.Children.Add(versions);
            page.Children.Add(mid);
            return page;
        }

        void BeginAboutUpdateCheck()
        {
            if (btnAboutUpdate != null)
                btnAboutUpdate.Visibility = Visibility.Collapsed;
            pendingUpdate = null;
            if (txtAboutVersion != null)
                txtAboutVersion.Text = "当前 " + AppInfo.DisplayVersion;
            if (aboutRemotePill != null)
            {
                aboutRemoteText.Text = "正在检查更新…";
                PaintPill(aboutRemotePill, aboutRemoteText, "nn.Label", "nn.Chip");
                aboutRemotePill.Visibility = Visibility.Visible;
            }
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
                        if (aboutRemotePill != null)
                            aboutRemotePill.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (!GitHubUpdater.IsNewer(info.Version, AppInfo.Version))
                    {
                        WhatHappened.Info("关于页检查更新：已是最新 local=" + AppInfo.Version + " remote=" + info.Version);
                        if (aboutRemotePill != null)
                        {
                            aboutRemoteText.Text = "已是最新";
                            PaintPill(aboutRemotePill, aboutRemoteText, "nn.Ok", "nn.Chip");
                        }
                        return;
                    }
                    pendingUpdate = info;
                    var tag = string.IsNullOrEmpty(info.Tag) ? info.Version : info.Tag;
                    if (aboutRemotePill != null)
                    {
                        aboutRemoteText.Text = "GitHub 有 " + tag;
                        PaintPill(aboutRemotePill, aboutRemoteText, "nn.Pink", "nn.AccentSoft");
                    }
                    if (btnAboutUpdate != null)
                        btnAboutUpdate.Visibility = Visibility.Visible;
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
                    SetStatus("更新已就绪，正在重启…", Theme.Ok);
                    WhatHappened.Info("自更新脚本已启动，准备退出");
                    Close();
                }));
            });
        }

        Grid RevealLine(string text)
        {
            // 悬停揭示：用模糊代替实色遮罩，开毛玻璃时不会露出深色方块。
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var line = new TextBlock
            {
                Text = text,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.55,
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 }
            };
            BindInk(line);
            grid.Children.Add(line);
            grid.MouseEnter += delegate
            {
                line.Effect = null;
                line.Opacity = 1;
            };
            grid.MouseLeave += delegate
            {
                line.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
                line.Opacity = 0.55;
            };
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

        static void StylePassword(PasswordBox box)
        {
            box.Template = PasswordTemplate();
            box.SetResourceReference(Control.BackgroundProperty, "nn.Field");
            box.SetResourceReference(Control.BorderBrushProperty, "nn.Hairline");
            box.SetResourceReference(Control.ForegroundProperty, "nn.Ink");
            box.SetResourceReference(PasswordBox.CaretBrushProperty, "nn.Pink");
        }

        // PasswordBox 原来用系统默认模板（直角、系统边框），和其他输入框不一致。
        static ControlTemplate PasswordTemplate()
        {
            var template = new ControlTemplate(typeof(PasswordBox));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
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
                    SetSettingsHint("目录不能用：" + ex.Message, Theme.Red);
                    return;
                }
                settings.LogsDir = logs;
                logsBrowsePath = logs;
                RefreshLogsList();
                UpdatePathStats();
            }
            var remote = Adb.NormalizeRemoteLogs(setRemoteLogsBox.Text);
            settings.RemoteLogsDir = remote == Adb.RemoteLogs ? "" : remote;
            RememberRemoteLogs(remote);
            setRemoteLogsBox.Text = remote;
            if (remoteSourceText != null)
                remoteSourceText.Text = "来自 " + remote;
            settings.FeishuAppId = setAppIdBox.Text == null ? "" : setAppIdBox.Text.Trim();
            settings.FeishuChatId = setChatBox.Text == null ? "" : setChatBox.Text.Trim();
            var token = setTokenBox.Password == null ? "" : setTokenBox.Password.Trim();
            if (token.Length > 0)
                settings.FeishuToken = token;
            settings.Save();
            if (token.Length == 0)
            {
                SetStatus("设置已保存", Theme.Ok);
                SetSettingsHint("已保存", Theme.Ok);
                return;
            }
            setTokenBox.Password = "";
            UpdateTokenPlaceholder();
            if (string.IsNullOrWhiteSpace(settings.FeishuAppId))
            {
                SetStatus("Token 已记在本地。写入飞书 CLI 还需要 App ID。", Theme.Orange);
                SetSettingsHint("Token 已记在本地。写入飞书 CLI 还需要 App ID。", Theme.Orange);
                return;
            }
            try
            {
                StoreToken(settings.FeishuAppId, token);
                SetStatus("设置已保存，Token 已写入飞书 CLI", Theme.Ok);
                SetSettingsHint("已保存，Token 已写入飞书 CLI", Theme.Ok);
                RefreshLarkStatus();
            }
            catch (Exception ex)
            {
                SetStatus("设置已保存，但写入飞书 CLI 失败：" + ex.Message, Theme.Orange);
                SetSettingsHint("已保存，但写入飞书 CLI 失败：" + ex.Message, Theme.Orange);
            }
        }

        void FitMediaTiles()
        {
            if (mediaTiles == null)
                return;
            var width = mediaTiles.ActualWidth;
            if (width < 200)
                return;
            if (Math.Abs(width - lastFitWidth) < 0.5)
                return;
            lastFitWidth = width;
            var cols = Math.Max(2, (int)Math.Floor((width + MediaGap) / (176 + MediaGap)));
            var tileWidth = Math.Floor((width - MediaGap * (cols - 1)) / cols);
            // 20:9 横屏框；ring 两侧各占 4px（边框 2 + 内边距 2）。
            var frameHeight = Math.Round((tileWidth - 8) * 9 / 20);
            foreach (var tile in mediaTileCards)
                tile.Width = tileWidth;
            foreach (var frame in mediaTileFrames)
                frame.Height = frameHeight;
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
            return ExtractVideoFrame(video, jpg, false);
        }

        static bool ExtractVideoFrame(string video, string jpg, bool nativeSize)
        {
            var ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
                return false;
            if (RunFfmpeg(ffmpeg, video, jpg, "1", nativeSize))
                return File.Exists(jpg);
            return RunFfmpeg(ffmpeg, video, jpg, "0", nativeSize) && File.Exists(jpg);
        }

        static bool RunFfmpeg(string ffmpeg, string video, string jpg, string seconds, bool nativeSize)
        {
            var vf = nativeSize ? "" : " -vf scale=360:-2";
            var psi = new ProcessStartInfo();
            psi.FileName = ffmpeg;
            psi.Arguments = "-y -ss " + seconds + " -i \"" + video + "\" -frames:v 1" + vf + " -q:v 3 \"" + jpg + "\"";
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
            return ShellThumb(path, width, height, 0x1);
        }

        BitmapSource ShellThumb(string path, int width, int height, int flags)
        {
            BitmapSource shot = null;
            Dispatcher.Invoke(new Action(delegate
            {
                shot = TryShellThumb(path, width, height, flags);
            }));
            return shot;
        }

        static BitmapSource TryShellThumb(string path, int width, int height, int flags)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                ThumbNative.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                var size = new ThumbSize { cx = width, cy = height };
                IntPtr hbmp;
                var hr = factory.GetImage(size, flags, out hbmp);
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
            if (previewLayer == null || selected == null || adbExe == null || item == null)
                return;
            previewToken++;
            var token = previewToken;
            previewItem = item;
            previewLayer.Visibility = Visibility.Visible;
            previewTitle.Text = item.Name;
            previewMeta.Text = KindLabel(item) + " · " + Paths.FormatSize(item.Size) + (string.IsNullOrEmpty(item.DateText) ? "" : " · " + FormatMediaDate(item.DateText));
            PaintPreviewCheck();
            var index = mediaOrdered.IndexOf(item);
            previewPrev.IsEnabled = index > 0;
            previewNext.IsEnabled = index >= 0 && index < mediaOrdered.Count - 1;
            previewImage.Source = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewVideo.Stop();
            previewVideo.Source = null;
            previewVideo.Visibility = Visibility.Collapsed;
            previewHint.Text = "正在准备预览…";
            previewHint.Visibility = Visibility.Visible;
            UpdateMediaCount();
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

        void PreviewStep(int delta)
        {
            if (previewItem == null)
                return;
            var index = mediaOrdered.IndexOf(previewItem);
            if (index < 0)
                return;
            var next = index + delta;
            if (next < 0 || next >= mediaOrdered.Count)
                return;
            OpenPreview(mediaOrdered[next]);
        }

        void PreviewToggleCheck()
        {
            var item = previewItem;
            if (item == null)
                return;
            item.IsChecked = !item.IsChecked;
            if (item.CheckHost != null)
                item.CheckHost.IsChecked = item.IsChecked;
            PaintPreviewCheck();
            UpdateMediaCount();
        }

        void PaintPreviewCheck()
        {
            if (previewCheck == null)
                return;
            var on = previewItem != null && previewItem.IsChecked;
            if (on)
            {
                previewCheck.SetResourceReference(Border.BackgroundProperty, "nn.Pink");
                previewCheck.SetResourceReference(Border.BorderBrushProperty, "nn.Pink");
            }
            else
            {
                previewCheck.Background = B("#66000000");
                previewCheck.BorderBrush = B("#E6FFFFFF");
            }
            if (previewCheckText != null)
                previewCheckText.Text = on ? "已勾选" : "勾选";
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
            previewItem = null;
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
            var mask = new Grid { Background = B("#B3000000") };
            mask.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == mask)
                    ClosePreview();
            };
            var card = new Border
            {
                Margin = new Thickness(48, 28, 48, 36),
                MaxWidth = 1100,
                MaxHeight = 760,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12)
            };
            Theme.BindElement(card, BackgroundProperty, "nn.Menu");
            Theme.BindElement(card, Border.BorderBrushProperty, "nn.Hairline");
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(2, 0, 0, 10) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var checkRow = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            previewCheck = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            checkRow.Children.Add(previewCheck);
            previewCheckText = new TextBlock { FontSize = 12, Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            BindLabel(previewCheckText);
            checkRow.Children.Add(previewCheckText);
            checkRow.MouseLeftButtonUp += delegate { PreviewToggleCheck(); };
            head.Children.Add(checkRow);
            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            previewTitle = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = MonoFont,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            BindInk(previewTitle);
            titles.Children.Add(previewTitle);
            TextBlock metaText;
            var meta = Pill("", out metaText);
            meta.Margin = new Thickness(10, 0, 0, 0);
            previewMeta = metaText;
            titles.Children.Add(meta);
            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            previewPrev = IconButton("", "上一张（←）");
            previewPrev.Click += delegate { PreviewStep(-1); };
            nav.Children.Add(previewPrev);
            previewNext = IconButton("", "下一张（→）");
            previewNext.Margin = new Thickness(6, 0, 0, 0);
            previewNext.Click += delegate { PreviewStep(1); };
            nav.Children.Add(previewNext);
            var close = IconButton("", "关闭（Esc）");
            close.Margin = new Thickness(6, 0, 0, 0);
            close.Click += delegate { ClosePreview(); };
            nav.Children.Add(close);
            Grid.SetColumn(nav, 2);
            head.Children.Add(nav);
            grid.Children.Add(head);

            var stage = new Grid { Background = Brushes.Black };
            ClipRound(stage, 8);
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

            var foot = new Grid { Margin = new Thickness(2, 10, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var keys = new TextBlock { Text = "← → 切换 · 空格 勾选 · 点视频暂停 · Esc 关闭", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            BindLabel(keys);
            foot.Children.Add(keys);
            previewSend = MakePrimary("发给自己");
            previewSend.Click += delegate
            {
                ClosePreview();
                SendSelectedAsync();
            };
            Grid.SetColumn(previewSend, 1);
            foot.Children.Add(previewSend);
            Grid.SetRow(foot, 2);
            grid.Children.Add(foot);
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
