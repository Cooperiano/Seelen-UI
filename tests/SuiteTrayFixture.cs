// Local integration fixture: owns only its own notify icon and named test events.
// Does not control other apps, record input, or change tray promotion preferences.
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

internal static class SuiteTrayFixture {
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private static Icon NumberIcon(int index) {
        using (var bitmap = new Bitmap(32, 32))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(SystemColors.WindowText))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
            graphics.Clear(SystemColors.Window);
            graphics.DrawString(index.ToString("D2"), font, brush, new RectangleF(0, 0, 32, 32), format);
            var handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    [STAThread]
    private static void Main(string[] args) {
        int count = 1;
        if (args.Length > 0 && (!int.TryParse(args[0], out count) || count < 1 || count > 64))
            throw new ArgumentException("Count must be between 1 and 64.");
        var icons = new List<NotifyIcon>();
        var numberedIcons = new List<Icon>();
        // Opt-in, fixture-local receipts only. Never capture global user input.
        bool trace = args.Length > 1 && args[1] == "--trace";
        var receipt = Path.ChangeExtension(Application.ExecutablePath, ".clicks.log");
        if (trace) File.AppendAllText(receipt, "start " + Process.GetCurrentProcess().Id + Environment.NewLine);
        var elapsed = Stopwatch.StartNew();
        using (var hide = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\StatsSuite.TrayFixture.Hide"))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\StatsSuite.TrayFixture.Show"))
        using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\StatsSuite.TrayFixture.Quit"))
        using (var timer = new System.Windows.Forms.Timer()) {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Tray fixture - no application actions").Enabled = false;
            for (int i = 0; i < count; i++) {
                var icon = new NotifyIcon();
                icons.Add(icon);
                icon.Text = count == 1 ? "Stats Desktop Tray Test" : "Stats Desktop Tray Test " + (i + 1).ToString("D2");
                icon.Icon = i % 3 == 0 ? SystemIcons.Information : i % 3 == 1 ? SystemIcons.Warning : SystemIcons.Error;
                if (trace) {
                    int index = i + 1;
                    var numbered = NumberIcon(index);
                    numberedIcons.Add(numbered);
                    icon.Icon = numbered;
                    icon.MouseClick += delegate(object sender, MouseEventArgs e) {
                        File.AppendAllText(receipt, index.ToString("D2") + " " + e.Button + Environment.NewLine);
                    };
                }
                icon.ContextMenuStrip = menu;
                icon.Visible = true;
            }
            timer.Interval = 100;
            timer.Tick += delegate {
                if (hide.WaitOne(0)) foreach (var icon in icons) icon.Visible = false;
                if (show.WaitOne(0)) foreach (var icon in icons) icon.Visible = true;
                if (quit.WaitOne(0) || (count > 1 && elapsed.Elapsed.TotalSeconds >= (trace ? 300 : 120))) Application.Exit();
            };
            timer.Start();
            try { Application.Run(); }
            finally {
                foreach (var icon in icons) { icon.Visible = false; icon.Dispose(); }
                foreach (var icon in numberedIcons) icon.Dispose();
                menu.Dispose();
            }
        }
    }
}
