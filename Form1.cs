using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Label = System.Windows.Forms.Label;

namespace PANEL_PRINTER
{
    public partial class Form1 : Form
    {
        private static readonly HttpClient client = new HttpClient();

        static Form1()
        {
            // Permite conectar a las impresoras por HTTPS ignorando certificados autofirmados
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3); // Timeout corto para no trabar el flujo
        }
        private string cuminity = "public";          // <-- Comunidad SNMP por defecto

        public Form1()
        {
            InitializeComponent();
        }

        private async void timerMonitoreo_Tick(object sender, EventArgs e)
        {
            // Apagamos el timer temporalmente para evitar que se acumulen ciclos si la red está lenta
            timerMonitoreo.Enabled = false;

            await Task.WhenAll(
                MonitorearAlertasHP(btnfinanzas, "10.91.51.30", "Finanzas"),
                MonitorearAlertasHP(btndireccion, "10.91.51.31", "Direccion"),
                MonitorearAlertasHP(btnbodyprod, "10.91.51.32", "Body Produccion"),
                MonitorearAlertasHP(btntracen, "10.91.51.33", "Training Center"),
                MonitorearAlertasHP(btnmafratrim, "10.91.51.34", "Mafra Trim"),
                MonitorearAlertasHP(btnmafrapintura, "10.91.51.35", "Pintura Mafra"),
                MonitorearAlertasHP(btnpressprod, "10.91.51.36", "Press Produccion"),
                MonitorearAlertasHP(btnrh, "10.91.51.37", "RH"),
                MonitorearAlertasHP(btnpilotajes, "10.91.51.38", "Pilotajes"),
                MonitorearAlertasHP(btnbotello, "10.91.51.39", "Botello"),
                MonitorearAlertasHP(btnceo, "10.91.51.40", "CEO"),
                MonitorearAlertasHP(btnccr, "10.91.51.41", "CCR")
            );
            // Volvemos a encender el timer para la siguiente revisión
            timerMonitoreo.Enabled = true;
        }


        private async Task MonitorearAlertasHP(Button boton, string ip, string nombreModelo)
        {
            // 1. VALIDACIÓN DE RED BÁSICA
            bool online = await RealizarPing(ip);
            if (!online)
            {
                MarcarBoton(boton, nombreModelo, "OFFLINE\n(Sin Red)", Color.DarkRed, Color.White, Color.Red, 10);
                return;
            }

            // Rutas estándar de consulta
            string[] rutasComunes = { "/DevMgmt/ProductStatusDyn.xml", "/start.htm", "/" };
            string[] protocolos = { "https", "http" };
            string contenidoWeb = "";

            foreach (var proto in protocolos)
            {
                foreach (var ruta in rutasComunes)
                {
                    try
                    {
                        string url = $"{proto}://{ip}{ruta}";
                        contenidoWeb = await client.GetStringAsync(url);
                        if (!string.IsNullOrEmpty(contenidoWeb) && contenidoWeb.Length > 100) break;
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(contenidoWeb)) break;
            }

            if (string.IsNullOrEmpty(contenidoWeb))
            {
                MarcarBoton(boton, nombreModelo, "ONLINE\nServicio Protegido", Color.Orange, Color.Black, Color.Orange , 2);
                return;
            }

            // 2. FILTRADO CORREGIDO Y EVOLUCIONADO (Evita Falsos Positivos)
            bool tieneAtasco = false;
            bool tienePuertaAbierta = false;
            bool tieneBandejaVacia = false;

            // Evaluamos si es un modelo HP (M610, M479, M477, M652) analizando su estructura XML
            if (contenidoWeb.Contains("<?xml") || contenidoWeb.Contains("DeviceStatus"))
            {
                // En HP, un atasco real cambia el DeviceStatus a "MediaJam" o "Jam" de forma aislada
                tieneAtasco = System.Text.RegularExpressions.Regex.IsMatch(contenidoWeb, @"<[^>]*DeviceStatus[^>]*>\s*(Jam|MediaJam|Mispick)\s*</", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Una puerta abierta real se marca explícitamente como "DoorOpen" o "Open" aislado, no como "NotOpen"
                tienePuertaAbierta = System.Text.RegularExpressions.Regex.IsMatch(contenidoWeb, @"<[^>]*DoorState[^>]*>\s*(Open|DoorOpen)\s*</", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                     System.Text.RegularExpressions.Regex.IsMatch(contenidoWeb, @"<[^>]*DeviceStatus[^>]*>\s*(DoorOpen)\s*</", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Bandeja vacía real
                tieneBandejaVacia = System.Text.RegularExpressions.Regex.IsMatch(contenidoWeb, @"<[^>]*TrayStatus[^>]*>\s*(Empty|Out)\s*</", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            else
            {
                // --- FILTRADO PARA LA KYOCERA (FS-9530DN) u otras marcas basadas en HTML ---
                // En el HTML clásico de Kyocera, los errores se muestran con frases imperativas exactas
                tieneAtasco = contenidoWeb.Contains("Atasco de papel") || contenidoWeb.Contains("Paper Jam!");
                tienePuertaAbierta = contenidoWeb.Contains("Cubierta abierta") || contenidoWeb.Contains("Cover Open");
                tieneBandejaVacia = contenidoWeb.Contains("Añadir papel") || contenidoWeb.Contains("Load Paper");
            }

            // 3. ASIGNACIÓN ESTRICTA DE COLORES EN EL PANEL
            if (tieneAtasco)
            {
                MarcarBoton(boton, nombreModelo, "⚠️ ATASCO DE PAPEL", Color.DarkRed, Color.White, Color.Red, 2);
            }
            else if (tienePuertaAbierta)
            {
                MarcarBoton(boton, nombreModelo, "🚪 PUERTA ABIERTA", Color.DarkRed, Color.White, Color.Red, 2);
            }
            else if (tieneBandejaVacia)
            {
                MarcarBoton(boton, nombreModelo, "📄 SIN PAPEL", Color.DarkRed, Color.White, Color.Red, 2);
            }
            else
            {
                // Si no cumple ninguna de las condiciones de error real, la máquina está operativa
                MarcarBoton(boton, nombreModelo, "✅ LISTO", Color.DarkGreen, Color.White, Color.Black, 100);
            }
        }

        private async Task<bool> RealizarPing(string ipAddress)
        {
            try
            {
                using (Ping pingSender = new Ping())
                {
                    PingReply reply = await pingSender.SendPingAsync(ipAddress, 1200);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        private void MarcarBoton(Button boton, string modelo, string textoEstado, Color fondo, Color texto, Color border, int size)
        {
            boton.BackColor = fondo;
            boton.ForeColor = texto;
            boton.FlatAppearance.BorderColor = border;
            boton.FlatAppearance.BorderSize = size;
            boton.Text = $"{modelo}\n{textoEstado}";
        }

        private async void InitializeAsync()
        {
           
            // Opción B: Cargar HTML como texto directamente con CSS incluido
            // string htmlConCss = "<html><head><style>body{background-color:powderblue;}</style></head><body><h1>Hola</h1></body></html>";
            // webView21.CoreWebView2.NavigateToString(htmlConCss);
        }



        // Función para cambiar el diseño del botón cuando la impresora se cae


        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
          
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            timerMonitoreo_Tick(this, EventArgs.Empty);

            // Inicializa el entorno del WebView2
            await webView21.EnsureCoreWebView2Async(null);

            string carpetaEjecucion = AppDomain.CurrentDomain.BaseDirectory;

            webView21.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "miapp.localhost",
            carpetaEjecucion,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
   );
            webView21.CoreWebView2.Navigate("https://miapp.localhost//index.html");


            // Vinculamos cada Label con la IP de su respectiva impresora
            List<(System.Windows.Forms.Label Etiqueta, string IP)> impresoras = new List<(System.Windows.Forms.Label, string)>
            {
                (lbl1, "10.91.51.32"),
                (lbl2, "10.91.51.35"),
                (lbl3, "10.91.51.36"),
                (lbl4, "10.91.51.38"),
                (lbl5, "10.91.51.39"),
            };

            var tareas = new List<Task>();
            foreach (var imp in impresoras)
            {
                tareas.Add(ConsultarTonerAsync(imp.Etiqueta, imp.IP));
            }

            await Task.WhenAll(tareas);

            EstablecerTextoCargando();

            // Ejecutamos las 5 consultas en paralelo (al mismo tiempo en la red)
            // Cada llamada recibe la IP de la impresora y sus 4 etiquetas específicas
            var tarea1 = ProcesarImpresoraAsync("10.91.51.30", lbl7negro, lbl7cian, lbl7magenta, lbl7amarillo);
            var tarea2 = ProcesarImpresoraAsync("10.91.51.31", lbl8negro, lbl8cian, lbl8magenta, lbl8amarillo);
            var tarea3 = ProcesarImpresoraAsync("10.91.51.34", lbl9negro, lbl9cian, lbl9magenta, lbl9amarillo);
            var tarea4 = ProcesarImpresoraAsync("10.91.51.37", lbl6negro, lbl6cian, lbl6magenta, lbl6amarillo);
            var tarea5 = ProcesarImpresoraAsync("10.91.51.33", lbl10negro, lbl10cian, lbl10magenta, lbl10amarillo); // <-- Nueva impresora

            // Esperamos a que todas terminen sin bloquear la interfaz de usuario
            await Task.WhenAll(tarea1, tarea2, tarea3, tarea4, tarea5);


            /////////////////KYOCERA////////////////////////////
            ///

            string ipImpresora = "10.91.51.41"; // Asegúrate de cambiar esto por la IP real de tu equipo
            lbl11.Text = "...";

            int porcentaje = await Task.Run(() => GetKyoceraTonerPercentage(ipImpresora));

            if (porcentaje >= 0)
            {
                lbl11.Text = $"Tóner: {porcentaje}%";
            }
            else if (porcentaje == -2)
            {
                lbl11.Text = "ERROR";
            }
            else if (porcentaje == -4)
            {
                lbl11.Text = "ERROR OIDs";
            }
            else
            {
                lbl11.Text = "Timeout";
            }
        }

        private async Task ProcesarImpresoraAsync(string ip, Label lblK, Label lblC, Label lblM, Label lblY)
        {
            try
            {
                var tóners = await ObtenerPorcentajesColorAsync(ip, cuminity);

                // Asignamos el porcentaje calculado directamente a cada Label correspondiente
                lblK.Text = $"{tóners["Negro"]}%";
                lblC.Text = $"{tóners["Cian"]}%";
                lblM.Text = $"{tóners["Magenta"]}%";
                lblY.Text = $"{tóners["Amarillo"]}%";
            }
            catch (Exception)
            {
                // Si la impresora falla o está apagada, su grupo específico de etiquetas mostrará "Error"
                lblK.Text = lblC.Text = lblM.Text = lblY.Text = "Error";
            }
        }

        private Task<Dictionary<string, int>> ObtenerPorcentajesColorAsync(string ipAddress, string community)
        {
            return Task.Run(() =>
            {
                var oids = new List<Variable>
                {
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.8.1.1")), // Max K
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.9.1.1")), // Act K
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.8.1.2")), // Max C
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.9.1.2")), // Act C
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.8.1.3")), // Max M
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.9.1.3")), // Act M
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.8.1.4")), // Max Y
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.43.11.1.1.9.1.4"))  // Act Y
                };

                IPAddress ip = IPAddress.Parse(ipAddress);
                var endpoint = new IPEndPoint(ip, 161);

                // Timeout de 2 segundos para descartar rápidamente impresoras desconectadas
                var resultado = Messenger.Get(VersionCode.V2, endpoint, new OctetString(community), oids, 2000);

                return new Dictionary<string, int>
                {
                    { "Negro", CalcularPorcentaje(resultado[0].Data, resultado[1].Data) },
                    { "Cian", CalcularPorcentaje(resultado[2].Data, resultado[3].Data) },
                    { "Magenta", CalcularPorcentaje(resultado[4].Data, resultado[5].Data) },
                    { "Amarillo", CalcularPorcentaje(resultado[6].Data, resultado[7].Data) }
                };
            });
        }

        private int CalcularPorcentaje(ISnmpData maxData, ISnmpData actualData)
        {
            int max = int.Parse(maxData.ToString());
            int actual = int.Parse(actualData.ToString());

            if (max <= 0 || actual < 0) return 0;

            return (actual * 100) / max;
        }

        private void EstablecerTextoCargando()
        {
            lbl7negro.Text = lbl7cian.Text = lbl7magenta.Text = lbl7amarillo.Text = "...";
            lbl8negro.Text = lbl8cian.Text = lbl8magenta.Text = lbl8amarillo.Text = "...";
            lbl9negro.Text = lbl9cian.Text = lbl9magenta.Text = lbl9amarillo.Text = "...";
            lbl6negro.Text = lbl6cian.Text = lbl6magenta.Text = lbl6amarillo.Text = "...";
            lbl10negro.Text = lbl10cian.Text = lbl10magenta.Text = lbl10amarillo.Text = "..."; // Nueva
        }



        private async Task ConsultarTonerAsync(System.Windows.Forms.Label etiqueta, string ip)
        {
            string community = "public";
            etiqueta.Text = "..."; // Texto temporal mientras conecta

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);
                var oids = new List<Variable> { new Variable(new ObjectIdentifier("1.3.6.1.2.1.43.11.1.1.9.1.1")) };

                IList<Variable> results = await Task.Run(() =>
                    Messenger.Get(VersionCode.V2, endpoint, new OctetString(community), oids, 30000)
                );

                string tonerLevel = results[0].Data.ToString();

                if (tonerLevel == "-2" || tonerLevel == "-3")
                {
                    etiqueta.Text = "N/D";
                }
                else
                {
                    etiqueta.Text = $"TONER: {tonerLevel}%";
                }
            }
            catch (Exception)
            {
                etiqueta.Text = "Err"; // Indica error de conexión o impresora apagada
            }
        }

        /// <summary>
        /// ////////KYOCERA////////////////////////////////////
        /// 

        private int GetKyoceraTonerPercentage(string printerIp, string community = "public")
        {
            try
            {
                IPAddress ip = IPAddress.Parse(printerIp);
                OctetString communityString = new OctetString(community);
                IPEndPoint endpoint = new IPEndPoint(ip, 161);

                // Intento 1: Probar con los OIDs universales estándar de impresora
                string maxOidStr = ".1.3.6.1.2.1.43.11.1.1.8.1.1";
                string curOidStr = ".1.3.6.1.2.1.43.11.1.1.9.1.1";

                var maxResult = Messenger.Get(VersionCode.V2, endpoint, communityString, new List<Variable> { new Variable(new ObjectIdentifier(maxOidStr)) }, 2000);
                var curResult = Messenger.Get(VersionCode.V2, endpoint, communityString, new List<Variable> { new Variable(new ObjectIdentifier(curOidStr)) }, 2000);

                string maxStr = maxResult[0].Data.ToString();
                string curStr = curResult[0].Data.ToString();

                // Si la respuesta no es numérica, saltar al catch interno para intentar los OIDs privados de Kyocera
                if (!int.TryParse(maxStr, out int maxCapacity) || !int.TryParse(curStr, out int currentLevel))
                {
                    throw new Exception("OIDs estandar no soportados. Intentando privados...");
                }

                if (currentLevel == -2 || currentLevel == -3) return -2;
                if (maxCapacity <= 0 || currentLevel < 0) return -1;

                return (currentLevel * 100) / maxCapacity;
            }
            catch
            {
                // Intento 2: Si falló lo anterior, usar los OIDs privados del fabricante Kyocera
                try
                {
                    IPAddress ip = IPAddress.Parse(printerIp);
                    OctetString communityString = new OctetString(community);
                    IPEndPoint endpoint = new IPEndPoint(ip, 161);

                    // OIDs privados de Kyocera MIB para consumibles
                    string maxOidKyocera = ".1.3.6.1.4.1.1347.43.11.1.1.8.1.1";
                    string curOidKyocera = ".1.3.6.1.4.1.1347.43.11.1.1.9.1.1";

                    var maxResult = Messenger.Get(VersionCode.V2, endpoint, communityString, new List<Variable> { new Variable(new ObjectIdentifier(maxOidKyocera)) }, 2000);
                    var curResult = Messenger.Get(VersionCode.V2, endpoint, communityString, new List<Variable> { new Variable(new ObjectIdentifier(curOidKyocera)) }, 2000);

                    int maxCapacity = int.Parse(maxResult[0].Data.ToString());
                    int currentLevel = int.Parse(curResult[0].Data.ToString());

                    if (currentLevel == -2 || currentLevel == -3) return -2;
                    if (maxCapacity <= 0 || currentLevel < 0) return -1;

                    return (currentLevel * 100) / maxCapacity;
                }
                catch
                {
                    return -4; // Código de error interno: OIDs completamente incompatibles
                }
            }
        }



        private void btnccr_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void webView21_Click(object sender, EventArgs e)
        {

        }
    }
}
