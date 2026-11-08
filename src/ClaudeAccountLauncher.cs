// Claude Desktop multi-account launcher for Windows.
//
// Starts Claude Desktop with its own --user-data-dir so a second (third, ...) account can run
// next to your normal one. The account number comes from this program's file name:
//   ClaudeAccount2.exe  ->  profile folder %LOCALAPPDATA%\ClaudeProfile-Account2
//
// What it does:
//   * If that account is already open, it brings its window to the front.
//   * Otherwise it starts the installed Claude.exe with --user-data-dir=<profile folder>.
//   * It gives that account's windows their own taskbar identity, so each account gets its own
//     taskbar button and numbered icon instead of stacking onto the normal Claude button.
//
// What it never does: read, copy or send sign-in data, change Claude's own files, or use the network.
//
// Options (mainly for install.ps1 / uninstall.ps1 and troubleshooting):
//   --status                Print what the launcher sees. Pipe it: .\ClaudeAccount2.exe --status | Write-Output
//   --tag-shortcut <file>   Give a .lnk shortcut this account's taskbar identity.
//   --reset-taskbar         Put this account's open windows back on Claude's normal taskbar button.
//
// Built by install.ps1 with the C# compiler that ships with Windows (.NET Framework 4.x),
// which only understands C# 5, so the code avoids newer syntax on purpose.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    static int Main(string[] args) {
        string title = "Claude account launcher";
        try {
            int account = AccountNumber();
            title = "Claude - Account " + account;
            SetCurrentProcessExplicitAppUserModelID(Taskbar.AppId(account));

            string option = args.Length > 0 ? args[0] : "";
            if (option == "--status" && args.Length == 1) { Status(account); return 0; }
            if (option == "--tag-shortcut" && args.Length == 2) { Taskbar.TagShortcut(args[1], account); return 0; }
            if (option == "--reset-taskbar" && args.Length == 1) {
                foreach (int pid in ClaudeApp.ProfileProcesses(account)) Taskbar.Reset(pid);
                return 0;
            }
            if (args.Length > 0) throw new Exception("Unknown option. Use --status, --tag-shortcut <file> or --reset-taskbar.");

            Launch(account);
            return 0;
        } catch (Exception e) {
            Log.Error(e.Message);
            if (args.Length == 0) MessageBox.Show(e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    static int AccountNumber() {
        string name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        Match match = Regex.Match(name, @"(\d+)$");
        int number;
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out number) || number < 2 || number > 99)
            throw new Exception("This launcher's file name must end with an account number from 2 to 99, for example ClaudeAccount2.exe.");
        return number;
    }

    static void Launch(int account) {
        string profile = ClaudeApp.ProfileDir(account);
        List<int> running = ClaudeApp.ProfileProcesses(account);

        if (running.Count > 0) {
            int pid = running[0];
            Taskbar.TryApply(account, pid);
            // Right after a launch the window may not exist yet, so give a fresh process a moment.
            if (AppWindow.Focus(pid, StartedRecently(pid) ? 15000 : 0)) return;

            // Closing Claude with X hides it to the tray. Forcing a hidden Claude window back into view
            // leaves it unresponsive, so offer a restart instead. "No" is the default.
            DialogResult answer = MessageBox.Show(
                "Claude Account " + account + " is still running in the background (its window was closed).\n\n" +
                "Restart it to get the window back? Anything running in this account will stop. " +
                "Your sign-in and settings are kept, and your other accounts are not touched.\n\n" +
                "Tip: minimize the window instead of closing it.",
                "Claude - Account " + account, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            using (Process old = Process.GetProcessById(pid)) {
                old.Kill();
                old.WaitForExit(10000);
            }
        }

        ProcessStartInfo info = new ProcessStartInfo(ClaudeApp.FindExecutable(), "--user-data-dir=\"" + profile + "\"");
        info.UseShellExecute = true;
        using (Process started = Process.Start(info)) {
            if (started == null) return;
            // Tag the new window as soon as it appears. Claude builds its main window in stages,
            // so tag once more shortly after.
            for (int attempt = 0; attempt < 80 && !started.HasExited; attempt++) {
                if (Taskbar.TryApply(account, started.Id) > 0) {
                    Thread.Sleep(1500);
                    Taskbar.TryApply(account, started.Id);
                    break;
                }
                Thread.Sleep(250);
            }
        }
    }

    static bool StartedRecently(int pid) {
        try {
            using (Process p = Process.GetProcessById(pid)) return (DateTime.Now - p.StartTime).TotalSeconds < 30;
        } catch (Exception) {
            return false;
        }
    }

    static void Status(int account) {
        string profile = ClaudeApp.ProfileDir(account);
        string exe;
        try { exe = ClaudeApp.FindExecutable(); } catch (Exception e) { exe = "NOT FOUND - " + e.Message.Split('\n')[0]; }
        List<int> running = ClaudeApp.ProfileProcesses(account);

        Console.WriteLine("Account       : " + account);
        Console.WriteLine("Profile folder: " + profile + (Directory.Exists(profile) ? "" : "  (not created yet)"));
        Console.WriteLine("Claude.exe    : " + exe);
        Console.WriteLine("Running       : " + (running.Count > 0 ? "yes" : "no"));
        Console.WriteLine("Taskbar id    : " + Taskbar.AppId(account));
        foreach (int pid in running) {
            foreach (IntPtr h in AppWindow.AppWindows(pid)) {
                string id;
                try { id = Taskbar.ReadAppId(h) ?? "(Claude default)"; } catch (Exception e) { id = "unreadable: " + e.Message; }
                Console.WriteLine("  window: visible=" + AppWindow.IsVisible(h) + ", taskbar id=" + id);
            }
        }
    }
}

// Finding Claude Desktop and the processes that belong to an account.
static class ClaudeApp {
    public static string ProfileDir(int account) {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeProfile-Account" + account);
    }

    class Proc {
        public int Id;
        public string Path;
        public string CommandLine;
    }

    // Claude Desktop's main processes. Chromium helper processes carry --type=..., and the Claude Code
    // command-line tool is also called claude.exe but lives elsewhere, so both are left out.
    static List<Proc> MainProcesses() {
        List<Proc> list = new List<Proc>();
        using (ManagementObjectSearcher query = new ManagementObjectSearcher(
                   "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'Claude.exe'"))
        using (ManagementObjectCollection results = query.Get()) {
            foreach (ManagementObject item in results) {
                Proc p = new Proc();
                p.Id = Convert.ToInt32(item["ProcessId"]);
                p.Path = item["ExecutablePath"] as string;
                p.CommandLine = item["CommandLine"] as string;
                item.Dispose();
                if (p.Path == null || p.CommandLine == null || p.CommandLine.Contains("--type=") || !IsDesktopApp(p.Path)) continue;
                list.Add(p);
            }
        }
        return list;
    }

    static bool IsDesktopApp(string path) {
        return path.IndexOf(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static List<int> ProfileProcesses(int account) {
        // The terminator stops ...Account2 from also matching ...Account20.
        Regex flag = new Regex("--user-data-dir=\"?" + Regex.Escape(ProfileDir(account)) + "(\"|\\s|$)", RegexOptions.IgnoreCase);
        List<int> ids = new List<int>();
        foreach (Proc p in MainProcesses()) {
            if (flag.IsMatch(p.CommandLine)) ids.Add(p.Id);
        }
        return ids;
    }

    // Looked up on every launch, so Claude updates (which change the install folder) never break the launcher.
    public static string FindExecutable() {
        // 1. Manual override: the full path to Claude.exe in claude-path.txt next to the launcher.
        string overrideFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claude-path.txt");
        if (File.Exists(overrideFile)) {
            string manual = File.ReadAllText(overrideFile).Trim().Trim('"');
            if (File.Exists(manual)) return manual;
            throw new Exception("claude-path.txt points to a file that does not exist:\n" + manual);
        }

        // 2. A Claude window that is already open shows exactly which install is current.
        foreach (Proc p in MainProcesses()) {
            if (File.Exists(p.Path)) return p.Path;
        }

        string best = null;
        Version bestVersion = null;

        // 3. Microsoft Store / MSIX install, found through the per-user package registry,
        //    because normal users cannot list C:\Program Files\WindowsApps itself.
        using (RegistryKey packages = Registry.CurrentUser.OpenSubKey(
                   @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")) {
            if (packages != null) {
                foreach (string name in packages.GetSubKeyNames()) {
                    if (!name.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] parts = name.Split('_');
                    Version version;
                    if (parts.Length < 2 || !Version.TryParse(parts[1], out version)) continue;
                    using (RegistryKey key = packages.OpenSubKey(name)) {
                        string root = key == null ? null : key.GetValue("PackageRootFolder") as string;
                        if (root == null) continue;
                        string exe = Path.Combine(root, @"app\Claude.exe");
                        if (File.Exists(exe) && (bestVersion == null || version > bestVersion)) {
                            best = exe;
                            bestVersion = version;
                        }
                    }
                }
            }
        }
        if (best != null) return best;

        // 4. Classic installer layout: %LOCALAPPDATA%\AnthropicClaude\app-<version>\claude.exe (not tested).
        string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicClaude");
        if (Directory.Exists(classic)) {
            foreach (string dir in Directory.GetDirectories(classic, "app-*")) {
                Version version;
                string exe = Path.Combine(dir, "claude.exe");
                if (Version.TryParse(Path.GetFileName(dir).Substring(4), out version) && File.Exists(exe)
                    && (bestVersion == null || version > bestVersion)) {
                    best = exe;
                    bestVersion = version;
                }
            }
        }
        if (best != null) return best;

        throw new Exception("Could not find Claude Desktop.\n\nInstall it and open it once, then try again. " +
                            "If it is installed somewhere unusual, put the full path to Claude.exe in claude-path.txt next to this launcher.");
    }
}

// Finding and focusing Claude's top-level windows.
static class AppWindow {
    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr hwnd, int index);

    const uint GW_OWNER = 4;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80;
    const int SW_RESTORE = 9;

    public static bool IsVisible(IntPtr hwnd) { return IsWindowVisible(hwnd); }

    // Top-level Electron windows (class Chrome_WidgetWin_1) of the process, without owned or tool windows.
    public static List<IntPtr> AppWindows(int pid) {
        List<IntPtr> list = new List<IntPtr>();
        EnumProc callback = delegate(IntPtr h, IntPtr unused) {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner != pid || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            long exStyle = IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() : GetWindowLong32(h, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;
            StringBuilder cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() == "Chrome_WidgetWin_1") list.Add(h);
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    static IntPtr VisibleMainWindow(int pid) {
        foreach (IntPtr h in AppWindows(pid)) {
            if (IsWindowVisible(h) && GetWindowTextLength(h) > 0) return h;
        }
        return IntPtr.Zero;
    }

    // Brings the account's visible (or minimized) window to the front. Returns false when the
    // window is hidden, which is what Claude does when you close it with X.
    public static bool Focus(int pid, int waitMilliseconds) {
        Stopwatch clock = Stopwatch.StartNew();
        IntPtr h = VisibleMainWindow(pid);
        while (h == IntPtr.Zero && clock.ElapsedMilliseconds < waitMilliseconds) {
            Thread.Sleep(250);
            h = VisibleMainWindow(pid);
        }
        if (h == IntPtr.Zero) return false;

        if (IsIconic(h)) {
            ShowWindowAsync(h, SW_RESTORE);
            Thread.Sleep(150);
        }
        BringWindowToTop(h);
        if (!SetForegroundWindow(h)) {
            uint ignored;
            uint current = GetCurrentThreadId();
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
            try {
                BringWindowToTop(h);
                SetForegroundWindow(h);
            } finally {
                if (attached) AttachThreadInput(current, foreground, false);
            }
        }
        return true;
    }
}

// Separate taskbar buttons through the Windows shell's AppUserModel properties.
static class Taskbar {
    // System.AppUserModel.* property keys from propkey.h.
    static readonly Guid AppUserModelFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    const uint RelaunchCommand = 2;
    const uint RelaunchIconResource = 3;
    const uint RelaunchDisplayName = 4;
    const uint AppUserModelId = 5;
    const short VT_LPWSTR = 31;

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(uint id) { FormatId = AppUserModelFormat; PropertyId = id; }
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    class TaskbarList { }

    [DllImport("shell32.dll", PreserveSig = false)]
    static extern void SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
    [DllImport("ole32.dll")]
    static extern int PropVariantClear(IntPtr value);

    public static string AppId(int account) { return "ClaudeMultiAccount.Account" + account; }

    static IntPtr NewVariant() {
        IntPtr v = Marshal.AllocCoTaskMem(32);
        for (int i = 0; i < 32; i++) Marshal.WriteByte(v, i, 0);
        return v;
    }

    static string Read(IPropertyStore store, uint id) {
        PropertyKey key = new PropertyKey(id);
        IntPtr v = NewVariant();
        try {
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, v));
            return Marshal.ReadInt16(v) == VT_LPWSTR ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(v, 8)) : null;
        } finally {
            PropVariantClear(v);
            Marshal.FreeCoTaskMem(v);
        }
    }

    // Writing null stores VT_EMPTY, which removes the override again.
    static void Write(IPropertyStore store, uint id, string text) {
        PropertyKey key = new PropertyKey(id);
        IntPtr v = NewVariant();
        try {
            if (text != null) {
                Marshal.WriteInt16(v, VT_LPWSTR);
                Marshal.WriteIntPtr(v, 8, Marshal.StringToCoTaskMemUni(text));
            }
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, v));
        } finally {
            PropVariantClear(v);
            Marshal.FreeCoTaskMem(v);
        }
    }

    static IPropertyStore WindowStore(IntPtr hwnd) {
        Guid iid = typeof(IPropertyStore).GUID;
        IPropertyStore store;
        SHGetPropertyStoreForWindow(hwnd, ref iid, out store);
        return store;
    }

    // Removes and re-adds the taskbar button so Windows regroups it under the new identity.
    static void RefreshButton(IntPtr hwnd) {
        if (!AppWindow.IsVisible(hwnd)) return;
        ITaskbarList taskbar = (ITaskbarList)new TaskbarList();
        try {
            taskbar.HrInit();
            taskbar.DeleteTab(hwnd);
            taskbar.AddTab(hwnd);
        } finally {
            Marshal.ReleaseComObject(taskbar);
        }
    }

    public static string ReadAppId(IntPtr hwnd) {
        IPropertyStore store = WindowStore(hwnd);
        try { return Read(store, AppUserModelId); } finally { Marshal.ReleaseComObject(store); }
    }

    // Tags every Claude window of the process. Returns how many windows now carry the account's identity.
    public static int Apply(int account, int pid) {
        string launcher = Application.ExecutablePath;
        string icon = Path.Combine(Path.GetDirectoryName(launcher), "Account" + account + ".ico");
        string iconResource = File.Exists(icon) ? icon + ",0" : launcher + ",0";
        int count = 0;
        foreach (IntPtr h in AppWindow.AppWindows(pid)) {
            IPropertyStore store = WindowStore(h);
            try {
                if (Read(store, AppUserModelId) != AppId(account)) {
                    // The relaunch properties tell a pinned button to reopen through this launcher.
                    Write(store, RelaunchCommand, "\"" + launcher + "\"");
                    Write(store, RelaunchIconResource, iconResource);
                    Write(store, RelaunchDisplayName, "Claude - Account " + account);
                    Write(store, AppUserModelId, AppId(account));
                    Marshal.ThrowExceptionForHR(store.Commit());
                    RefreshButton(h);
                }
                count++;
            } finally {
                Marshal.ReleaseComObject(store);
            }
        }
        return count;
    }

    public static int TryApply(int account, int pid) {
        try {
            return Apply(account, pid);
        } catch (Exception e) {
            Log.Error("Taskbar identity: " + e.Message);
            return 0;
        }
    }

    public static void Reset(int pid) {
        foreach (IntPtr h in AppWindow.AppWindows(pid)) {
            IPropertyStore store = WindowStore(h);
            try {
                Write(store, AppUserModelId, null);
                Write(store, RelaunchCommand, null);
                Write(store, RelaunchIconResource, null);
                Write(store, RelaunchDisplayName, null);
                Marshal.ThrowExceptionForHR(store.Commit());
                RefreshButton(h);
            } finally {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    // A shortcut with the same identity as the windows lets a pinned taskbar button and the open
    // windows share one button.
    public static void TagShortcut(string path, int account) {
        if (!File.Exists(path)) throw new Exception("Shortcut not found: " + path);
        object link = new ShellLink();
        try {
            IPersistFile file = (IPersistFile)link;
            file.Load(path, 2); // STGM_READWRITE
            IPropertyStore store = (IPropertyStore)link;
            Write(store, AppUserModelId, AppId(account));
            Marshal.ThrowExceptionForHR(store.Commit());
            file.Save(path, true);
            SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero); // SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH
        } finally {
            Marshal.ReleaseComObject(link);
        }
    }
}

// Errors only, written next to the launcher. No account data is ever logged.
static class Log {
    public static void Error(string message) {
        try {
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log"),
                               DateTime.Now.ToString("s") + "  " + message + Environment.NewLine);
        } catch (Exception) {
        }
    }
}
