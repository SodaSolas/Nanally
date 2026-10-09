using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Nanally
{
    public static class Program
    {
        const string RemoteLogs = "/sdcard/Android/data/com.piegame.bd/files/Logs";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            SetProcessDPIAware();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
                return SelfTest();

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Theme.Bootstrap(app.Resources);
            WhatHappened.Init(Paths.ToolRoot());
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                WhatHappened.Error("UI 未处理异常: " + (e.Exception == null ? "?" : e.Exception.ToString()));
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                WhatHappened.Error("进程未处理异常: " + (e.ExceptionObject == null ? "?" : e.ExceptionObject.ToString()));
            };
            app.Run(new MainWindow());
            WhatHappened.Info("Nanally 退出");
            return 0;
        }

        static int SelfTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "nanally-selftest-" + Guid.NewGuid().ToString("N"));
            var errors = new List<string>();
            try
            {
                var safe = Paths.SafeName("Pixel / 6:test?");
                if (safe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || safe.IndexOf('/') >= 0 || safe.IndexOf(':') >= 0)
                    errors.Add("safe name still has invalid chars: " + safe);

                var src = Path.Combine(root, "src");
                Directory.CreateDirectory(Path.Combine(src, "session"));
                File.WriteAllText(Path.Combine(src, "a.log"), "hello", Encoding.UTF8);
                File.WriteAllText(Path.Combine(src, "session", "b.log"), "world", Encoding.UTF8);

                var logs = Path.Combine(root, "logs");
                Directory.CreateDirectory(logs);
                File.WriteAllText(Path.Combine(logs, "keep.txt"), "keep", Encoding.UTF8);
                var snap = Paths.CreateSnapshotDir(logs, "Pixel / 6");
                int n2 = Paths.CopyContents(src, snap);
                if (n2 != 2)
                    errors.Add("file count " + n2);
                if (!snap.StartsWith(logs, StringComparison.OrdinalIgnoreCase))
                    errors.Add("snapshot not under logs: " + snap);
                if (!File.Exists(Path.Combine(logs, "keep.txt")))
                    errors.Add("logs root was cleared");
                if (File.Exists(Path.Combine(logs, "a.log")))
                    errors.Add("files were dumped into logs root");
                if (!File.Exists(Path.Combine(snap, "session", "b.log")))
                    errors.Add("copied files missing");
                if (Path.GetFileName(snap).IndexOf('+') < 0)
                    errors.Add("snapshot name missing +: " + snap);
                if (!Regex.IsMatch(Path.GetFileName(snap), @"\+\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$"))
                    errors.Add("snapshot date format: " + Path.GetFileName(snap));
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }

            var msg = errors.Count == 0 ? "PASS" : "FAIL\n" + string.Join("\n", errors.ToArray());
            try { Console.WriteLine(msg); } catch { }
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "nanally-selftest.txt"), msg, Encoding.UTF8);
            }
            catch { }
            return errors.Count == 0 ? 0 : 1;
        }
    }

    static class Theme
    {
        public static SolidColorBrush Bg;
        public static SolidColorBrush Sidebar;
        public static SolidColorBrush Card;
        public static SolidColorBrush CardSoft;
        public static SolidColorBrush Menu;
        public static SolidColorBrush Ink;
        public static SolidColorBrush Label;
        public static SolidColorBrush FieldLabel;
        public static SolidColorBrush Pink;
        public static SolidColorBrush PinkPressed;
        public static SolidColorBrush PinkSoft;
        public static SolidColorBrush AccentSoft;
        public static SolidColorBrush NavHover;
        public static SolidColorBrush Stroke;
        public static SolidColorBrush Orange;
        public static SolidColorBrush Red;
        public static SolidColorBrush Hairline;
        public static SolidColorBrush Chip;
        public static SolidColorBrush ChipHover;
        public static SolidColorBrush MenuHover;
        public static SolidColorBrush RowHover;
        public static SolidColorBrush Selected;
        public static SolidColorBrush Field;
        public static SolidColorBrush OnAccent;
        public static SolidColorBrush Thumb;
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");
        public static bool Dark;

        // Like Linko ThemeManager.EnsureMutableBrushes: create mutable brushes before any window freezes them.
        // Default bootstrap matches GlassWidgetStyle light (white / ice-blue).
        public static void Bootstrap(ResourceDictionary dict)
        {
            if (dict == null)
                return;
            Bg = Paint("#E6F2F5F8");
            Sidebar = Paint("#66FFFFFF");
            Card = Paint("#B8FFFFFF");
            CardSoft = Paint("#8CFFFFFF");
            Menu = Paint("#F2FFFFFF");
            Ink = Paint("#1C2D42");
            Label = Paint("#6B7C8F");
            FieldLabel = Paint("#6B7C8F");
            Pink = Paint("#4A7FE5");
            PinkPressed = Paint("#5B8FEA");
            PinkSoft = Paint("#4A7FE5");
            AccentSoft = Paint("#334A7FE5");
            NavHover = Paint("#334A7FE5");
            Stroke = Paint("#B3A8B8C8");
            Orange = Paint("#D97706");
            Red = Paint("#E86A6A");
            Hairline = Paint("#99FFFFFF");
            Chip = Paint("#99FFFFFF");
            ChipHover = Paint("#E6E4E0F5");
            MenuHover = Paint("#334A7FE5");
            RowHover = Paint("#8CFFFFFF");
            Selected = Paint("#334A7FE5");
            Field = Paint("#F2FFFFFF");
            OnAccent = Paint("#FFFFFF");
            Thumb = Paint("#99A0B0C0");
            Mount(dict);
        }

        public static void Apply(bool dark, string accent)
        {
            Dark = dark;
            if (dark)
            {
                // Dark frosted glass (translucent charcoal over Acrylic; not opaque Linko slabs)
                Set(ref Bg, "nn.Bg", "#E6121212");
                Set(ref Sidebar, "nn.Sidebar", "#66101010");
                Set(ref Card, "nn.Card", "#B81C1C1C");
                Set(ref CardSoft, "nn.CardSoft", "#8C222222");
                Set(ref Menu, "nn.Menu", "#F21A1A1A");
                Set(ref Ink, "nn.Ink", "#EEEEEE");
                Set(ref Label, "nn.Label", "#9AA0A6");
                Set(ref FieldLabel, "nn.FieldLabel", "#B0B0B0");
                Set(ref Stroke, "nn.Stroke", "#40FFFFFF");
                Set(ref Hairline, "nn.Hairline", "#33FFFFFF");
                Set(ref Field, "nn.Field", "#F2181818");
                Set(ref Chip, "nn.Chip", "#33FFFFFF");
                Set(ref ChipHover, "nn.ChipHover", "#40FFFFFF");
                Set(ref Thumb, "nn.Thumb", "#66A0A0A0");
                Set(ref RowHover, "nn.RowHover", "#28FFFFFF");
                Set(ref Red, "nn.Red", "#F14C4C");
                Set(ref Orange, "nn.Orange", "#FFB020");
            }
            else
            {
                // GlassWidgetStyle light palette (D:\tools\GlassWidgetStyle\README.md)
                Set(ref Bg, "nn.Bg", "#E6F2F5F8");
                Set(ref Sidebar, "nn.Sidebar", "#66FFFFFF");
                Set(ref Card, "nn.Card", "#B8FFFFFF");
                Set(ref CardSoft, "nn.CardSoft", "#8CFFFFFF");
                Set(ref Menu, "nn.Menu", "#F2FFFFFF");
                Set(ref Ink, "nn.Ink", "#1C2D42");
                Set(ref Label, "nn.Label", "#6B7C8F");
                Set(ref FieldLabel, "nn.FieldLabel", "#6B7C8F");
                Set(ref Stroke, "nn.Stroke", "#B3A8B8C8");
                Set(ref Hairline, "nn.Hairline", "#99FFFFFF");
                Set(ref Field, "nn.Field", "#F2FFFFFF");
                Set(ref Chip, "nn.Chip", "#99FFFFFF");
                Set(ref ChipHover, "nn.ChipHover", "#E6E4E0F5");
                Set(ref Thumb, "nn.Thumb", "#99A0B0C0");
                Set(ref RowHover, "nn.RowHover", "#8CFFFFFF");
                Set(ref Red, "nn.Red", "#E86A6A");
                Set(ref Orange, "nn.Orange", "#D97706");
            }
            var pink = accent == "Blue" ? (dark ? "#3794FF" : "#4A7FE5") : accent == "Green" ? (dark ? "#22C55E" : "#16A34A") : (dark ? "#EC4899" : "#DB2777");
            var hover = accent == "Blue" ? (dark ? "#4DAAF8" : "#5B8FEA") : accent == "Green" ? (dark ? "#4ADE80" : "#15803D") : (dark ? "#F472B6" : "#BE185D");
            var soft = accent == "Blue" ? (dark ? "#403794FF" : "#334A7FE5") : accent == "Green" ? (dark ? "#4022C55E" : "#3316A34A") : (dark ? "#40EC4899" : "#33DB2777");
            var nav = accent == "Blue" ? (dark ? "#333794FF" : "#334A7FE5") : accent == "Green" ? (dark ? "#3322C55E" : "#2216A34A") : (dark ? "#33EC4899" : "#22DB2777");
            Set(ref Pink, "nn.Pink", pink);
            Set(ref PinkPressed, "nn.PinkPressed", hover);
            Set(ref AccentSoft, "nn.AccentSoft", soft);
            Set(ref NavHover, "nn.NavHover", nav);
            Set(ref MenuHover, "nn.MenuHover", nav);
            Set(ref Selected, "nn.Selected", soft);
            Set(ref PinkSoft, "nn.PinkSoft", dark ? "#FFC2DC" : pink);
            Set(ref OnAccent, "nn.OnAccent", "#FFFFFF");
        }

        public static bool SystemIsDark()
        {
            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key == null)
                    return true;
                var value = key.GetValue("AppsUseLightTheme");
                key.Close();
                if (value is int)
                    return (int)value == 0;
                if (value is long)
                    return (long)value == 0;
            }
            catch { }
            return true;
        }

        public static void Mount(ResourceDictionary dict)
        {
            if (dict == null)
                return;
            Put(dict, "nn.Bg", Bg);
            Put(dict, "nn.Sidebar", Sidebar);
            Put(dict, "nn.Card", Card);
            Put(dict, "nn.CardSoft", CardSoft);
            Put(dict, "nn.Menu", Menu);
            Put(dict, "nn.Ink", Ink);
            Put(dict, "nn.Label", Label);
            Put(dict, "nn.FieldLabel", FieldLabel);
            Put(dict, "nn.Pink", Pink);
            Put(dict, "nn.PinkPressed", PinkPressed);
            Put(dict, "nn.PinkSoft", PinkSoft);
            Put(dict, "nn.AccentSoft", AccentSoft);
            Put(dict, "nn.NavHover", NavHover);
            Put(dict, "nn.Stroke", Stroke);
            Put(dict, "nn.Orange", Orange);
            Put(dict, "nn.Red", Red);
            Put(dict, "nn.Hairline", Hairline);
            Put(dict, "nn.Chip", Chip);
            Put(dict, "nn.ChipHover", ChipHover);
            Put(dict, "nn.MenuHover", MenuHover);
            Put(dict, "nn.RowHover", RowHover);
            Put(dict, "nn.Selected", Selected);
            Put(dict, "nn.Field", Field);
            Put(dict, "nn.OnAccent", OnAccent);
            Put(dict, "nn.Thumb", Thumb);
            InstallScrollBars(dict);
        }

        public static void Bind(FrameworkElementFactory factory, DependencyProperty property, Brush brush)
        {
            var key = Key(brush);
            if (key != null)
                factory.SetResourceReference(property, key);
            else if (brush != null)
                factory.SetValue(property, brush);
        }

        public static void BindElement(FrameworkElement element, DependencyProperty property, string key)
        {
            if (element != null && key != null)
                element.SetResourceReference(property, key);
        }

        public static Setter Dyn(DependencyProperty property, Brush brush, string targetName)
        {
            var key = Key(brush);
            if (key != null)
                return new Setter(property, new DynamicResourceExtension(key), targetName);
            return new Setter(property, brush, targetName);
        }

        public static object Ref(Brush brush)
        {
            var key = Key(brush);
            if (key != null)
                return new DynamicResourceExtension(key);
            return brush;
        }

        public static string Key(Brush brush)
        {
            if (ReferenceEquals(brush, Bg)) return "nn.Bg";
            if (ReferenceEquals(brush, Sidebar)) return "nn.Sidebar";
            if (ReferenceEquals(brush, Card)) return "nn.Card";
            if (ReferenceEquals(brush, CardSoft)) return "nn.CardSoft";
            if (ReferenceEquals(brush, Menu)) return "nn.Menu";
            if (ReferenceEquals(brush, Ink)) return "nn.Ink";
            if (ReferenceEquals(brush, Label)) return "nn.Label";
            if (ReferenceEquals(brush, FieldLabel)) return "nn.FieldLabel";
            if (ReferenceEquals(brush, Pink)) return "nn.Pink";
            if (ReferenceEquals(brush, PinkPressed)) return "nn.PinkPressed";
            if (ReferenceEquals(brush, PinkSoft)) return "nn.PinkSoft";
            if (ReferenceEquals(brush, AccentSoft)) return "nn.AccentSoft";
            if (ReferenceEquals(brush, NavHover)) return "nn.NavHover";
            if (ReferenceEquals(brush, Stroke)) return "nn.Stroke";
            if (ReferenceEquals(brush, Orange)) return "nn.Orange";
            if (ReferenceEquals(brush, Red)) return "nn.Red";
            if (ReferenceEquals(brush, Hairline)) return "nn.Hairline";
            if (ReferenceEquals(brush, Chip)) return "nn.Chip";
            if (ReferenceEquals(brush, ChipHover)) return "nn.ChipHover";
            if (ReferenceEquals(brush, MenuHover)) return "nn.MenuHover";
            if (ReferenceEquals(brush, RowHover)) return "nn.RowHover";
            if (ReferenceEquals(brush, Selected)) return "nn.Selected";
            if (ReferenceEquals(brush, Field)) return "nn.Field";
            if (ReferenceEquals(brush, OnAccent)) return "nn.OnAccent";
            if (ReferenceEquals(brush, Thumb)) return "nn.Thumb";
            return null;
        }

        static void InstallScrollBars(ResourceDictionary dict)
        {
            if (dict.Contains(typeof(ScrollBar)))
                return;
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(Control.OverridesDefaultStyleProperty, true));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 8.0));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, ScrollTemplate(true)));
            var horiz = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horiz.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
            horiz.Setters.Add(new Setter(FrameworkElement.HeightProperty, 8.0));
            horiz.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 8.0));
            horiz.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2)));
            horiz.Setters.Add(new Setter(Control.TemplateProperty, ScrollTemplate(false)));
            style.Triggers.Add(horiz);
            dict[typeof(ScrollBar)] = style;
        }

        static ControlTemplate ScrollTemplate(bool vertical)
        {
            var track = vertical
                ? "<Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\"><Track.DecreaseRepeatButton>" + PageButton("ScrollBar.PageUpCommand") + "</Track.DecreaseRepeatButton><Track.Thumb>" + ThumbBox() + "</Track.Thumb><Track.IncreaseRepeatButton>" + PageButton("ScrollBar.PageDownCommand") + "</Track.IncreaseRepeatButton></Track>"
                : "<Track x:Name=\"PART_Track\"><Track.DecreaseRepeatButton>" + PageButton("ScrollBar.PageLeftCommand") + "</Track.DecreaseRepeatButton><Track.Thumb>" + ThumbBox() + "</Track.Thumb><Track.IncreaseRepeatButton>" + PageButton("ScrollBar.PageRightCommand") + "</Track.IncreaseRepeatButton></Track>";
            var margin = vertical ? "2,2,2,2" : "2,2,2,2";
            var xaml = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollBar\"><Grid Margin=\"" + margin + "\"><Border Background=\"{DynamicResource nn.CardSoft}\" CornerRadius=\"4\" Opacity=\"0.55\"/>" + track + "</Grid></ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        static string PageButton(string command)
        {
            return "<RepeatButton Command=\"" + command + "\" OverridesDefaultStyle=\"True\" IsTabStop=\"False\" Focusable=\"False\"><RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template></RepeatButton>";
        }

        static string ThumbBox()
        {
            return "<Thumb OverridesDefaultStyle=\"True\" IsTabStop=\"False\"><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border x:Name=\"thumb\" Background=\"{DynamicResource nn.Thumb}\" CornerRadius=\"3\" Margin=\"0\"/><ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"thumb\" Property=\"Background\" Value=\"{DynamicResource nn.Label}\"/></Trigger><Trigger Property=\"IsDragging\" Value=\"True\"><Setter TargetName=\"thumb\" Property=\"Background\" Value=\"{DynamicResource nn.Pink}\"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb>";
        }

        static void Put(ResourceDictionary dict, string key, Brush brush)
        {
            if (dict != null && key != null && brush != null)
                dict[key] = brush;
        }

        // Like Linko ThemeManager.Set: mutate if mutable, otherwise replace dictionary + field.
        // Shared brushes must stay unfrozen; if something froze them, replace so DynamicResource can refresh.
        static void Set(ref SolidColorBrush field, string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (field != null && !field.IsFrozen)
            {
                field.Color = color;
                if (Application.Current != null)
                    Application.Current.Resources[key] = field;
                return;
            }
            field = new SolidColorBrush(color);
            if (Application.Current != null)
                Application.Current.Resources[key] = field;
        }

        static SolidColorBrush Paint(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    static class AppInfo
    {
        public const string Version = "1.0.2";
        public const string DisplayVersion = "v1.0.2";
        /// <summary>默认更新源：GitHub owner/repo，对应 Releases。</summary>
        public const string DefaultUpdateRepo = "SodaSolas/Nanally";
    }

    sealed class UpdateInfo
    {
        public string Version;
        public string Tag;
        public string DownloadUrl;
        public string Notes;
    }

    static class GitHubUpdater
    {
        public static UpdateInfo CheckLatest(string ownerRepo, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(ownerRepo) || ownerRepo.IndexOf('/') < 0)
            {
                error = "更新仓库地址无效";
                return null;
            }
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch { }
            var url = "https://api.github.com/repos/" + ownerRepo.Trim().Trim('/') + "/releases/latest";
            string json;
            try
            {
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] = "Nanally/" + AppInfo.Version;
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = client.DownloadString(url);
                }
            }
            catch (Exception ex)
            {
                error = "无法访问 GitHub：" + ex.Message;
                return null;
            }
            if (string.IsNullOrEmpty(json))
            {
                error = "GitHub 返回空内容";
                return null;
            }
            var tag = MatchJsonString(json, "tag_name");
            if (string.IsNullOrEmpty(tag))
            {
                error = "没有找到最新 Release";
                return null;
            }
            var version = tag.Trim();
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase) || version.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                version = version.Substring(1);
            var download = FindAssetUrl(json, "Nanally.exe");
            if (string.IsNullOrEmpty(download))
            {
                error = "最新 Release 里没有 Nanally.exe";
                return null;
            }
            var info = new UpdateInfo();
            info.Tag = tag;
            info.Version = version;
            info.DownloadUrl = download;
            info.Notes = MatchJsonString(json, "body");
            return info;
        }

        public static bool IsNewer(string remoteVersion, string localVersion)
        {
            var a = ParseVersion(remoteVersion);
            var b = ParseVersion(localVersion);
            for (var i = 0; i < 4; i++)
            {
                if (a[i] > b[i])
                    return true;
                if (a[i] < b[i])
                    return false;
            }
            return false;
        }

        public static void DownloadAndApply(string downloadUrl, string toolRoot, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(toolRoot))
            {
                error = "更新地址无效";
                return;
            }
            var updateExe = Path.Combine(toolRoot, "Nanally.update.exe");
            var bat = Path.Combine(toolRoot, "nanally-apply-update.cmd");
            try
            {
                if (File.Exists(updateExe))
                    File.Delete(updateExe);
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Nanally/" + AppInfo.Version;
                    client.DownloadFile(downloadUrl, updateExe);
                }
                if (!File.Exists(updateExe) || new FileInfo(updateExe).Length < 1024)
                {
                    error = "下载的更新文件无效";
                    return;
                }
                var script =
                    "@echo off\r\n"
                    + "setlocal\r\n"
                    + "cd /d \"%~dp0\"\r\n"
                    + "echo Applying Nanally update...\r\n"
                    + ":wait\r\n"
                    + "ping 127.0.0.1 -n 2 >nul\r\n"
                    + "tasklist /FI \"IMAGENAME eq Nanally.exe\" | find /I \"Nanally.exe\" >nul\r\n"
                    + "if not errorlevel 1 goto wait\r\n"
                    + "copy /Y \"Nanally.update.exe\" \"Nanally.exe\" >nul\r\n"
                    + "if errorlevel 1 (\r\n"
                    + "  echo Update failed.\r\n"
                    + "  pause\r\n"
                    + "  exit /b 1\r\n"
                    + ")\r\n"
                    + "del /F /Q \"Nanally.update.exe\" >nul 2>nul\r\n"
                    + "start \"\" \"Nanally.exe\"\r\n"
                    + "del /F /Q \"%~f0\"\r\n";
                File.WriteAllText(bat, script, Encoding.ASCII);
                var psi = new ProcessStartInfo();
                psi.FileName = bat;
                psi.WorkingDirectory = toolRoot;
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                error = "下载或准备更新失败：" + ex.Message;
                try { if (File.Exists(updateExe)) File.Delete(updateExe); } catch { }
            }
        }

        static int[] ParseVersion(string text)
        {
            var parts = new int[4];
            if (string.IsNullOrWhiteSpace(text))
                return parts;
            var t = text.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase) || t.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(1);
            var bits = t.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length && i < bits.Length; i++)
            {
                int n;
                if (int.TryParse(bits[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    parts[i] = n;
            }
            return parts;
        }

        static string MatchJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return null;
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                return null;
            return UnescapeJson(m.Groups[1].Value);
        }

        static string FindAssetUrl(string json, string fileName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fileName))
                return null;
            // 在 assets 数组里找 name 匹配的 browser_download_url
            var assets = Regex.Match(json, "\"assets\"\\s*:\\s*\\[(.*)\\]", RegexOptions.Singleline);
            var blob = assets.Success ? assets.Groups[1].Value : json;
            var pattern = "\"name\"\\s*:\\s*\"" + Regex.Escape(fileName) + "\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"";
            var m = Regex.Match(blob, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups[1].Value.Replace("\\u0026", "&");
            // 宽松：任意 browser_download_url 以 Nanally.exe 结尾
            m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*" + Regex.Escape(fileName) + ")\"", RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups[1].Value.Replace("\\u0026", "&");
            return null;
        }

        static string UnescapeJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return text.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    /// <summary>
    /// Nanally 自身运营日志。落盘目录：工具根下的 whathappened\，不是 logs\。
    /// logs\ 只给「提取日志」拉下来的游戏 Logs 用。
    /// </summary>
    static class WhatHappened
    {
        static readonly object Gate = new object();
        static string dir;
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static void Init(string toolRoot)
        {
            dir = Path.Combine(string.IsNullOrEmpty(toolRoot) ? Paths.ToolRoot() : toolRoot, "whathappened");
            try { Directory.CreateDirectory(dir); }
            catch { }
            EnsureReadme();
            Info("Nanally 启动 toolRoot=" + Paths.ToolRoot());
        }

        public static string Dir()
        {
            if (string.IsNullOrEmpty(dir))
                Init(Paths.ToolRoot());
            return dir;
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        static void Write(string level, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(dir))
                    Init(Paths.ToolRoot());
                if (string.IsNullOrEmpty(dir))
                    return;
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " [" + level + "] "
                    + (message ?? "")
                    + Environment.NewLine;
                var file = Path.Combine(dir, "nanally-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                lock (Gate)
                {
                    File.AppendAllText(file, line, Utf8Bom);
                }
            }
            catch { }
        }

        static void EnsureReadme()
        {
            try
            {
                var readme = Path.Combine(dir, "README.md");
                if (File.Exists(readme))
                    return;
                var text =
                    "# whathappened\n\n"
                    + "这里是 **Nanally 工具自己的运营日志**（排查「安装失败 / 发飞书失败 / 找不到设备」等）。\n\n"
                    + "## 和 `logs` 的区别（重要）\n\n"
                    + "| 目录 | 存什么 | 谁用 |\n"
                    + "|---|---|---|\n"
                    + "| **`whathappened\\`** | Nanally **App 自身**运行记录（按天 `nanally-yyyy-MM-dd.log`） | 同事排查工具问题时，把这个文件夹打包发过来 |\n"
                    + "| **`logs\\`** | 从手机拉下来的 **游戏** `com.piegame.bd` Logs | 游戏侧问题；不是 Nanally 的运行日志 |\n\n"
                    + "不要把 Nanally 的运行日志写进 `logs\\`，也不要指望在 `logs\\` 里找到本工具自己的报错。\n\n"
                    + "## 文件\n\n"
                    + "- `nanally-yyyy-MM-dd.log`：当天追加写入，UTF-8\n"
                    + "- 可能含设备序列号、APK 路径、adb 摘要；**不应**含飞书 Token / App Secret\n\n"
                    + "本 README 由 Nanally 首次启动时自动创建（已存在则不覆盖）。\n";
                File.WriteAllText(readme, text, Utf8Bom);
            }
            catch { }
        }
    }

    static class Acrylic
    {
        const int DwmWaUseImmersiveDarkMode = 20;
        const int DwmWaSystemBackdropType = 38;
        const int DwmWaBorderColor = 34;
        const int DwmWaCaptionColor = 35;
        const int DwmWaWindowCornerPreference = 33;
        const int DwmColorNone = unchecked((int)0xFFFFFFFE);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        public static void TryApply(Window window, bool dark, bool glass)
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.EnsureHandle();
                var hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = glass ? Colors.Transparent : (dark ? Colors.Black : Color.FromRgb(0xF2, 0xF5, 0xF8));
                int mode = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 19, ref mode, 4);
                DwmSetWindowAttribute(hwnd, DwmWaUseImmersiveDarkMode, ref mode, 4);
                if (!glass)
                    return;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int corner = 2;
                DwmSetWindowAttribute(hwnd, DwmWaWindowCornerPreference, ref corner, 4);
                int none = DwmColorNone;
                DwmSetWindowAttribute(hwnd, DwmWaCaptionColor, ref none, 4);
                DwmSetWindowAttribute(hwnd, DwmWaBorderColor, ref none, 4);
                int backdrop = 3;
                if (DwmSetWindowAttribute(hwnd, DwmWaSystemBackdropType, ref backdrop, 4) != 0)
                {
                    backdrop = 2;
                    DwmSetWindowAttribute(hwnd, DwmWaSystemBackdropType, ref backdrop, 4);
                }
            }
            catch { }
        }
    }

    static class Paths
    {
        public static string ToolRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir))
                dir = Environment.CurrentDirectory;
            return dir.TrimEnd('\\');
        }

        /// <summary>Nanally 自身运营日志目录（不是游戏 Logs）。</summary>
        public static string WhatHappenedDir()
        {
            return Path.Combine(ToolRoot(), "whathappened");
        }

        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "device";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (ch < 32 || Array.IndexOf(invalid, ch) >= 0)
                    sb.Append('_');
                else
                    sb.Append(ch);
            }
            var text = sb.ToString().Trim().TrimEnd('.');
            if (text.Length == 0)
                return "device";
            if (text.Length > 48)
                text = text.Substring(0, 48).Trim().TrimEnd('.');
            return text.Length == 0 ? "device" : text;
        }

        public static string SnapshotName(string deviceName, DateTime when)
        {
            var stamp = when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            return SafeName(deviceName) + "+" + stamp;
        }

        public static string CreateSnapshotDir(string root, string deviceName)
        {
            var baseName = SnapshotName(deviceName, DateTime.Now);
            var path = Path.Combine(root, baseName);
            var n = 2;
            while (Directory.Exists(path))
            {
                path = Path.Combine(root, baseName + "_" + n);
                n++;
            }
            Directory.CreateDirectory(path);
            return path;
        }

        public static void ClearDirectory(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(dir))
                File.Delete(file);
            foreach (var child in Directory.GetDirectories(dir))
                Directory.Delete(child, true);
        }

        public static int CopyContents(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            var count = 0;
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(src.Length).TrimStart('\\', '/');
                var target = Path.Combine(dest, rel);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.Copy(file, target, true);
                count++;
            }
            return count;
        }

        public static long DirectorySize(string dir)
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }

    sealed class DeviceInfo
    {
        public string Serial;
        public string State;
        public string Model;
        public string Name;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name))
                    return Name;
                if (!string.IsNullOrEmpty(Model))
                    return Model;
                return Serial;
            }
        }

        public bool IsReady
        {
            get { return State == "device"; }
        }
    }

    sealed class CopyResult
    {
        public bool Ok;
        public string Message;
        public string SnapshotDir;
        public string LogsDir;
        public int FileCount;
        public long Bytes;

        public static CopyResult Fail(string message)
        {
            return new CopyResult { Ok = false, Message = message };
        }
    }

    sealed class AdbResult
    {
        public int ExitCode;
        public string Stdout = "";
        public string Stderr = "";
        public bool TimedOut;
    }

    static class Adb
    {
        public const string RemoteLogs = "/sdcard/Android/data/com.piegame.bd/files/Logs";

        public static string FindExe()
        {
            foreach (var proc in Process.GetProcessesByName("adb"))
            {
                try
                {
                    var path = proc.MainModule.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                catch { }
            }

            var env = Environment.GetEnvironmentVariable("ADB");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return Path.GetFullPath(env);

            var onPath = FindOnPath("adb.exe");
            if (onPath != null)
                return onPath;

            var roots = new List<string>();
            Add(roots, Environment.GetEnvironmentVariable("ANDROID_HOME"));
            Add(roots, Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
            Add(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));
            foreach (var root in roots)
            {
                var candidate = Path.Combine(root, "platform-tools", "adb.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            var extras = new[]
            {
                @"C:\Workspace\onepiece\Tools\CITools\SDK\platform-tools\adb.exe",
                @"C:\Workspace\onepiece\Client\Tools\CITools\SDK\platform-tools\adb.exe",
                @"C:\Android\platform-tools\adb.exe",
                @"C:\platform-tools\adb.exe",
                @"D:\Android\platform-tools\adb.exe"
            };
            foreach (var extra in extras)
            {
                if (File.Exists(extra))
                    return extra;
            }
            return null;
        }

        static void Add(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value))
                list.Add(value);
        }

        static string FindOnPath(string exe)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(';'))
            {
                var dir = part.Trim();
                if (dir.Length == 0)
                    continue;
                try
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        public static AdbResult Run(string adb, string serial, string args, int timeoutMs, Action<Process> onStart)
        {
            return Run(adb, serial, args, timeoutMs, onStart, null);
        }

        public static AdbResult Run(string adb, string serial, string args, int timeoutMs, Action<Process> onStart, Action<string> onLine)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = adb;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            if (string.IsNullOrEmpty(serial))
                psi.Arguments = args;
            else
                psi.Arguments = "-s \"" + serial.Replace("\"", "") + "\" " + args;

            var process = new Process();
            process.StartInfo = psi;
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            // adb push 进度用 \r 刷新同一行；BeginOutputReadLine 要等换行才回调，会看起来像卡死。
            // 按字符泵流，遇到 \r 或 \n 都触发 onLine。
            process.Start();
            if (onStart != null)
                onStart(process);
            var outDone = new ManualResetEvent(false);
            var errDone = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { PumpStream(process.StandardOutput, stdout, onLine); }
                finally { outDone.Set(); }
            });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { PumpStream(process.StandardError, stderr, onLine); }
                finally { errDone.Set(); }
            });
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                return new AdbResult { ExitCode = -1, TimedOut = true, Stdout = stdout.ToString(), Stderr = stderr.ToString() };
            }
            process.WaitForExit();
            outDone.WaitOne(5000);
            errDone.WaitOne(5000);
            return new AdbResult { ExitCode = process.ExitCode, Stdout = stdout.ToString(), Stderr = stderr.ToString() };
        }

        static void PumpStream(StreamReader reader, StringBuilder capture, Action<string> onLine)
        {
            if (reader == null)
                return;
            var buf = new char[1024];
            var pending = new StringBuilder();
            while (true)
            {
                int n;
                try { n = reader.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n <= 0)
                    break;
                for (int i = 0; i < n; i++)
                {
                    var c = buf[i];
                    if (c == '\r' || c == '\n')
                    {
                        if (pending.Length == 0)
                            continue;
                        var line = pending.ToString();
                        pending.Length = 0;
                        capture.AppendLine(line);
                        if (onLine != null)
                            onLine(line);
                    }
                    else
                    {
                        pending.Append(c);
                    }
                }
            }
            if (pending.Length > 0)
            {
                var line = pending.ToString();
                capture.AppendLine(line);
                if (onLine != null)
                    onLine(line);
            }
        }

        /// <summary>从 adb 进度行解析百分比；找不到返回 -1。</summary>
        public static int TryParsePercent(string line)
        {
            if (string.IsNullOrEmpty(line))
                return -1;
            var idx = line.IndexOf('%');
            if (idx <= 0)
                return -1;
            var end = idx;
            var start = end - 1;
            while (start >= 0 && char.IsDigit(line[start]))
                start--;
            start++;
            if (start >= end)
                return -1;
            int pct;
            if (!int.TryParse(line.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out pct))
                return -1;
            if (pct < 0 || pct > 100)
                return -1;
            return pct;
        }

        public static List<DeviceInfo> ListDevices(string adb, out string error)
        {
            error = null;
            var result = Run(adb, null, "devices -l", 20000, null);
            if (result.TimedOut)
            {
                error = "adb 没有响应";
                return new List<DeviceInfo>();
            }
            if (result.ExitCode != 0)
            {
                error = FirstLine(result.Stderr.Length > 0 ? result.Stderr : result.Stdout);
                if (string.IsNullOrEmpty(error))
                    error = "读取设备失败";
                return new List<DeviceInfo>();
            }

            var list = new List<DeviceInfo>();
            foreach (var raw in result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                var state = parts[1];
                if (state != "device" && state != "unauthorized" && state != "offline" && state != "authorizing")
                    continue;
                var info = new DeviceInfo();
                info.Serial = parts[0];
                info.State = state;
                // 只要 USB：adb 无线调试的序列号是 host:port，跳过。
                if (IsWirelessSerial(info.Serial))
                    continue;
                info.Model = ReadToken(line, "model:");
                if (!string.IsNullOrEmpty(info.Model))
                    info.Model = info.Model.Replace('_', ' ');
                list.Add(info);
            }
            return list;
        }

        public static bool IsWirelessSerial(string serial)
        {
            return !string.IsNullOrEmpty(serial) && serial.IndexOf(':') >= 0;
        }

        public static void FillName(string adb, DeviceInfo info)
        {
            if (info == null || info.State != "device")
                return;
            var name = ShellOne(adb, info.Serial, "shell settings get global device_name");
            if (Usable(name))
            {
                info.Name = name;
                return;
            }
            var market = ShellOne(adb, info.Serial, "shell getprop ro.product.marketname");
            if (Usable(market))
            {
                info.Name = market;
                return;
            }
            var model = ShellOne(adb, info.Serial, "shell getprop ro.product.model");
            if (Usable(model))
                info.Name = model;
        }

        static bool Usable(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        static string ShellOne(string adb, string serial, string args)
        {
            var result = Run(adb, serial, args, 8000, null);
            if (result.TimedOut || result.ExitCode != 0)
                return null;
            var text = (result.Stdout ?? "").Replace("\r", "").Trim();
            var nl = text.IndexOf('\n');
            if (nl >= 0)
                text = text.Substring(0, nl).Trim();
            return text;
        }

        static string ReadToken(string line, string key)
        {
            var i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return null;
            var start = i + key.Length;
            var end = line.IndexOf(' ', start);
            if (end < 0)
                end = line.Length;
            return line.Substring(start, end - start);
        }

        public static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var line = text.Replace("\r", "").Trim();
            var nl = line.IndexOf('\n');
            if (nl >= 0)
                line = line.Substring(0, nl).Trim();
            if (line.Length > 180)
                line = line.Substring(0, 180);
            return line;
        }

        public static string FindContentRoot(string tempDir)
        {
            var wrapped = Path.Combine(tempDir, "Logs");
            if (Directory.Exists(wrapped))
                return wrapped;
            return tempDir;
        }
    }

    static class Apk
    {
        public static string FindAapt()
        {
            var env = Environment.GetEnvironmentVariable("AAPT");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return Path.GetFullPath(env);
            var onPath = FindToolOnPath("aapt.exe");
            if (onPath != null)
                return onPath;
            var roots = new List<string>();
            AddRoot(roots, Environment.GetEnvironmentVariable("ANDROID_HOME"));
            AddRoot(roots, Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
            AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));
            var extras = new[]
            {
                @"C:\Workspace\onepiece\Tools\CITools\SDK",
                @"C:\Workspace\onepiece\Client\Tools\CITools\SDK",
                @"C:\Android",
                @"D:\Android"
            };
            foreach (var extra in extras)
                AddRoot(roots, extra);
            string best = null;
            foreach (var root in roots)
            {
                var tools = Path.Combine(root, "build-tools");
                if (!Directory.Exists(tools))
                    continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(tools); }
                catch { continue; }
                Array.Sort(dirs);
                for (var i = dirs.Length - 1; i >= 0; i--)
                {
                    var candidate = Path.Combine(dirs[i], "aapt.exe");
                    if (File.Exists(candidate))
                    {
                        best = candidate;
                        break;
                    }
                }
                if (best != null)
                    return best;
            }
            return null;
        }

        static void AddRoot(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                list.Add(value);
        }

        static string FindToolOnPath(string exe)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(';'))
            {
                var dir = part.Trim();
                if (dir.Length == 0)
                    continue;
                try
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        public static string ReadPackageName(string apkPath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
            {
                error = "APK 不存在";
                return null;
            }
            var aapt = FindAapt();
            if (aapt == null)
            {
                error = "找不到 aapt（需要 Android SDK build-tools）";
                return null;
            }
            var result = RunTool(aapt, "dump badging \"" + apkPath.Replace("\"", "") + "\"", 60000);
            if (result.TimedOut)
            {
                error = "aapt 超时";
                return null;
            }
            var text = (result.Stdout ?? "") + "\n" + (result.Stderr ?? "");
            var m = Regex.Match(text, @"package:\s*name='([^']+)'");
            if (!m.Success)
            {
                error = Adb.FirstLine(result.Stderr.Length > 0 ? result.Stderr : result.Stdout);
                if (string.IsNullOrEmpty(error))
                    error = "读不出包名";
                return null;
            }
            return m.Groups[1].Value;
        }

        public static bool IsInstalled(string adb, string serial, string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return false;
            var result = Adb.Run(adb, serial, "shell pm path " + packageName, 20000, null);
            if (result.TimedOut || result.ExitCode != 0)
                return false;
            return (result.Stdout ?? "").IndexOf("package:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdbResult Uninstall(string adb, string serial, string packageName, Action<string> onLine)
        {
            return Adb.Run(adb, serial, "uninstall " + packageName, 180000, null, onLine);
        }

        public static AdbResult Install(string adb, string serial, string apkPath, bool replaceExisting, Action<string> onLine)
        {
            return Install(adb, serial, apkPath, replaceExisting, onLine, null, false);
        }

        public static AdbResult Install(string adb, string serial, string apkPath, bool replaceExisting, Action<string> onLine, Action<string> onPhase)
        {
            return Install(adb, serial, apkPath, replaceExisting, onLine, onPhase, false);
        }

        public static AdbResult Install(string adb, string serial, string apkPath, bool replaceExisting, Action<string> onLine, Action<string> onPhase, bool useAdbInstall)
        {
            var quoted = "\"" + apkPath.Replace("\"", "") + "\"";
            var flags = "-r -t ";
            if (useAdbInstall)
            {
                // 直接走 adb install 流式通道；适合对照 push+pm，或个别机型 pm 路径异常时切换。
                if (onPhase != null)
                    onPhase("install");
                return Adb.Run(adb, serial, "install " + flags + quoted, 600000, null, onLine);
            }

            // push + pm install：大包在不少机型上比 adb install 流式通道更快，也方便并行时各写各的临时文件。
            var safeSerial = (serial ?? "device").Replace(":", "_").Replace("\"", "");
            var remote = "/data/local/tmp/nanally_" + safeSerial + ".apk";
            if (onPhase != null)
                onPhase("push");
            var push = Adb.Run(adb, serial, "push " + quoted + " " + remote, 600000, null, onLine);
            if (push.TimedOut || push.ExitCode != 0)
                return push;
            if (onPhase != null)
                onPhase("install");
            var installed = Adb.Run(adb, serial, "shell pm install " + flags + remote, 300000, null, onLine);
            Adb.Run(adb, serial, "shell rm -f " + remote, 15000, null, null);
            return installed;
        }

        static AdbResult RunTool(string exe, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            var process = new Process();
            process.StartInfo = psi;
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { }
                return new AdbResult { ExitCode = -1, TimedOut = true, Stdout = stdout.ToString(), Stderr = stderr.ToString() };
            }
            process.WaitForExit();
            return new AdbResult { ExitCode = process.ExitCode, Stdout = stdout.ToString(), Stderr = stderr.ToString() };
        }
    }

    sealed partial class MainWindow : Window
    {
        readonly string toolRoot;
        string adbExe;
        List<DeviceInfo> devices = new List<DeviceInfo>();
        DeviceInfo selected;
        bool busy;
        Process pullProcess;
        readonly object pullLock = new object();
        string lastSnapshot;
        string lastLogs;

        TextBlock statusText;
        TextBlock previewName;
        StackPanel deviceHost;
        Border deviceCard;
        Popup devicePopup;
        TextBlock chevronText;
        DateTime popupClosedAt;
        Button copyButton;
        Button openCurrentButton;
        TextBlock refreshText;
        AppSettings settings;
        string logsBrowsePath;
        TextBox logsPathBox;
        StackPanel logsRows;
        TextBlock logsEmpty;
        WrapPanel mediaTiles;
        TextBlock mediaEmpty;
        int thumbGeneration;
        int thumbDone;
        int thumbTotal;
        bool thumbPumping;
        readonly Queue<MediaItem> thumbQueue = new Queue<MediaItem>();
        readonly object thumbLock = new object();
        Grid previewLayer;
        Image previewImage;
        MediaElement previewVideo;
        TextBlock previewTitle;
        TextBlock previewHint;
        int previewToken;
        bool previewPlaying = true;
        TextBlock mediaCount;
        ComboBox mediaFilter;
        Grid pageLogs;
        Grid pageMedia;
        Grid pageApk;
        TextBox setLogsBox;
        TextBox setAppIdBox;
        PasswordBox setTokenBox;
        TextBox setChatBox;
        Button tabLogs;
        Button tabMedia;
        Button tabApk;
        Button navSettings;
        Button navToggle;
        Button btnAbout;
        Button sendButton;
        ColumnDefinition colSidebar;
        TextBlock txtNavLogs;
        TextBlock txtNavMedia;
        TextBlock txtNavApk;
        TextBlock txtNavSettings;
        StackPanel panelSidebarBrand;
        Grid pageSettings;
        Grid pageAbout;
        TextBlock txtAboutVersion;
        Button btnAboutUpdate;
        UpdateInfo pendingUpdate;
        int aboutUpdateCheckGen;
        Border titleBar;
        Grid shellBody;
        Border shellSidebar;
        Border settingsNavCard;
        Border settingsContentCard;
        UIElement deviceBar;
        UIElement logsTools;
        UIElement mediaTools;
        UIElement apkTools;
        UIElement actionFooter;
        Grid footerRightHost;
        StackPanel footerButtons;
        Grid apkProgressHost;
        string currentPage = "logs";
        CheckBox chkThemeDark;
        CheckBox chkFollowSystem;
        CheckBox chkGlass;
        CheckBox chkApkUninstall;
        CheckBox chkApkInstallAll;
        CheckBox chkApkUseAdbInstall;
        ComboBox cmbAccent;
        Button btnSetLook;
        Button btnSetPath;
        Button btnSetFeishu;
        UIElement panelSetLook;
        UIElement panelSetPath;
        UIElement panelSetFeishu;
        TextBlock settingsHint;
        StackPanel apkLogPanel;
        ScrollViewer apkLogScroller;
        TextBlock apkHint;
        Border apkProgressTrack;
        Border apkProgressFill;
        TextBlock apkProgressText;
        double apkProgressValue;
        DateTime apkProgressStartedUtc;
        int apkLastLoggedPercent = -1;
        bool applyingTheme;
        readonly List<MediaItem> mediaItems = new List<MediaItem>();

        public MainWindow()
        {
            toolRoot = Paths.ToolRoot();
            settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.LogsDir))
                settings.LogsDir = Path.Combine(toolRoot, "logs");
            try { Directory.CreateDirectory(settings.LogsDir); } catch { }
            logsBrowsePath = settings.LogsDir;
            Theme.Apply(settings.ResolvedDark(), settings.Accent);
            BuildShell();
            TrySetIcon();
            Loaded += delegate
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
                RefreshAsync();
                RefreshLogsList();
            };
            Closing += delegate
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
                CloseDeviceMenu();
                KillPull();
            };
        }

        void OnSystemUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (!settings.FollowSystem)
                return;
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General && e.Category != Microsoft.Win32.UserPreferenceCategory.Color)
                return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                applyingTheme = true;
                if (chkThemeDark != null)
                    chkThemeDark.IsChecked = Theme.SystemIsDark();
                applyingTheme = false;
                ApplyAppearance();
            }));
        }

        void TrySetIcon()
        {
            try
            {
                var path = Path.Combine(toolRoot, "icon", "Nanally.png");
                if (!File.Exists(path))
                    return;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                Icon = image;
            }
            catch { }
        }

        void Build()
        {
            Title = "Nanally";
            Width = 376;
            Height = 560;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Font;
            Foreground = Theme.Ink;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var shell = new Border
            {
                Margin = new Thickness(16),
                Background = Theme.Bg,
                CornerRadius = new CornerRadius(28),
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 28,
                    ShadowDepth = 0,
                    Opacity = 0.22,
                    Color = Colors.Black
                }
            };
            Content = shell;

            var root = new Grid { Margin = new Thickness(0, 10, 0, 8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.MouseLeftButtonDown += DragFromShell;
            shell.Child = root;

            var chrome = BuildChrome();
            Grid.SetRow(chrome, 0);
            root.Children.Add(chrome);

            var body = BuildBody();
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = BuildFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            var home = new Border
            {
                Width = 128,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = Theme.Pink,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8)
            };
            Grid.SetRow(home, 3);
            root.Children.Add(home);

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += delegate { TickClock(); };
            clock.Start();
            TickClock();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && !busy)
                {
                    if (devicePopup != null && devicePopup.IsOpen)
                    {
                        devicePopup.IsOpen = false;
                        e.Handled = true;
                        return;
                    }
                    Close();
                }
            };
        }

        UIElement BuildChrome()
        {
            var bar = new Grid
            {
                Height = 44,
                Margin = new Thickness(18, 2, 14, 0),
                Background = Brushes.Transparent
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "Nanally",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Ink
            };
            bar.Children.Add(titleText);

            var close = CircleButton("×", 15);
            close.Tag = "nodrag";
            close.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Close();
            };
            Grid.SetColumn(close, 1);
            bar.Children.Add(close);
            return bar;
        }

        UIElement BuildBody()
        {
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(20, 4, 20, 8)
            };
            var stack = new StackPanel();
            scroller.Content = stack;

            stack.Children.Add(SectionRow("设备", out refreshText));
            refreshText.MouseLeftButtonUp += delegate
            {
                if (!busy)
                    RefreshAsync();
            };

            deviceHost = new StackPanel();
            deviceCard = Card(deviceHost);
            deviceCard.Tag = "nodrag";
            stack.Children.Add(deviceCard);
            ShowDevicePlaceholder("正在查找手机…", "稍等一下");

            stack.Children.Add(SectionLabel("保存位置"));
            var save = new StackPanel();
            previewName = Line("logs/设备名+日期时间", 15, Theme.Ink, FontWeights.SemiBold);
            previewName.Margin = new Thickness(0, 4, 0, 0);
            save.Children.Add(previewName);
            save.Children.Add(new TextBlock
            {
                Text = "来自 Android/data/com.piegame.bd/files/Logs",
                FontSize = 12,
                Foreground = Theme.Label,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            stack.Children.Add(Card(save));
            return scroller;
        }

        UIElement BuildFooter()
        {
            var footer = new StackPanel
            {
                Margin = new Thickness(20, 8, 20, 4),
                VerticalAlignment = VerticalAlignment.Bottom
            };

            statusText = new TextBlock
            {
                Text = "连上手机，点一下就拷贝",
                FontSize = 13,
                Foreground = Theme.Label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 10)
            };
            footer.Children.Add(statusText);

            copyButton = new Button
            {
                Content = "拷贝日志",
                Height = 50,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                IsEnabled = false
            };
            var copyFill = new LinearGradientBrush(Color.FromRgb(255, 111, 168), Color.FromRgb(255, 61, 127), 90);
            copyFill.Freeze();
            copyButton.Template = ButtonTemplate(copyFill, Theme.PinkPressed, 14);
            copyButton.Click += delegate { StartCopy(); };
            footer.Children.Add(copyButton);
            return footer;
        }

        UIElement SectionRow(string title, out TextBlock action)
        {
            var row = new Grid { Margin = new Thickness(4, 16, 4, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Line(title, 13, Theme.Label, FontWeights.Regular);
            row.Children.Add(label);
            action = new TextBlock
            {
                Text = "刷新",
                FontSize = 15,
                Foreground = Theme.Pink,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "nodrag"
            };
            Grid.SetColumn(action, 1);
            row.Children.Add(action);
            return row;
        }

        static TextBlock SectionLabel(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 13,
                Foreground = Theme.Label,
                Margin = new Thickness(4, 12, 4, 6)
            };
        }

        static Border Card(UIElement child)
        {
            return new Border
            {
                Background = Theme.Card,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 12, 14, 12),
                Child = child
            };
        }

        static TextBlock Line(string text, double size, Brush color, FontWeight weight)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap
            };
            var key = Theme.Key(color);
            if (key != null)
                block.SetResourceReference(TextBlock.ForegroundProperty, key);
            else if (color != null)
                block.Foreground = color;
            else
                block.SetResourceReference(TextBlock.ForegroundProperty, "nn.Label");
            return block;
        }

        static Border CircleButton(string glyph, double size)
        {
            var border = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = Theme.Chip,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = new TextBlock
            {
                Text = glyph,
                FontSize = size,
                Foreground = Theme.Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            };
            border.MouseEnter += delegate { border.Background = Theme.ChipHover; };
            border.MouseLeave += delegate { border.Background = Theme.Chip; };
            return border;
        }

        static ControlTemplate ButtonTemplate(Brush normal, Brush pressed, double radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            Theme.Bind(border, Border.BackgroundProperty, normal);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            HoverBrush(border, pressed, normal);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            var down = new Trigger { Property = Button.IsPressedProperty, Value = true };
            down.Setters.Add(new Setter(Border.OpacityProperty, 0.86, "Bd"));
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38));
            template.Triggers.Add(down);
            template.Triggers.Add(off);
            return template;
        }

        static void HoverBrush(FrameworkElementFactory border, Brush enter, Brush leave)
        {
            border.AddHandler(UIElement.MouseEnterEvent, new MouseEventHandler(delegate(object sender, MouseEventArgs e)
            {
                var box = sender as Border;
                if (box != null && enter != null)
                    box.Background = enter;
            }));
            border.AddHandler(UIElement.MouseLeaveEvent, new MouseEventHandler(delegate(object sender, MouseEventArgs e)
            {
                var box = sender as Border;
                if (box != null)
                    box.Background = leave;
            }));
        }

        void DragFromShell(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;
            if (IsDragBlocked(e.OriginalSource as DependencyObject))
                return;
            try { DragMove(); } catch { }
        }

        static bool IsDragBlocked(DependencyObject source)
        {
            var node = source;
            while (node != null)
            {
                if (node is Button || node is ScrollBar)
                    return true;
                var element = node as FrameworkElement;
                if (element != null && Equals(element.Tag, "nodrag"))
                    return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        void TickClock()
        {
            if (previewName == null)
                return;
            if (selected != null && selected.IsReady)
                previewName.Text = "logs/" + Paths.SnapshotName(selected.DisplayName, DateTime.Now);
            else if (selected == null)
                previewName.Text = "logs/设备名+日期时间";
        }

        void ShowDevicePlaceholder(string title, string sub)
        {
            deviceHost.Children.Clear();
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Line(title, 14, Theme.Ink, FontWeights.SemiBold));
            var hint = Line(sub, 12, Theme.Label, FontWeights.Regular);
            hint.Margin = new Thickness(8, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(hint);
            deviceHost.Children.Add(row);
        }

        void RefreshAsync()
        {
            if (busy)
                return;
            adbExe = Adb.FindExe();
            if (adbExe == null)
            {
                devices = new List<DeviceInfo>();
                selected = null;
                ShowDevicePlaceholder("没有找到 adb", "先装 platform-tools");
                SetStatus("没有找到 adb，装好后再点刷新", Theme.Red);
                UpdateButtons();
                return;
            }

            ShowDevicePlaceholder("正在查找手机…", "稍等一下");
            SetStatus("正在查找连接的手机…", Theme.Label);
            var adb = adbExe;
            var keep = selected == null ? null : selected.Serial;
            Task.Run(delegate
            {
                string error;
                var list = Adb.ListDevices(adb, out error);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    devices = list;
                    WhatHappened.Info("刷新设备 count=" + list.Count.ToString(CultureInfo.InvariantCulture)
                        + (string.IsNullOrEmpty(error) ? "" : " error=" + error));
                    if (error != null && list.Count == 0)
                    {
                        selected = null;
                        ShowDevicePlaceholder("读不到设备", error);
                        SetStatus(error, Theme.Red);
                        UpdateButtons();
                        return;
                    }
                    RenderDevices(keep);
                    SetStatus(StatusForSelection(), Theme.Label);
                    UpdateButtons();
                }));
                foreach (var item in list)
                    Adb.FillName(adb, item);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!busy)
                    {
                        RenderDevices(selected == null ? keep : selected.Serial);
                        TickClock();
                        SetStatus(StatusForSelection(), Theme.Label);
                    }
                }));
            });
        }

        void RenderDevices(string preferSerial)
        {
            CloseDeviceMenu();
            deviceHost.Children.Clear();
            chevronText = null;
            var previousSerial = selected == null ? null : selected.Serial;
            if (devices.Count == 0)
            {
                selected = null;
                ShowDevicePlaceholder("没有连接的手机", "用数据线连上，并允许 USB 调试");
                OnSelectedDeviceChanged(previousSerial);
                return;
            }

            selected = PickDevice(preferSerial);
            var multi = devices.Count > 1;
            var face = BuildDeviceFace(selected, multi);
            if (multi)
            {
                face.Cursor = Cursors.Hand;
                face.MouseLeftButtonUp += OnDevicePickerClick;
            }
            deviceHost.Children.Add(face);
            OnSelectedDeviceChanged(previousSerial);
        }

        void OnSelectedDeviceChanged(string previousSerial)
        {
            var serial = selected == null ? null : selected.Serial;
            if (string.Equals(previousSerial ?? "", serial ?? "", StringComparison.Ordinal))
                return;
            ClosePreview();
            mediaItems.Clear();
            ResetThumbs();
            if (mediaTiles != null)
                RenderMediaRows();
            if (currentPage == "media" && selected != null && selected.IsReady && !busy)
                RefreshMediaAsync();
        }

        DeviceInfo PickDevice(string preferSerial)
        {
            DeviceInfo prefer = null;
            foreach (var item in devices)
            {
                if (item.Serial == preferSerial)
                    prefer = item;
            }
            if (prefer != null)
                return prefer;
            foreach (var item in devices)
            {
                if (item.IsReady)
                    return item;
            }
            return devices[0];
        }

        Border BuildDeviceFace(DeviceInfo item, bool showChevron)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = DotBrush(item.State),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(dot);

            var text = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            text.Children.Add(Line(item.DisplayName, 14, Theme.Ink, FontWeights.SemiBold));
            var subBlock = Line(StateText(item), 12, Theme.Label, FontWeights.Regular);
            subBlock.Margin = new Thickness(8, 0, 0, 0);
            subBlock.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(subBlock);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (showChevron)
            {
                chevronText = new TextBlock
                {
                    Text = "\uE70D",
                    FontFamily = Theme.IconFont,
                    FontSize = 10,
                    Foreground = Theme.Label,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(chevronText, 2);
                grid.Children.Add(chevronText);
            }

            return new Border { Child = grid, Background = Brushes.Transparent };
        }

        void OnDevicePickerClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (busy || devices.Count < 2)
                return;
            if ((DateTime.UtcNow - popupClosedAt).TotalMilliseconds < 250)
                return;
            if (devicePopup != null && devicePopup.IsOpen)
            {
                devicePopup.IsOpen = false;
                return;
            }
            OpenDeviceMenu();
        }

        void OpenDeviceMenu()
        {
            if (deviceCard == null || devices.Count < 2)
                return;
            EnsureDevicePopup();
            var list = new StackPanel();
            for (var i = 0; i < devices.Count; i++)
            {
                var item = devices[i];
                list.Children.Add(BuildMenuItem(item, item == selected, i == devices.Count - 1));
            }
            var scroller = new ScrollViewer
            {
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = list
            };
            devicePopup.Child = new Border
            {
                Background = Theme.Card,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 4, 0, 0),
                Child = scroller,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.45,
                    Color = Colors.Black
                }
            };
            devicePopup.PlacementTarget = deviceCard;
            devicePopup.Width = deviceCard.ActualWidth > 0 ? deviceCard.ActualWidth : 300;
                if (chevronText != null)
                    chevronText.Text = "\uE70E";
            devicePopup.IsOpen = true;
        }

        UIElement BuildMenuItem(DeviceInfo item, bool isSelected, bool last)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? Theme.CardSoft : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 1, 0, 1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = DotBrush(item.State),
                Margin = new Thickness(0, 5, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            grid.Children.Add(dot);

            var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(Line(item.DisplayName, 15, Theme.Ink, FontWeights.SemiBold));
            var sub = Line(StateText(item), 12, Theme.Label, FontWeights.Regular);
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (isSelected)
            {
                var mark = new TextBlock
                {
                    Text = "\uE73E",
                    FontFamily = Theme.IconFont,
                    FontSize = 12,
                    Foreground = Theme.Ink,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(mark, 2);
                grid.Children.Add(mark);
            }

            row.Child = grid;
            row.MouseEnter += delegate
            {
                if (!isSelected)
                    row.Background = Theme.MenuHover;
            };
            row.MouseLeave += delegate
            {
                row.Background = isSelected ? Theme.MenuHover : Brushes.Transparent;
            };
            row.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (busy)
                    return;
                if (devicePopup != null)
                    devicePopup.IsOpen = false;
                RenderDevices(item.Serial);
                TickClock();
                SetStatus(StatusForSelection(), item.IsReady ? Theme.Label : Theme.Orange);
                UpdateButtons();
            };
            return row;
        }

        void EnsureDevicePopup()
        {
            if (devicePopup != null)
                return;
            devicePopup = new Popup
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = deviceCard,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = 6
            };
            devicePopup.Closed += delegate
            {
                popupClosedAt = DateTime.UtcNow;
                if (chevronText != null)
                    chevronText.Text = "\uE70D";
            };
        }

        void CloseDeviceMenu()
        {
            if (devicePopup != null && devicePopup.IsOpen)
                devicePopup.IsOpen = false;
        }

        static Brush DotBrush(string state)
        {
            if (state == "device")
                return Theme.Pink;
            if (state == "unauthorized" || state == "authorizing")
                return Theme.Orange;
            return Theme.Label;
        }

        static string StateText(DeviceInfo item)
        {
            if (item.State == "device")
                return "已连接 · USB";
            if (item.State == "unauthorized" || item.State == "authorizing")
                return "请在手机上允许调试";
            if (item.State == "offline")
                return "离线";
            return item.State;
        }

        string StatusForSelection()
        {
            if (selected == null)
                return devices.Count == 0 ? "连上手机，点一下就拷贝" : "选一台手机";
            if (!selected.IsReady)
                return "手机还没允许调试，点允许后再刷新";
            if (devices.Count > 1)
                return "已选中 " + selected.DisplayName + "，下拉可以换一台";
            return "已连上 " + selected.DisplayName + "，可以拷贝了";
        }

        void UpdateButtons()
        {
            if (copyButton != null)
            {
                copyButton.IsEnabled = !busy && selected != null && selected.IsReady && adbExe != null;
                copyButton.Content = busy ? "正在拷贝…" : "拷贝日志";
            }
            if (sendButton != null)
                sendButton.IsEnabled = !busy && selected != null && selected.IsReady && adbExe != null;
            if (refreshText != null)
                refreshText.Opacity = busy ? 0.35 : 1;
        }

        void SetStatus(string text, Brush color)
        {
            statusText.Text = text;
            var brush = color ?? Theme.Label;
            var key = Theme.Key(brush);
            if (key != null)
                statusText.SetResourceReference(TextBlock.ForegroundProperty, key);
            else
                statusText.Foreground = brush;
            if (brush == Theme.Red)
                WhatHappened.Error("status " + text);
            else if (brush == Theme.Orange)
                WhatHappened.Warn("status " + text);
        }

        void StartPulse()
        {
            var anim = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            statusText.BeginAnimation(OpacityProperty, anim);
        }

        void StopPulse()
        {
            statusText.BeginAnimation(OpacityProperty, null);
            statusText.Opacity = 1;
        }

        void StartCopy()
        {
            if (busy || selected == null || !selected.IsReady || adbExe == null)
                return;
            CloseDeviceMenu();
            busy = true;
            UpdateButtons();
            SetStatus("正在从手机拷贝…", Theme.Label);
            StartPulse();
            WhatHappened.Info("拷贝游戏 Logs 开始 serial=" + selected.Serial + " device=" + selected.DisplayName);

            var adb = adbExe;
            var serial = selected.Serial;
            var name = selected.DisplayName;
            var root = string.IsNullOrWhiteSpace(settings.LogsDir) ? Path.Combine(toolRoot, "logs") : settings.LogsDir;
            Task.Run(delegate
            {
                CopyResult result;
                try { result = DoCopy(adb, serial, name, root); }
                catch (Exception ex) { result = CopyResult.Fail(ex.Message); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    StopPulse();
                    busy = false;
                    if (result.Ok)
                    {
                        lastSnapshot = result.SnapshotDir;
                        lastLogs = result.LogsDir;
                        SetStatus(result.Message, Theme.PinkSoft);
                        logsBrowsePath = result.SnapshotDir;
                        RefreshLogsList();
                        WhatHappened.Info("拷贝游戏 Logs 成功 → " + result.SnapshotDir);
                    }
                    else
                    {
                        SetStatus(result.Message, Theme.Red);
                        WhatHappened.Error("拷贝游戏 Logs 失败: " + result.Message);
                    }
                    UpdateButtons();
                }));
            });
        }

        CopyResult DoCopy(string adb, string serial, string deviceName, string root)
        {
            var remote = Adb.RemoteLogs;
            var listed = Adb.Run(adb, serial, "shell ls \"" + remote + "\"", 20000, null);
            if (listed.TimedOut)
                return CopyResult.Fail("读取手机目录超时了");
            if (listed.ExitCode != 0)
            {
                var detail = Adb.FirstLine(listed.Stderr.Length > 0 ? listed.Stderr : listed.Stdout);
                if (string.IsNullOrEmpty(detail))
                    detail = "手机上还没有这个日志目录";
                return CopyResult.Fail(detail);
            }

            var temp = Path.Combine(root, ".nanally-tmp");
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);

                Dispatcher.BeginInvoke(new Action(delegate { SetStatus("正在从手机拷贝…", Theme.Label); }));
                var pulled = Adb.Run(adb, serial, "pull \"" + remote + "\" \"" + temp + "\"", 600000, delegate(Process p)
                {
                    lock (pullLock) { pullProcess = p; }
                });
                lock (pullLock) { pullProcess = null; }
                if (pulled.TimedOut)
                    return CopyResult.Fail("拷贝超时了，手机可能掉线了");
                if (pulled.ExitCode != 0)
                {
                    var detail = Adb.FirstLine(pulled.Stderr.Length > 0 ? pulled.Stderr : pulled.Stdout);
                    if (string.IsNullOrEmpty(detail))
                        detail = "从手机拷贝失败";
                    return CopyResult.Fail(detail);
                }

                var content = Adb.FindContentRoot(temp);
                var files = Directory.GetFiles(content, "*", SearchOption.AllDirectories);
                if (files.Length == 0)
                    return CopyResult.Fail("这个路径下没有文件");

                Dispatcher.BeginInvoke(new Action(delegate { SetStatus("正在整理到本地…", Theme.Label); }));
                var logs = root;
                Directory.CreateDirectory(logs);
                var snap = Paths.CreateSnapshotDir(logs, deviceName);
                var count = Paths.CopyContents(content, snap);
                var bytes = Paths.DirectorySize(snap);
                var result = new CopyResult();
                result.Ok = true;
                result.FileCount = count;
                result.Bytes = bytes;
                result.LogsDir = logs;
                result.SnapshotDir = snap;
                result.Message = "已拷贝 " + count.ToString(CultureInfo.InvariantCulture) + " 个文件 · " + Paths.FormatSize(bytes);
                return result;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        void KillPull()
        {
            lock (pullLock)
            {
                try
                {
                    if (pullProcess != null && !pullProcess.HasExited)
                        pullProcess.Kill();
                }
                catch { }
            }
        }

        void OpenCurrentFolder()
        {
            var path = logsPathBox != null && logsPathBox.Text != null ? logsPathBox.Text.Trim() : "";
            if (!Directory.Exists(path))
                path = logsBrowsePath;
            if (!Directory.Exists(path))
                path = settings.LogsDir;
            if (!Directory.Exists(path))
            {
                SetStatus("这个路径打不开", Theme.Orange);
                return;
            }
            OpenFolder(path);
        }

        static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}
