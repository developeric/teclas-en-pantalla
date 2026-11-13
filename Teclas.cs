using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Teclas: muestra las teclas pulsadas.
//  - En pantalla: ventanita flotante que solo aparece mientras pulsas algo.
//  - Para OBS: página en http://localhost:8765/ para añadir como "Navegador" (fondo transparente).
// Se controla desde el icono junto al reloj (bandeja del sistema).
class Teclas : Form
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    const int Puerto = 8765;
    static readonly string Url = "http://localhost:" + Puerto + "/";

    readonly Label label = new Label();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly List<string> pressed = new List<string>(); // en orden de pulsación
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ToolStripMenuItem itemPantalla;
    readonly ToolStripMenuItem itemMover;
    string lastText = null;
    bool mostrarEnPantalla;
    bool moviendo;

    // Texto actual compartido con el servidor para OBS
    static readonly object cambio = new object();
    static string actual = "";
    static int version = 0;

    static readonly Color Verde = Color.FromArgb(22, 163, 74);

    Teclas(bool pantalla)
    {
        mostrarEnPantalla = pantalla;

        Text = "Teclas";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Verde;
        Padding = new Padding(24, 12, 24, 12);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        label.AutoSize = true;
        label.ForeColor = Color.White;
        label.Font = new Font("Segoe UI", 32, FontStyle.Bold);
        label.Location = new Point(24, 12);
        Controls.Add(label);

        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Left + 40, wa.Bottom - 140);

        // Arrastrar con clic izquierdo
        MouseEventHandler drag = (s, e) =>
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
        };
        MouseDown += drag;
        label.MouseDown += drag;

        // Menú (icono de la bandeja y clic derecho en la ventana)
        var menu = new ContextMenuStrip();
        itemPantalla = new ToolStripMenuItem("Mostrar en mi pantalla", null, (s, e) => SetPantalla(!mostrarEnPantalla));
        itemPantalla.Checked = mostrarEnPantalla;
        itemMover = new ToolStripMenuItem("Mover ventana", null, (s, e) => SetMover(!moviendo));
        menu.Items.Add(itemPantalla);
        menu.Items.Add(itemMover);
        menu.Items.Add("Copiar enlace para OBS", null, (s, e) =>
        {
            try { Clipboard.SetText(Url); } catch { }
            tray.ShowBalloonTip(3000, "Teclas", "Enlace copiado: " + Url + "\nEn OBS: Fuente > Navegador > pega el enlace.", ToolTipIcon.Info);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Cerrar", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        ContextMenuStrip = menu;
        label.ContextMenuStrip = menu;

        tray.Icon = CrearIcono();
        tray.Text = "Teclas (OBS: " + Url + ")";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;

        timer.Interval = 15;
        timer.Tick += (s, e) => Poll();
        timer.Start();
    }

    // No robar el foco al aparecer, no salir en Alt+Tab, siempre encima
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/;
            return cp;
        }
    }

    void SetPantalla(bool v)
    {
        mostrarEnPantalla = v;
        itemPantalla.Checked = v;
        lastText = null;
        Render();
    }

    void SetMover(bool v)
    {
        moviendo = v;
        itemMover.Checked = v;
        itemMover.Text = v ? "Fijar posición" : "Mover ventana";
        lastText = null;
        Render();
    }

    void Poll()
    {
        var now = new HashSet<string>();
        for (int vk = 0x08; vk <= 0xFE; vk++)
        {
            // Saltar las genéricas Shift/Ctrl/Alt (usamos las variantes L/R)
            if (vk == 0x10 || vk == 0x11 || vk == 0x12) continue;
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            string name = KeyName(vk);
            if (name != null) now.Add(name);
        }

        pressed.RemoveAll(k => !now.Contains(k));
        foreach (var k in now) if (!pressed.Contains(k)) pressed.Add(k);
        Render();
    }

    void Render()
    {
        string text = string.Join("  +  ", pressed);
        if (text == lastText) return;
        lastText = text;

        lock (cambio) { actual = text; version++; Monitor.PulseAll(cambio); }

        string mostrar = text.Length > 0 ? text : (moviendo ? "Arrástrame" : "");
        bool visible = mostrar.Length > 0 && (mostrarEnPantalla || moviendo);
        if (visible) { label.Text = mostrar; if (!Visible) Show(); }
        else if (Visible) Hide();
    }

    static Icon CrearIcono()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        using (var f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var b = new SolidBrush(Verde)) g.FillRectangle(b, 2, 6, 28, 20);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("T", f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static string KeyName(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);           // teclado numérico
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24

        switch (vk)
        {
            case 0x08: return "Retroceso";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x13: return "Pausa";
            case 0x14: return "Bloq Mayús";
            case 0x1B: return "Esc";
            case 0x20: return "Espacio";
            case 0x21: return "Re Pág";
            case 0x22: return "Av Pág";
            case 0x23: return "Fin";
            case 0x24: return "Inicio";
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x2C: return "Impr Pant";
            case 0x2D: return "Insert";
            case 0x2E: return "Supr";
            case 0x5B: case 0x5C: return "Win";
            case 0x5D: return "Menú";
            case 0x6A: return "Num *";
            case 0x6B: return "Num +";
            case 0x6D: return "Num -";
            case 0x6E: return "Num .";
            case 0x6F: return "Num /";
            case 0x90: return "Bloq Num";
            case 0x91: return "Bloq Despl";
            case 0xA0: case 0xA1: return "Shift";
            case 0xA2: case 0xA3: return "Ctrl";
            case 0xA4: return "Alt";
            case 0xA5: return "Alt Gr";
            case 0xAD: return "Silenciar";
            case 0xAE: return "Vol -";
            case 0xAF: return "Vol +";
            case 0xB0: return "Siguiente";
            case 0xB1: return "Anterior";
            case 0xB3: return "Play/Pausa";
        }

        // Teclas de símbolos (ñ, ´, ç, etc.) según la distribución del teclado
        if ((vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDF) || vk == 0xE2)
        {
            uint ch = MapVirtualKey((uint)vk, 2) & 0x7FFFFFFF;
            if (ch != 0) return char.ToUpper((char)ch).ToString();
        }
        return null; // ratón y teclas desconocidas se ignoran
    }

    // ---------- Servidor local para OBS (solo accesible desde este PC) ----------

    const string Html = @"<!doctype html>
<html><head><meta charset='utf-8'><title>Teclas</title><style>
html,body{margin:0;background:transparent;overflow:hidden}
#k{display:none;position:absolute;left:20px;bottom:20px;padding:12px 28px;border-radius:16px;
background:#16a34a;color:#fff;font:700 48px 'Segoe UI',Arial,sans-serif;white-space:nowrap;
box-shadow:0 4px 14px rgba(0,0,0,.35)}
</style></head><body><div id='k'></div><script>
var k=document.getElementById('k');
var es=new EventSource('/stream');
es.onmessage=function(e){if(e.data){k.textContent=e.data;k.style.display='inline-block';}else{k.style.display='none';}};
es.onerror=function(){k.style.display='none';};
</script></body></html>";

    static TcpListener IniciarServidor()
    {
        var l = new TcpListener(IPAddress.Loopback, Puerto);
        l.Start();
        var t = new Thread(() =>
        {
            while (true)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                var h = new Thread(Atender);
                h.IsBackground = true;
                h.Start(c);
            }
        });
        t.IsBackground = true;
        t.Start();
        return l;
    }

    static void Atender(object o)
    {
        using (var c = (TcpClient)o)
        {
            try
            {
                var s = c.GetStream();
                var r = new StreamReader(s, Encoding.ASCII);
                string line = r.ReadLine();
                if (line == null) return;
                string h;
                while (!string.IsNullOrEmpty(h = r.ReadLine())) { }
                var parts = line.Split(' ');
                string path = parts.Length > 1 ? parts[1] : "/";

                if (path.StartsWith("/stream"))
                {
                    Escribir(s, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\n" +
                                "Cache-Control: no-store\r\nConnection: keep-alive\r\n\r\n");
                    int vista = -1;
                    while (true)
                    {
                        string texto;
                        lock (cambio)
                        {
                            if (vista == version) Monitor.Wait(cambio, 10000);
                            texto = actual;
                            vista = version;
                        }
                        Escribir(s, "data: " + texto + "\n\n"); // también sirve de latido cada 10 s
                    }
                }

                byte[] body = Encoding.UTF8.GetBytes(Html);
                Escribir(s, "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " +
                            body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                s.Write(body, 0, body.Length);
            }
            catch { } // OBS cerró la conexión
        }
    }

    static void Escribir(Stream s, string texto)
    {
        byte[] b = Encoding.UTF8.GetBytes(texto);
        s.Write(b, 0, b.Length);
        s.Flush();
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool nuevo;
        using (var mutex = new Mutex(true, "Teclas_App_Unica", out nuevo))
        {
            if (!nuevo)
            {
                MessageBox.Show("Teclas ya está abierta. Búscala en el icono verde \"T\" junto al reloj.", "Teclas");
                return;
            }

            bool pantalla = Array.IndexOf(args ?? new string[0], "-obs") < 0;
            Application.EnableVisualStyles();

            TcpListener servidor = null;
            try { servidor = IniciarServidor(); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo iniciar el enlace para OBS (puerto " + Puerto + " ocupado).\n" + ex.Message, "Teclas");
            }

            var app = new Teclas(pantalla);
            app.tray.ShowBalloonTip(4000, "Teclas activa",
                (pantalla ? "Se ve en tu pantalla y en OBS." : "Solo para OBS (no se ve en tu pantalla).") +
                "\nEnlace OBS: " + Url + "\nClic derecho en el icono para opciones.", ToolTipIcon.Info);
            Application.Run();

            app.tray.Visible = false;
            if (servidor != null) servidor.Stop();
        }
    }
}
