using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class UpdateManifest
{
    public string Product, Version, Url, Sha256, Signature;
    public long Size;
}

sealed class UpdateJob
{
    public UpdateManifest Manifest;
    public string TargetExe, StagedExe, ReadyFile;
    public int ParentPid;
    public long ParentStarted;
}

sealed class StagedUpdate
{
    internal UpdateManifest Manifest;
    internal string Folder, Exe;
}

static class UpdateTrust
{
    internal const string Product = "SilkroadRStudio";
    internal const string DefaultManifestUrl = "https://github.com/gabbybilisim-cloud/NewEra-Client/releases/latest/download/manifest.json";
    // Releases of the original author were signed with a different key; settings that still point there are migrated.
    internal const string LegacyManifestUrl = "https://github.com/by-rikimaru/NewEra/releases/latest/download/manifest.json";
    internal const long MaxExeSize = 150 * 1024 * 1024;

    internal static string EmbeddedPublicKey()
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("updates-public-key.xml"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
    }

    internal static Version CurrentVersion
    {
        get { return Assembly.GetExecutingAssembly().GetName().Version; }
    }

    internal static byte[] Canonical(UpdateManifest m)
    {
        return Encoding.UTF8.GetBytes(m.Product + "\n" + m.Version + "\n" + m.Url + "\n" + m.Sha256.ToUpperInvariant() +
            "\n" + m.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
    }

    internal static void Validate(UpdateManifest m)
    {
        Validate(m, EmbeddedPublicKey());
    }

    internal static void Validate(UpdateManifest m, string publicKeyXml)
    {
        Version ver;
        Uri url;
        if (m == null || m.Product != Product || !Version.TryParse(m.Version, out ver) || m.Version.Length > 32 || m.Size < 1024 ||
            m.Size > MaxExeSize || m.Sha256 == null || !Regex.IsMatch(m.Sha256, @"\A[0-9a-fA-F]{64}\z") || !Uri.TryCreate(m.Url,
            UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps || m.Url.Length > 4096 || m.Url.Contains('\n') ||
            m.Url.Contains('\r'))
            throw new InvalidDataException("Geçersiz güncelleme bildirimi.");
        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.FromXmlString(publicKeyXml);
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(m.Signature ?? "");
            }
            catch
            {
                throw new InvalidDataException("Güncelleme imzası geçersiz.");
            }
            if (!rsa.VerifyData(Canonical(m), CryptoConfig.MapNameToOID("SHA256"), signature))
                throw new InvalidDataException("Güncellemenin yayıncı imzası doğrulanamadı.");
        }
    }

    internal static string Hash(string path)
    {
        using (var f = File.OpenRead(path))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "");
    }

    internal static void VerifyFile(string path, UpdateManifest m)
    {
        Validate(m);
        if (new FileInfo(path).Length != m.Size || !string.Equals(Hash(path), m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("İndirilen EXE bütünlüğü doğrulanamadı.");
        if (AssemblyName.GetAssemblyName(path).Version != Version.Parse(m.Version))
            throw new InvalidDataException("EXE sürümü bildirimle eşleşmiyor.");
    }
}

static class UpdateService
{
    internal static string Root
    {
        get { return Path.Combine(V3Settings.DirectoryPath, "updates"); }
    }

    internal static string Acknowledgment;

    internal static bool OwnedPath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path), root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static HttpWebResponse Open(string address)
    {
        Uri uri = new Uri(address);
        for (int i = 0; i < 6; i++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new IOException("Güncelleme adresi HTTPS olmalı.");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Timeout = 15000;
            request.ReadWriteTimeout = 20000;
            request.AllowAutoRedirect = false;
            request.UserAgent = "SilkroadRStudio/" + UpdateTrust.CurrentVersion;
            var response = (HttpWebResponse)request.GetResponse();
            int code = (int)response.StatusCode;
            if (code >= 300 && code < 400)
            {
                string next = response.Headers["Location"];
                response.Dispose();
                if (string.IsNullOrEmpty(next))
                    throw new IOException("Eksik yönlendirme.");
                uri = new Uri(uri, next);
                continue;
            }
            if (code != 200)
            {
                response.Dispose();
                throw new IOException("Güncelleme sunucusu yanıt vermedi.");
            }
            return response;
        }
        throw new IOException("Çok fazla indirme yönlendirmesi.");
    }

    static void CopyBounded(Stream from, Stream to, long max)
    {
        var buffer = new byte[65536];
        long total = 0;
        int n;
        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            if (total > max)
                throw new IOException("Güncelleme dosyası boyut sınırını aştı.");
            to.Write(buffer, 0, n);
        }
    }

    internal static StagedUpdate Check(string manifestUrl)
    {
        Uri uri;
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("HTTPS güncelleme adresini ayarla.");
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        UpdateManifest m;
        using (var response = Open(manifestUrl))
            using (var stream = response.GetResponseStream())
                using (var data = new MemoryStream())
                {
                    CopyBounded(stream, data, 65536);
                    m = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 12 }.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(data.ToArray()));
                }
        UpdateTrust.Validate(m);
        if (Version.Parse(m.Version) <= UpdateTrust.CurrentVersion)
            return null;
        string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, "application.exe");
        using (var response = Open(m.Url))
            using (var stream = response.GetResponseStream())
                using (var target = new FileStream(exe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (response.ContentLength > m.Size)
                        throw new InvalidDataException("Dosya boyutu bildirimden büyük.");
                    CopyBounded(stream, target, m.Size);
                }
        UpdateTrust.VerifyFile(exe, m);
        return new StagedUpdate { Manifest = m, Folder = folder, Exe = exe };
    }

    internal static void LaunchWorker(StagedUpdate update)
    {
        UpdateTrust.VerifyFile(update.Exe, update.Manifest);
        if (!OwnedPath(update.Exe))
            throw new IOException("Güncelleme hazırlık yolu geçersiz.");
        string worker = Path.Combine(update.Folder, "worker.exe"), jobPath = Path.Combine(update.Folder, "job.json"),
            target = Assembly.GetExecutingAssembly().Location;
        File.Copy(target, worker, false);
        if (UpdateTrust.Hash(worker) != UpdateTrust.Hash(target))
            throw new IOException("Güncelleme yardımcısı doğrulanamadı.");
        using (var parent = Process.GetCurrentProcess())
        {
            var job = new UpdateJob { Manifest = update.Manifest, TargetExe = target, StagedExe = update.Exe, ReadyFile =
                Path.Combine(update.Folder, "ready.txt"), ParentPid = parent.Id, ParentStarted = parent.StartTime.ToUniversalTime()
                .Ticks };
            File.WriteAllText(jobPath, new JavaScriptSerializer().Serialize(job), Encoding.UTF8);
        }
        Process.Start(new ProcessStartInfo(worker, "--apply-update " + Quote(jobPath)) { UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = update.Folder });
    }

    static string Quote(string path)
    {
        if (path.Contains('"'))
            throw new IOException("Geçersiz dosya yolu.");
        return "\"" + path + "\"";
    }

    internal static void Acknowledge()
    {
        if (Acknowledgment == null)
            return;
        if (!OwnedPath(Acknowledgment) || Path.GetFileName(Acknowledgment) != "ready.txt")
            throw new IOException("Güncelleme doğrulama yolu geçersiz.");
        File.WriteAllText(Acknowledgment, UpdateTrust.CurrentVersion.ToString(), Encoding.UTF8);
    }

    internal static void Apply(string jobPath)
    {
        string target = null, temp = null, backup = null;
        Process child = null;
        bool replaced = false, targetVerified = false, parentExited = false;
        try
        {
            if (!OwnedPath(jobPath) || Path.GetFileName(jobPath) != "job.json" || new FileInfo(jobPath).Length > 65536)
                throw new IOException("Güncelleme işi yolu geçersiz.");
            var job = new JavaScriptSerializer().Deserialize<UpdateJob>(File.ReadAllText(jobPath, Encoding.UTF8));
            target = Path.GetFullPath(job.TargetExe);
            string folder = Path.GetDirectoryName(Path.GetFullPath(jobPath));
            if (!OwnedPath(job.StagedExe) || !OwnedPath(job.ReadyFile) || Path.GetDirectoryName(Path.GetFullPath(job.StagedExe)) != folder ||
                Path.GetDirectoryName(Path.GetFullPath(job.ReadyFile)) != folder || Path.GetFileName(job.StagedExe) != "application.exe" ||
                Path.GetFileName(job.ReadyFile) != "ready.txt" || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                OwnedPath(target))
                throw new IOException("Güncelleme işi alanları geçersiz.");
            UpdateTrust.VerifyFile(job.StagedExe, job.Manifest);
            if (Version.Parse(job.Manifest.Version) <= UpdateTrust.CurrentVersion)
                throw new IOException("Eski sürüme geçiş reddedildi.");
            if (UpdateTrust.Hash(target) != UpdateTrust.Hash(Assembly.GetExecutingAssembly().Location))
                throw new IOException("Hedef dosya bu uygulamayla eşleşmiyor; değiştirilmedi.");
            targetVerified = true;
            try
            {
                using (var parent = Process.GetProcessById(job.ParentPid))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStarted)
                        throw new IOException("Üst işlem kimliği değişti.");
                    if (!parent.WaitForExit(30000))
                        throw new IOException("Eski uygulama kapanmadı; dosya değiştirilmedi.");
                }
            }
            catch (ArgumentException) { }
            parentExited = true;
            if (UpdateTrust.Hash(target) != UpdateTrust.Hash(Assembly.GetExecutingAssembly().Location))
                throw new IOException("Hedef dosya bu uygulamayla eşleşmiyor; değiştirilmedi.");
            temp = Path.Combine(Path.GetDirectoryName(target), ".silkroadrstudio-update-" + Guid.NewGuid().ToString("N") +
                ".tmp");
            backup = target + ".previous.exe";
            File.Copy(job.StagedExe, temp, false);
            UpdateTrust.VerifyFile(temp, job.Manifest);
            File.Replace(temp, target, backup, true);
            replaced = true;
            child = Process.Start(new ProcessStartInfo(target, "--updated " + Quote(job.ReadyFile)) { UseShellExecute =
                false, WorkingDirectory = Path.GetDirectoryName(target) });
            var wait = Stopwatch.StartNew();
            bool ready = false;
            while (wait.ElapsedMilliseconds < 15000)
            {
                if (child == null || child.HasExited)
                    break;
                if (File.Exists(job.ReadyFile) && File.ReadAllText(job.ReadyFile, Encoding.UTF8).Trim() == job.Manifest.Version)
                {
                    ready = true;
                    break;
                }
                Thread.Sleep(100);
            }
            if (!ready)
                throw new IOException("Yeni sürüm açılışı doğrulanmadı.");
            WriteLog("Güncelleme uygulandı: " + job.Manifest.Version);
        }
        catch (Exception e)
        {
            WriteLog("Güncelleme uygulanamadı: " + e.Message);
            if (replaced && target != null && backup != null && File.Exists(backup))
            {
                try
                {
                    if (child != null && !child.HasExited)
                    {
                        child.Kill();
                        child.WaitForExit(5000);
                    }
                    if (UpdateTrust.Hash(backup) == UpdateTrust.Hash(Assembly.GetExecutingAssembly().Location))
                        File.Replace(backup, target, null, true);
                }
                catch (Exception rollback)
                {
                    WriteLog("Geri alma: " + rollback.Message);
                }
            }
            if (targetVerified && parentExited && target != null && File.Exists(target) && UpdateTrust.Hash(target) == UpdateTrust.Hash(Assembly.GetExecutingAssembly()
                .Location))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
                }
                catch { }
            }
        }
    }

    static void WriteLog(string message)
    {
        try
        {
            Directory.CreateDirectory(V3Settings.DirectoryPath);
            File.AppendAllText(Path.Combine(V3Settings.DirectoryPath, "update.log"), DateTime.Now + " · " + message +
                Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }
}

sealed partial class MemorySelectV3Form : Form
{
    readonly CheckBox autoUpdate = new CheckBox();
    readonly TextBox updateUrl = new TextBox();
    readonly Label updateStatus = new Label();
    readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
    BackgroundWorker updateWorker;
    StagedUpdate stagedUpdate;
    long nextUpdateCheck;

    void BuildUpdateControls()
    {
        var page = new TabPage("Güncelleme/Kılavuz");
        tabs.TabPages.Add(page);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents =
            false, Padding = new Padding(24) };
        page.Controls.Add(panel);
        autoUpdate.Text = "Otomatik güncelleme kontrolü";
        autoUpdate.Width = 400;
        panel.Controls.Add(autoUpdate);
        panel.Controls.Add(Btn("Şimdi kontrol et", delegate
        {
            SaveSettings();
            CheckUpdates();
        }, 220));
        panel.Controls.Add(LabelOf("Sürüm: " + UpdateTrust.CurrentVersion, 300));
        panel.Controls.Add(LabelOf("Güncelleme adresi", 300));
        updateUrl.Width = 850;
        panel.Controls.Add(updateUrl);
        locked.Add(updateUrl);
        updateStatus.Width = 900;
        updateStatus.Height = 80;
        panel.Controls.Add(updateStatus);
        panel.Controls.Add(Btn("Kullanım Kılavuzu · PDF", OpenManual, 280, false));
    }

    void RestoreUpdateControls()
    {
        autoUpdate.Checked = settings.AutoUpdate;
        updateUrl.Text = string.IsNullOrWhiteSpace(settings.UpdateManifestUrl) ?
            UpdateTrust.DefaultManifestUrl : settings.UpdateManifestUrl;
        updateStatus.Text = string.IsNullOrWhiteSpace(updateUrl.Text) ?
            "Yayın adresi henüz hazırlanmadı; otomatik indirme yapılmaz." : "Güncelleme kontrolü hazır.";
    }

    void SetupUpdates()
    {
        UpdateService.Acknowledge();
        updateTimer.Interval = 2000;
        updateTimer.Tick += delegate
        {
            if (stagedUpdate != null)
            {
                TryApplyUpdate();
                return;
            }
            if (autoUpdate.Checked && !string.IsNullOrWhiteSpace(updateUrl.Text) && clock.ElapsedMilliseconds >= nextUpdateCheck)
            {
                nextUpdateCheck = clock.ElapsedMilliseconds + 15 * 60 * 1000;
                CheckUpdates();
            }
        };
        updateTimer.Start();
    }

    void CheckUpdates()
    {
        if (updateWorker != null && updateWorker.IsBusy)
            return;
        string address = updateUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            updateStatus.Text = "Henüz güncelleme yayın adresi yok. Altyapı hazır; adres daha sonra eklenecek.";
            return;
        }
        updateStatus.Text = "Sürüm kontrol ediliyor; yeni sürüm varsa arka planda indirilir.";
        updateWorker = new BackgroundWorker();
        updateWorker.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = UpdateService.Check(address); };
        updateWorker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
        {
            if (IsDisposed)
                return;
            if (e.Error != null)
            {
                updateStatus.Text = "Güncelleme yapılamadı; mevcut sürüm korunuyor: " + e.Error.Message;
                Log(e.Error.Message);
                return;
            }
            stagedUpdate = e.Result as StagedUpdate;
            if (stagedUpdate == null)
            {
                updateStatus.Text = "Güncel sürüm kullanılıyor.";
                return;
            }
            updateStatus.Text = "Yeni sürüm doğrulandı: " + stagedUpdate.Manifest.Version +
                ". Uygulama yeniden açılacak.";
            TryApplyUpdate();
        };
        updateWorker.RunWorkerAsync();
    }

    void TryApplyUpdate()
    {
        if (stagedUpdate == null)
            return;
        if (transaction.Pending || (active && recovery.Phase != RecoveryPhase.Ready))
        {
            updateStatus.Text = "Güncelleme hazır; bekleyen eşya / ölüm işleminin bitmesi bekleniyor.";
            return;
        }
        try
        {
            Stop("Güncelleme için otomasyon durduruldu.");
            SaveSettings();
            UpdateService.LaunchWorker(stagedUpdate);
            stagedUpdate = null;
            updateTimer.Stop();
            Close();
        }
        catch (Exception e)
        {
            stagedUpdate = null;
            updateStatus.Text = "Güncelleme başlatılamadı; mevcut sürüm korunuyor: " + e.Message;
            Log(e.ToString());
        }
    }
}
