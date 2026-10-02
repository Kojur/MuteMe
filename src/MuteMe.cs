using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace MuteMe {
    public class Settings {
        public string MicrophoneId = "";
        public int IdleSeconds = 60;
        public bool Notifications = true;
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuteMe"); } }
        public static Settings Load() {
            try { using (var f = File.OpenRead(Path.Combine(Folder, "settings.xml"))) {
                var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f);
                if (s.IdleSeconds < 1 || s.IdleSeconds > 3600) s.IdleSeconds = 60;
                return s;
            } } catch { return new Settings(); }
        }
        public void Save() {
            Directory.CreateDirectory(Folder);
            using (var f = File.Create(Path.Combine(Folder, "settings.xml"))) new XmlSerializer(typeof(Settings)).Serialize(f, this);
        }
    }
    // No audio operations happen inside the mouse message handler.
    public sealed class MouseListener : NativeWindow, IDisposable {
        public Action Activity;
        IntPtr buffer = Marshal.AllocHGlobal(128);
        [StructLayout(LayoutKind.Sequential)] struct Device { public ushort page, usage; public uint flags; public IntPtr target; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        public MouseListener() {
            CreateHandle(new CreateParams { Caption = "MuteMe input", Parent = new IntPtr(-3) });
            var d = new Device { page = 1, usage = 2, flags = 0x100, target = Handle };
            if (!RegisterRawInputDevices(new Device[] { d }, 1, (uint)Marshal.SizeOf(typeof(Device)))) {
                Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0xff) {
                uint size = 128;
                uint read = GetRawInputData(m.LParam, 0x10000003, buffer, ref size, (uint)(8 + 2 * IntPtr.Size));
                if (read != uint.MaxValue && read > 0 && Activity != null) Activity();
            }
            base.WndProc(ref m);
        }
        public void Dispose() {
            if (Handle != IntPtr.Zero) {
                RegisterRawInputDevices(new Device[] { new Device { page = 1, usage = 2, flags = 1 } }, 1, (uint)Marshal.SizeOf(typeof(Device)));
                DestroyHandle();
            }
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
        }
    }
    public interface IMuteDevice { bool Muted { get; set; } }
    public sealed class VolumeDevice : IMuteDevice, IDisposable {
        IAudioEndpointVolume volume;
        Guid context = new Guid("635051f6-8da5-4778-839f-0412a27c9e07");
        public VolumeDevice(string id) { volume = Audio.Open(id); }
        public bool Muted {
            get { bool value; Audio.Check(volume.GetMute(out value)); return value; }
            set { Audio.Check(volume.SetMute(value, ref context)); }
        }
        public void Dispose() { Audio.Release(volume); volume = null; }
    }
    // Own only changes made by this app; never unmute a device that was already muted.
    public sealed class MuteSession {
        public bool OwnsMute { get; private set; }
        readonly IMuteDevice device;
        public MuteSession(IMuteDevice d) { device = d; }
        public bool Begin() {
            if (device.Muted) return false;
            device.Muted = true; OwnsMute = true; return true;
        }
        public bool Restore() {
            if (!OwnsMute) return false;
            device.Muted = false; OwnsMute = false; return true;
        }
    }
    public sealed class TrayApp : ApplicationContext {
        readonly Settings settings = Settings.Load();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly MouseListener mouse;
        readonly Icon readyIcon, mutedIcon, pausedIcon;
        double lastMouse, nextAudio, lastError = -100;
        bool paused, idle, stopping;
        string currentId;
        VolumeDevice volume;
        MuteSession session;
        public TrayApp(bool smoke) {
            readyIcon = MakeIcon(Color.SeaGreen); mutedIcon = MakeIcon(Color.DarkOrange); pausedIcon = MakeIcon(Color.Gray);
            tray.Icon = readyIcon; tray.Text = "MuteMe - starting"; tray.ContextMenuStrip = menu; tray.Visible = true;
            mouse = new MouseListener(); mouse.Activity = delegate { lastMouse = clock.Elapsed.TotalSeconds; };
            if (!smoke && String.IsNullOrEmpty(settings.MicrophoneId)) {
                try {
                    var devices = Audio.List();
                    var usb = devices.FindAll(d => d.Name.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (usb.Count == 1) settings.MicrophoneId = usb[0].Id;
                    else if (devices.Count == 1) settings.MicrophoneId = devices[0].Id;
                    else paused = true;
                    settings.Save();
                } catch { paused = true; }
            }
            menu.Opening += delegate { BuildMenu(); };
            tray.DoubleClick += delegate { ShowHelp(); };
            timer.Interval = 250;
            timer.Tick += delegate { if (!smoke) Tick(); };
            timer.Start(); UpdateStatus();
            if (!smoke) Notice(paused ? "Choose your microphone" : "MuteMe is ready", paused ? "Right-click the tray icon and choose Microphone to start." : "Your microphone will mute after " + settings.IdleSeconds + " seconds without mouse activity.");
            if (smoke) {
                BuildMenu(); BuildMenu();
                var stop = new System.Windows.Forms.Timer { Interval = 5000 };
                stop.Tick += delegate { stop.Stop(); stop.Dispose(); ExitThread(); }; stop.Start();
            }
        }
        void Tick() {
            bool shouldIdle = !paused && IsIdle(clock.Elapsed.TotalSeconds, lastMouse, settings.IdleSeconds);
            if (!shouldIdle) {
                if (idle || session != null) {
                    if (Restore()) { idle = false; currentId = null; UpdateStatus(); }
                }
                return;
            }
            if (clock.Elapsed.TotalSeconds < nextAudio) return;
            nextAudio = clock.Elapsed.TotalSeconds + 5;
            try {
                string id = String.IsNullOrEmpty(settings.MicrophoneId) ? Audio.DefaultId() : settings.MicrophoneId;
                if (idle && currentId == id) return;
                if (!Restore()) return;
                volume = new VolumeDevice(id); session = new MuteSession(volume);
                bool changed = session.Begin(); currentId = id; idle = true; UpdateStatus();
                if (changed) Notice("Microphone muted", "No mouse activity for " + settings.IdleSeconds + " seconds. Move the mouse to restore it.");
            } catch (Exception ex) { ReportError(ex); }
        }
        bool Restore() {
            if (session == null) { if (volume != null) { volume.Dispose(); volume = null; } return true; }
            try {
                bool restored = session.Restore(); session = null;
                if (volume != null) { volume.Dispose(); volume = null; }
                if (restored && !stopping) Notice("Microphone restored", "Mouse activity detected. Your microphone is unmuted.");
                return true;
            } catch (Exception ex) { ReportError(ex); return false; }
        }
        void ReportError(Exception ex) {
            tray.Icon = pausedIcon; tray.Text = "MuteMe - microphone unavailable";
            if (clock.Elapsed.TotalSeconds - lastError < 30) return;
            lastError = clock.Elapsed.TotalSeconds;
            Notice("Microphone unavailable", "Check that it is connected, or choose another microphone in the tray menu.");
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(Path.Combine(Settings.Folder, "last-error.txt"), DateTime.Now + "\r\n" + ex); } catch { }
        }
        void Notice(string title, string text) { if (settings.Notifications) tray.ShowBalloonTip(2000, title, text, ToolTipIcon.None); }
        void UpdateStatus() {
            tray.Icon = paused ? pausedIcon : idle ? mutedIcon : readyIcon;
            tray.Text = paused ? "MuteMe - paused" : idle ? "MuteMe - idle (microphone muted)" : "MuteMe - watching mouse activity";
        }
        void Reset() { idle = false; currentId = null; lastMouse = clock.Elapsed.TotalSeconds; nextAudio = 0; UpdateStatus(); }
        void Save() { try { settings.Save(); } catch { Notice("Could not save settings", "Your choices apply for this session only."); } }
        void BuildMenu() {
            while (menu.Items.Count > 0) { var oldItem = menu.Items[0]; menu.Items.RemoveAt(0); oldItem.Dispose(); }
            menu.Items.Add(new ToolStripMenuItem(paused ? "Paused" : idle ? "Idle - microphone muted" : "Active - watching mouse") { Enabled = false });
            menu.Items.Add("Pause automatic muting", null, delegate {
                if (!Restore()) return; paused = !paused; Reset();
            }); ((ToolStripMenuItem)menu.Items[1]).Checked = paused;
            var mics = new ToolStripMenuItem("Microphone");
            var def = new ToolStripMenuItem("Follow Windows default input", null, delegate { SelectMic(""); });
            def.Checked = String.IsNullOrEmpty(settings.MicrophoneId); mics.DropDownItems.Add(def);
            try {
                foreach (var d in Audio.List()) {
                    var device = d;
                    var item = new ToolStripMenuItem(device.Name, null, delegate { SelectMic(device.Id); });
                    item.Checked = device.Id == settings.MicrophoneId; mics.DropDownItems.Add(item);
                }
            } catch { mics.DropDownItems.Add(new ToolStripMenuItem("No microphones available") { Enabled = false }); }
            menu.Items.Add(mics);
            var delay = new ToolStripMenuItem("Mute after");
            foreach (int seconds in new int[] { 60, 120, 300 }) {
                int s = seconds;
                var item = new ToolStripMenuItem((s / 60) + " minute" + (s == 60 ? "" : "s"), null, delegate {
                    if (!Restore()) return; settings.IdleSeconds = s; Save(); Reset();
                }); item.Checked = settings.IdleSeconds == s; delay.DropDownItems.Add(item);
            }
            menu.Items.Add(delay);
            var notify = new ToolStripMenuItem("Small notifications", null, delegate { settings.Notifications = !settings.Notifications; Save(); });
            notify.Checked = settings.Notifications; menu.Items.Add(notify);
            var start = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            start.Checked = StartupEnabled(); menu.Items.Add(start);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("How it works", null, delegate { ShowHelp(); });
            menu.Items.Add("Exit (restore microphone)", null, delegate { ExitThread(); });
        }
        void SelectMic(string id) { if (!Restore()) return; settings.MicrophoneId = id; paused = false; Save(); Reset(); }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        bool StartupEnabled() { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("MuteMe") != null; }
        void ToggleStartup() {
            try { using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (key.GetValue("MuteMe") != null) key.DeleteValue("MuteMe", false);
                else key.SetValue("MuteMe", "\"" + Application.ExecutablePath + "\"");
            } } catch (Exception ex) { MessageBox.Show(ex.Message, "Could not change startup"); }
        }
        void ShowHelp() {
            MessageBox.Show("MuteMe mutes the selected microphone after 60 seconds without mouse movement, clicks or scrolling. Mouse activity restores a mute made by MuteMe.\r\n\r\nA microphone that was already muted stays muted. Keyboard activity does not reset the timer. USB microphones can be selected in the tray menu.\r\n\r\nGreen: watching. Orange: idle. Gray: paused or unavailable. Pause and Exit restore a mute made by this app. Start with Windows is optional.\r\n\r\nNo recording, camera, network, or administrator access. Settings are saved in %LOCALAPPDATA%\\MuteMe.", "MuteMe", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override void ExitThreadCore() {
            stopping = true;
            if (!Restore()) {
                stopping = false;
                MessageBox.Show("The microphone could not be restored. Reconnect it and try Exit again, or restore it in Windows sound settings.", "MuteMe"); return;
            }
            timer.Stop(); timer.Dispose(); mouse.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose();
            readyIcon.Dispose(); mutedIcon.Dispose(); pausedIcon.Dispose(); base.ExitThreadCore();
        }
        static Icon MakeIcon(Color color) {
            using (var bitmap = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new Pen(Color.White, 2.5f)) {
                    g.DrawArc(pen, 9, 8, 14, 15, 0, 180); g.DrawLine(pen, 16, 23, 16, 27); g.DrawLine(pen, 12, 27, 20, 27);
                }
                using (var brush = new SolidBrush(Color.White)) g.FillRectangle(brush, 13, 7, 6, 12);
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static bool IsIdle(double now, double last, int seconds) { return now - last >= seconds; }
    }
    static class Program {
        [STAThread] static void Main(string[] args) {
            if (args.Length > 0 && args[0] == "--self-test") { Tests.Run(args[1]); return; }
            if (args.Length > 0 && args[0] == "--diagnostics") {
                try {
                    var lines = new System.Collections.Generic.List<string>();
                    foreach (var d in Audio.List()) using (var v = new VolumeDevice(d.Id)) lines.Add(d.Name + " | Muted=" + v.Muted + " | " + d.Id);
                    lines.Add("Default input: " + Audio.DefaultId()); File.WriteAllLines(args[1], lines.ToArray());
                } catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; } return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\MuteMe.Tray.635051f6", out created)) {
                if (!created) return;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new TrayApp(args.Length > 0 && args[0] == "--smoke-test")); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "MuteMe could not start"); Environment.ExitCode = 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
    static class Tests {
        sealed class Fake : IMuteDevice {
            public bool State, Fail; public int Writes;
            public bool Muted { get { return State; } set { if (Fail) throw new InvalidOperationException(); State = value; Writes++; } }
        }
        static void Assert(bool value, string name) { if (!value) throw new Exception(name); }
        public static void Run(string path) {
            try {
                Assert(!TrayApp.IsIdle(59.99, 0, 60), "Before threshold"); Assert(TrayApp.IsIdle(60, 0, 60), "At threshold");
                Assert(!TrayApp.IsIdle(60.1, 60, 60), "Mouse return resets timeout");
                var f = new Fake(); var s = new MuteSession(f);
                Assert(s.Begin() && f.State && s.OwnsMute, "Idle mutes"); Assert(s.Restore() && !f.State, "Return unmutes");
                Assert(!s.Restore() && f.Writes == 2, "Repeated restore does not change sound");
                f = new Fake { State = true }; s = new MuteSession(f);
                Assert(!s.Begin() && !s.OwnsMute, "Manual mute preserved"); Assert(!s.Restore() && f.State && f.Writes == 0, "Manual mute never cleared");
                f = new Fake(); s = new MuteSession(f); s.Begin(); f.Fail = true;
                try { s.Restore(); Assert(false, "Restore must fail"); } catch (InvalidOperationException) { }
                Assert(s.OwnsMute, "Failed restore keeps ownership"); f.Fail = false; Assert(s.Restore(), "Restore retry succeeds");
                f = new Fake { Fail = true }; s = new MuteSession(f);
                try { s.Begin(); Assert(false, "Mute must fail"); } catch (InvalidOperationException) { }
                Assert(!s.OwnsMute, "Failed mute does not gain ownership");
                File.WriteAllText(path, "PASS: idle threshold, mouse return, mute/restore, manual mute preservation, repeated restore, audio failure recovery.");
            } catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); Environment.ExitCode = 1; }
        }
    }
}
