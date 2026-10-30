// bip run <commande>   lance la commande dans ce terminal et la suit dans l'encoche
// bip --version
//
// Compile en console (/target:exe) : la commande partage le terminal, sa sortie, ses
// couleurs et sa saisie restent intactes. bip renvoie le code de retour de la commande.
// La commande est executee par cmd.exe (pour une commande PowerShell : bip run pwsh -c "...").
//
// Etat ecrit dans state\run-<pid>.state, au format commun (StateRecord, harness "run").

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

static class BipCli
{
    static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    static int Main(string[] args)
    {
        string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (first == "--version" || first == "version")
        {
            Console.WriteLine("Bip v" + ClaudeNotch.AppVersion.Text);
            return 0;
        }
        if (first == "info") return Info(args);
        if (first == "agenda") return AgendaCmd();
        string command = first == "run" ? ClaudeNotch.Logic.UnwrapCommand(RawAfterRun(), File.Exists) : "";
        if (command.Length == 0)
        {
            Console.WriteLine("Bip v" + ClaudeNotch.AppVersion.Text + " — suivre une commande longue dans l'encoche");
            Console.WriteLine();
            Console.WriteLine("  bip run <commande>     ex. : bip run npm run build");
            Console.WriteLine("  bip info [--source nom] [--priorite 2|3] [--duree secondes] \"titre\" [\"texte\"]");
            Console.WriteLine("                         afficher une info dans l'encoche (canal Bip Connect)");
            Console.WriteLine("  bip agenda             (collecteur de Logres) agenda en base64 sur l'entree standard");
            Console.WriteLine("  bip --version");
            return first == "" || first == "help" || first == "-h" || first == "--help" ? 0 : 2;
        }

        int pid;
        DateTime startTime;
        using (var me = Process.GetCurrentProcess())
        {
            pid = me.Id;
            // Heure de lancement : ordre dans l'encoche, et preuve que le pid est bien le notre.
            startTime = me.StartTime;
        }
        string file = Path.Combine(Root, "state", "run-" + pid + ".state");
        string dir = Environment.CurrentDirectory;
        string label = command.Replace("|", "¦").Replace("\r", " ").Replace("\n", " ");
        if (label.Length > 80) label = label.Substring(0, 77) + "…";

        bool interrupted = false;
        // Ctrl+C : la commande le recoit aussi et s'arrete ; bip reste en vie pour noter la fin.
        // Ctrl+Pause ne peut pas etre annule : bip s'arrete, l'encoche le verra disparaitre.
        Console.CancelKeyPress += (s, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlBreak) return;
            e.Cancel = true;
            interrupted = true;
        };

        var rec = new ClaudeNotch.StateRecord { Harness = "run", State = "busy", Dir = dir, Pid = pid, Label = label, Start = startTime };
        Write(file, rec.Write());
        int code;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"") { UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                code = p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("bip : impossible de lancer la commande (" + ex.Message + ")");
            code = 1;
        }

        // Une commande qui gere Ctrl+C et finit proprement (code 0) n'est pas un echec.
        bool stopped = interrupted && code != 0;
        string outcome = stopped ? "interrompu" : code.ToString();
        rec.State = code == 0 ? "ready" : "error";
        rec.Code = outcome;
        Write(file, rec.Write());
        return code;
    }

    // bip info : envoie une info a l'encoche par le canal Bip Connect (lecture seule cote encoche).
    // Code de retour : 0 acceptee, 1 refusee, 2 usage, 3 encoche absente ou qui ne repond pas en 3 s.
    static int Info(string[] args)
    {
        string source = "manuel", title = null, text = "";
        int priority = 3, ttl = 300;
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--source" && i + 1 < args.Length) source = args[++i];
            else if (a == "--priorite" && i + 1 < args.Length) int.TryParse(args[++i], out priority);
            else if (a == "--duree" && i + 1 < args.Length) int.TryParse(args[++i], out ttl);
            else if (title == null) title = a;
            else text = a;
        }
        if (string.IsNullOrWhiteSpace(title)) { Console.Error.WriteLine("bip info : titre manquant"); return 2; }

        var msg = ClaudeNotch.Json.Write(new System.Collections.Generic.Dictionary<string, object>
            { { "source", source }, { "title", title }, { "text", text }, { "priority", priority }, { "ttl", ttl } });
        string name = "BipConnect-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        try
        {
            using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut))
            {
                try { pipe.Connect(1000); }
                catch (TimeoutException) { Console.Error.WriteLine("bip info : l'encoche ne tourne pas"); return 3; }
                var bytes = new UTF8Encoding(false).GetBytes(msg + "\n");
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();
                // Une encoche figee ne doit pas bloquer l'appelant (le collecteur de Logres, un script) : 3 s au plus.
                var read = System.Threading.Tasks.Task.Run(() => new System.IO.StreamReader(pipe, Encoding.UTF8).ReadLine());
                if (!read.Wait(3000)) { Console.Error.WriteLine("bip info : l'encoche ne répond pas"); return 3; }
                var reply = read.Result ?? "";
                Console.WriteLine(reply);
                return reply == "ok" ? 0 : 1;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("bip info : " + ex.Message); return 3; }
    }

    // bip agenda : appele par le collecteur de Logres (docs/CONTRAT-BIP-AGENDA.md). Entree standard : la reponse brute du releve,
    // en base64 (UTF-8). Seul le champ "agenda" est garde, revalide (AgendaLogic.Parse), puis ecrit dans stategenda.json avec
    // l'heure de reception ; l'encoche le relit. Code de retour : 0 accepte, 1 refuse (illisible ou hors contrat).
    static int AgendaCmd()
    {
        try
        {
            string b64 = Console.In.ReadToEnd().Trim();
            if (b64.Length == 0 || b64.Length > 2 * 1024 * 1024) { Console.Error.WriteLine("bip agenda : entree vide ou trop grande"); return 1; }
            var root = ClaudeNotch.Json.Obj(ClaudeNotch.Json.Parse(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(b64))));
            var agenda = ClaudeNotch.Json.Get(root, "agenda");
            if (ClaudeNotch.AgendaLogic.Parse(agenda) == null) { Console.Error.WriteLine("bip agenda : agenda hors contrat, ignore"); return 1; }
            var doc = new System.Collections.Generic.Dictionary<string, object>
                { { "recu", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) }, { "agenda", agenda } };
            string file = Path.Combine(Root, "state", "agenda.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            // Ecriture a cote puis remplacement : l'encoche ne lit jamais un fichier a moitie ecrit.
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, ClaudeNotch.Json.Write(doc), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("bip agenda : " + ex.GetType().Name); return 1; }
    }

    // Tout ce qui suit "run" sur la ligne de commande, tel que tape (guillemets compris).
    static string RawAfterRun()
    {
        string cl = Environment.CommandLine;
        int i = 0;
        if (cl.StartsWith("\"")) { i = cl.IndexOf('"', 1) + 1; if (i <= 0) i = cl.Length; }
        else while (i < cl.Length && !char.IsWhiteSpace(cl[i])) i++;
        while (i < cl.Length && char.IsWhiteSpace(cl[i])) i++;
        if (string.Compare(cl, i, "run", 0, 3, StringComparison.OrdinalIgnoreCase) == 0) i += 3;
        while (i < cl.Length && char.IsWhiteSpace(cl[i])) i++;
        return i < cl.Length ? cl.Substring(i).Trim() : "";
    }

    static void Write(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            for (int i = 0; ; i++)
            {
                try { File.WriteAllText(file, text, new UTF8Encoding(false)); return; }
                catch (IOException) { if (i >= 5) return; Thread.Sleep(50); }
            }
        }
        catch { }
    }
}
