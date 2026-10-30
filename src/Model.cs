// Modele : sessions lues dans state\, demandes en attente recues par le pipe,
// et reglages. Aucune dependance a l'interface.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ClaudeNotch
{
    enum St { Stale = 0, Ready = 1, Busy = 2, Error = 3, Waiting = 4, Idle = 5 }

    static class States
    {
        public static St Parse(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "busy":    return St.Busy;
                case "waiting": return St.Waiting;
                case "error":   return St.Error;
                case "idle":    return St.Idle;
                default:        return St.Ready;
            }
        }

        public static string Label(St s)
        {
            switch (s)
            {
                case St.Ready:   return "prêt";
                case St.Busy:    return "en cours";
                case St.Waiting: return "en attente";
                case St.Error:   return "erreur";
                case St.Idle:    return "au repos";
                default:         return "inactif";
            }
        }
    }

    class Session
    {
        public string Id;
        public string Dir;
        public string Name;
        public St State;
        public DateTime Since;   // entree dans l'etat courant
        public DateTime Order;   // lancement de la session : ordre stable dans l'encoche
        public string Kind = "claude";   // "claude" ou "run" (commande lancee par bip run)
        public string Harness = "claude";   // outil de la session : claude, codex... ou run
        public string Label;             // run : la commande
        public string Code;              // run termine : code de retour, ou "interrompu"
        public string Title;             // claude : titre du chat (transcript), ou null
        public List<SubAgent> SubAgents = new List<SubAgent>();

        // Ligne de session : "titre · dossier/", ou "dossier/" sans titre. Titre long coupe, dossier toujours visible.
        public string Display
        {
            get
            {
                if (IsRun) return Name;
                // Autre outil que Claude Code : son nom en tete ("codex · dossier/").
                var tool = Harness == "claude" ? "" : Harness + " · ";
                var folder = NameOf(Dir) + "/";
                if (string.IsNullOrEmpty(Title)) return tool + folder;
                var t = Title.Length > TITLE_MAX ? Title.Substring(0, TITLE_MAX - 1).TrimEnd() + "…" : Title;
                return tool + t + " · " + folder;
            }
        }
        const int TITLE_MAX = 30;

        // Entrouverture : le titre seul (ou le dossier sans titre).
        public string ShortName { get { return IsRun || string.IsNullOrEmpty(Title) ? Name : Title; } }

        public bool IsRun { get { return Kind == "run"; } }
        public TimeSpan RunTime { get { return Since - Order; } }   // run termine : duree

        public TimeSpan Elapsed { get { return DateTime.Now - Since; } }

        public static string NameOf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "session";
            var d = dir.TrimEnd('\\', '/');
            var n = Audit.LastSegment(d);
            return string.IsNullOrEmpty(n) ? d : n;
        }
    }

    // ------------------------------------------------------------ reglages

    class Config
    {
        readonly string _path;
        public bool Flash = true;
        public DateTime DndUntil = DateTime.MinValue;   // "ne pas deranger" jusqu'a cette heure
        public bool Dnd { get { return DateTime.Now < DndUntil; } }
        public bool AutoDnd = true;      // sourdine automatique pendant les appels
        // Une demande sans reponse dans l'encoche repart vers Claude au bout de ce delai : la session
        // n'attend plus jusqu'au filet d'une heure du hook si l'encoche n'est pas vue.
        public static readonly int[] ReplyChoices = { 5, 10, 30, 60 };
        public int ReplyMinutes = 10;
        // Phase 3 : fichier de la capture rapide (Ctrl+Alt+B), et jour ou le bilan du matin a ete masque.
        public string CapturePath = @"D:\Obsidian\ideation\inbox.md";
        public string BriefHiddenOn = "";
        public bool PauseMusic = true;   // musique en pause pendant les appels (reprise a la fin)

        public Config(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var kv = line.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    var k = kv[0].Trim(); var v = kv[1].Trim();
                    if (k == "flash") Flash = v != "0";
                    else if (k == "dnd") { long t; if (long.TryParse(v, out t)) DndUntil = new DateTime(t); }
                    else if (k == "autodnd") AutoDnd = v != "0";
                    else if (k == "reply") { int m; if (int.TryParse(v, out m) && ReplyChoices.Contains(m)) ReplyMinutes = m; }
                    else if (k == "capture" && v.Length > 0) CapturePath = v;
                    else if (k == "brief") BriefHiddenOn = v;
                    else if (k == "music") PauseMusic = v != "0";
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_path, "flash=" + (Flash ? "1" : "0") + Environment.NewLine +
                                         "dnd=" + DndUntil.Ticks + Environment.NewLine +
                                         "autodnd=" + (AutoDnd ? "1" : "0") + Environment.NewLine +
                                         "reply=" + ReplyMinutes + Environment.NewLine +
                                         "capture=" + CapturePath + Environment.NewLine +
                                         "brief=" + BriefHiddenOn + Environment.NewLine +
                                         "music=" + (PauseMusic ? "1" : "0") + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------ capture rapide (phase 3)

    // Ajoute une ligne a la boite de capture du vault (copie locale synchronisee par Syncthing). Cree le fichier avec
    // son en-tete s'il manque, jamais le dossier (vault absent : rien n'est ecrit) ; refuse un fichier qui est un lien.
    static class Capture
    {
        // Null si la ligne est ecrite, sinon la raison de l'echec.
        public static string Append(string path, string line)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "dossier introuvable : " + dir;
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) return "le dossier est un lien, rien n'est écrit";
                // Les attributs du chemin lui-meme, sans suivre un lien : un lien dont la cible n'existe pas encore
                // (File.Exists faux) serait sinon suivi et sa cible creee.
                FileAttributes attrs = 0;
                bool present = true;
                try { attrs = File.GetAttributes(path); } catch (FileNotFoundException) { present = false; }
                if (present && (attrs & FileAttributes.ReparsePoint) != 0) return "le fichier est un lien, rien n'est écrit";
                bool exists = present && File.Exists(path);
                var text = (exists ? "" : Logic.InboxHeader.Replace("\n", Environment.NewLine)) + line + Environment.NewLine;
                // Une derniere ligne sans fin de ligne (modifiee a la main) : la capture repart sur une ligne propre.
                if (exists)
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        if (fs.Length > 0) { fs.Seek(-1, SeekOrigin.End); if (fs.ReadByte() != '\n') text = Environment.NewLine + text; }
                File.AppendAllText(path, text, new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // ------------------------------------------------------------ journal du jour

    class DayStats
    {
        public int Sessions, Answers, Commands, CommandFailures;
        public TimeSpan Work;
        public List<Tuple<string, DateTime>> Finished = new List<Tuple<string, DateTime>>();   // nom, fin (3 au plus)
    }

    // Un fichier par jour (journal\AAAA-MM-JJ.log), garde 7 jours. Lignes
    // "HH:mm:ss|evenement|session|nom" ; evenements : travail, pause, termine, reponse,
    // commande-ok, commande-ko (commandes suivies par bip run), info2 / info3 (info de Bip
    // Connect et sa priorite ; session = source, nom = titre).
    static class Journal
    {
        static string Dir { get { return Path.Combine(Program.Root, "journal"); } }
        static string FileOf(DateTime d) { return Path.Combine(Dir, d.ToString("yyyy-MM-dd") + ".log"); }

        public static void Add(string evt, string id, string name)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FileOf(DateTime.Now), DateTime.Now.ToString("HH:mm:ss") + "|" + evt + "|" + id + "|"
                                   + (name ?? "").Replace("|", "_") + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        public static void Purge()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Dir, "*.log"))
                    if (DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromDays(7)) File.Delete(f);
            }
            catch { }
        }

        // Evenements posterieurs a "from", sur plusieurs jours (fichier de chaque jour) : (heure, evenement, session, nom).
        // Sert au bilan du matin (depuis la veille a 20 h) et au recap d'un appel qui passe minuit.
        public static List<Tuple<DateTime, string, string, string>> Between(DateTime from, DateTime to)
        {
            var list = new List<Tuple<DateTime, string, string, string>>();
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                string[] lines;
                try { lines = File.ReadAllLines(FileOf(day), Encoding.UTF8); }
                catch { continue; }
                foreach (var l in lines)
                {
                    var p = l.Split(new[] { '|' }, 4);
                    DateTime t;
                    if (p.Length < 4 || !DateTime.TryParseExact(p[0], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    var at = day + t.TimeOfDay;
                    if (at >= from.AddSeconds(-1) && at <= to) list.Add(Tuple.Create(at, p[1], p[2], p[3]));
                }
            }
            return list;
        }

        // Evenements posterieurs a "from" : (heure, evenement, nom).
        public static List<Tuple<DateTime, string, string>> Since(DateTime from)
        {
            // Plusieurs jours : un appel commence avant minuit garde ce qui s'est passe avant (constat F7 de l'audit).
            return Between(from, DateTime.Now).Select(e => Tuple.Create(e.Item1, e.Item2, e.Item4)).ToList();
        }

        public static DayStats Today()
        {
            var st = new DayStats();
            string[] lines;
            try { lines = File.ReadAllLines(FileOf(DateTime.Now), Encoding.UTF8); }
            catch { return st; }

            var ids = new HashSet<string>();
            var open = new Dictionary<string, DateTime>();      // debut de travail en cours, par session
            var finished = new Dictionary<string, Tuple<string, DateTime>>();
            foreach (var l in lines)
            {
                var p = l.Split(new[] { '|' }, 4);
                DateTime t;
                if (p.Length < 4 || !DateTime.TryParseExact(p[0], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                var at = DateTime.Today + t.TimeOfDay;
                if (!p[1].StartsWith("commande")) ids.Add(p[2]);
                DateTime start;
                switch (p[1])
                {
                    case "travail": if (!open.ContainsKey(p[2])) open[p[2]] = at; break;
                    case "pause":   if (open.TryGetValue(p[2], out start)) { st.Work += at - start; open.Remove(p[2]); } break;
                    case "termine": finished[p[2]] = Tuple.Create(p[3], at); break;
                    case "reponse": st.Answers++; break;
                    case "commande-ok": st.Commands++; break;
                    case "commande-ko": st.Commands++; st.CommandFailures++; break;
                }
            }
            foreach (var since in open.Values) st.Work += DateTime.Now - since;   // encore au travail
            st.Sessions = ids.Count;
            st.Finished = finished.Values.OrderByDescending(f => f.Item2).Take(3).ToList();
            return st;
        }
    }

    // ------------------------------------------------------------ sessions

    static class Sessions
    {
        static readonly TimeSpan STALE_BUSY    = TimeSpan.FromMinutes(20);
        // Une interruption (Echap) n'emet pas de Stop : sans ce plafond, une session
        // vivante resterait "en cours" indefiniment. Chaque outil reecrit l'etat.
        static readonly TimeSpan STALE_BUSY_ALIVE = TimeSpan.FromHours(1);
        static readonly TimeSpan STALE_WAITING = TimeSpan.FromHours(6);
        static readonly TimeSpan STALE_READY   = TimeSpan.FromMinutes(4);   // pret / au repos, puis le Bip s'efface
        static readonly TimeSpan STALE_RUN_ERROR = TimeSpan.FromMinutes(10);  // commande en echec
        static readonly TimeSpan PURGE_AGE     = TimeSpan.FromHours(12);

        // Ce qui merite une place dans l'encoche ("pret" s'efface au bout de 10 min).
        public static bool IsActive(St s) { return s != St.Stale; }

        // Memes regles que ClaudeVoyant : preuve de vie par PID, sinon anciennete.
        public static List<Session> Load(string stateDir)
        {
            var list = new List<Session>();
            string[] files;
            try { files = Directory.GetFiles(stateDir, "*.state"); }
            catch { return null; }

            foreach (var f in files)
            {
                try
                {
                    var rec = StateRecord.Parse(ReadShared(f));
                    if (rec == null) continue;

                    // Format commun (StateRecord) : claude, codex... ou "run" pour bip run.
                    var st = States.Parse(rec.State);
                    var dir = rec.Dir;
                    var since = File.GetLastWriteTime(f);
                    int pid = rec.Pid;
                    string kind = rec.IsRun ? "run" : "claude";
                    bool hasStart = rec.Start != null;
                    DateTime start = hasStart ? rec.Start.Value : File.GetCreationTime(f);

                    if (kind == "run")
                    {
                        // Preuve de vie : bip.exe, tant que la commande tourne. Une fois finie,
                        // elle reste affichee 4 min (succes) ou 10 min (echec).
                        var age = DateTime.Now - since;
                        if (st == St.Busy && pid > 0 && !Alive(pid, null, hasStart ? start : DateTime.MinValue)) st = St.Stale;
                        else if (st == St.Ready && age > STALE_READY) st = St.Stale;
                        else if (st == St.Error && age > STALE_RUN_ERROR) st = St.Stale;
                        // Commande effacee de l'encoche : son fichier n'a plus d'usage.
                        if (st == St.Stale) { try { File.Delete(f); } catch { } continue; }
                    }
                    else if (pid > 0 && !Alive(pid, rec.Proc ?? rec.Harness, DateTime.MinValue)) st = St.Stale;
                    else
                    {
                        var age = DateTime.Now - since;
                        if (st == St.Busy && age > (pid == 0 ? STALE_BUSY : STALE_BUSY_ALIVE)) st = St.Stale;
                        else if (st == St.Waiting && age > STALE_WAITING) st = St.Stale;
                        else if ((st == St.Ready || st == St.Idle) && age > STALE_READY) st = St.Stale;
                    }

                    string id = Path.GetFileNameWithoutExtension(f);
                    bool live = !rec.IsRun && st != St.Stale;
                    list.Add(new Session
                    {
                        Id = id,
                        Dir = dir,
                        Name = rec.IsRun && !string.IsNullOrEmpty(rec.Label) ? rec.Label : Session.NameOf(dir),
                        State = st,
                        Since = since,
                        Order = start,
                        Kind = kind,
                        Harness = rec.Harness,
                        Label = rec.IsRun ? rec.Label : null,
                        Code = rec.Code,
                        // Titre du chat : transcript de Claude Code seulement (celui des autres outils n'est pas un format stable).
                        Title = !live ? null : rec.Harness == "claude" ? Agents.Title(rec.Transcript) : rec.Title,
                        SubAgents = live ? Agents.Load(stateDir, id) : new List<SubAgent>()
                    });
                }
                catch { }
            }

            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        // Lecture partagee : un hook peut ecrire le fichier au meme instant. Un fichier
        // vu vide (en cours de reecriture) est relu une fois.
        static string ReadShared(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                    {
                        var t = r.ReadToEnd().Trim();
                        if (t.Length > 0) return t;
                    }
                }
                catch (IOException) { }
                Thread.Sleep(20);
            }
            return "";
        }

        public static void Purge(string stateDir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(stateDir, "*.state").Concat(Directory.GetFiles(stateDir, "*.agents")))
                    if (DateTime.Now - File.GetLastWriteTime(f) > PURGE_AGE) File.Delete(f);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GetExitCodeProcess(IntPtr h, out int code);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // Interroge directement le processus (quelques microsecondes, sans enumerer tout le
        // poste). Seul "aucun processus avec ce pid" prouve la mort ; dans le doute (acces
        // refuse a un processus eleve...), vivant. Un pid reutilise par un autre programme se
        // voit a son nom (name) ou a son heure de lancement (start), quand on les connait.
        static bool Alive(int pid, string name, DateTime start)
        {
            const int QUERY_LIMITED = 0x1000, STILL_ACTIVE = 259, ERROR_INVALID_PARAMETER = 87;
            var h = OpenProcess(QUERY_LIMITED, false, pid);
            if (h == IntPtr.Zero) return System.Runtime.InteropServices.Marshal.GetLastWin32Error() != ERROR_INVALID_PARAMETER;
            try
            {
                int code;
                long created, x, k, u;
                if (GetExitCodeProcess(h, out code) && code != STILL_ACTIVE) return false;
                if (start != DateTime.MinValue && GetProcessTimes(h, out created, out x, out k, out u)
                    && Math.Abs((DateTime.FromFileTime(created) - start).TotalSeconds) >= 2) return false;
                if (name == null) return true;
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return true;
                return string.Equals(Path.GetFileNameWithoutExtension(sb.ToString()), name, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(h); }
        }
    }

    // ------------------------------------------------------------ demandes en attente

    // Une demande de permission ou une question, bloquee cote hook jusqu'a la reponse.
    class Pending
    {
        public string Kind;                       // "permission" | "ask"
        public string SessionId;
        public string Cwd;
        public string Proj;                       // racine du projet (CLAUDE_PROJECT_DIR), sinon Cwd
        public Dictionary<string, object> Payload;
        public DateTime Since = DateTime.Now;

        public string ToolName { get { return Json.Str(Payload, "tool_name") ?? ""; } }
        public Dictionary<string, object> ToolInput { get { return Json.Obj(Json.Get(Payload, "tool_input")) ?? new Dictionary<string, object>(); } }

        internal Action<string> Send;             // ecrit la reponse dans le pipe (une seule fois)
        int _done;

        public bool Done { get { return _done != 0; } }

        // Reponse vide = Claude pose la question lui-meme.
        public void Reply(string hookStdout)
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            try { Send(hookStdout ?? ""); } catch { }
        }

        internal void MarkGone() { Interlocked.Exchange(ref _done, 1); }
    }

    static class Replies
    {
        public static string Allow()
        {
            return Json.Write(Hso("PermissionRequest", "decision", new Dictionary<string, object> { { "behavior", "allow" } }));
        }

        // "Toujours" : une seule des suggestions de Claude Code, celle dont l'utilisateur
        // a lu la portee sur le bouton, limitee a la session.
        public static string AllowAlways(Dictionary<string, object> suggestion)
        {
            var copy = new Dictionary<string, object>(suggestion);
            copy["destination"] = "session";
            var decision = new Dictionary<string, object>
            {
                { "behavior", "allow" },
                { "updatedPermissions", new object[] { copy } }
            };
            return Json.Write(Hso("PermissionRequest", "decision", decision));
        }

        // Libelle du bouton, ou null pour un type de suggestion inconnu (non propose).
        public static string AlwaysLabel(Dictionary<string, object> s)
        {
            switch (Json.Str(s, "type"))
            {
                case "setMode":
                    var mode = Json.Str(s, "mode");
                    return mode == "acceptEdits" ? "Toujours : toutes les modifications de fichiers"
                                                 : "Toujours : mode « " + mode + " »";
                case "addRules":
                    var rules = new List<string>();
                    foreach (var r in Json.Arr(Json.Get(s, "rules")) ?? new object[0])
                    {
                        var rd = Json.Obj(r);
                        var tool = Json.Str(rd, "toolName");
                        if (string.IsNullOrEmpty(tool)) continue;
                        var content = Json.Str(rd, "ruleContent");
                        rules.Add(string.IsNullOrEmpty(content) ? tool : tool + "(" + content + ")");
                    }
                    return rules.Count == 0 ? null : "Toujours : " + string.Join(", ", rules);
                case "addDirectories":
                    var dirs = (Json.Arr(Json.Get(s, "directories")) ?? new object[0]).Select(d => Convert.ToString(d)).ToList();
                    return dirs.Count == 0 ? null : "Toujours : accès à " + string.Join(", ", dirs);
                default:
                    return null;
            }
        }

        public static string Deny()
        {
            return Json.Write(Hso("PermissionRequest", "decision", new Dictionary<string, object>
            {
                { "behavior", "deny" },
                { "message", "Refusé par l'utilisateur depuis ClaudeNotch." }
            }));
        }

        // AskUserQuestion : on reprend l'entree d'origine et on y ajoute les reponses,
        // indexees par le texte de la question (choix multiples joints par ", ").
        public static string Answers(Pending p, Dictionary<string, string> answers)
        {
            var input = new Dictionary<string, object>(p.ToolInput);
            var a = new Dictionary<string, object>();
            foreach (var kv in answers) a[kv.Key] = kv.Value;
            input["answers"] = a;
            var hso = new Dictionary<string, object>
            {
                { "hookEventName", "PreToolUse" },
                { "permissionDecision", "allow" },
                { "updatedInput", input }
            };
            return Json.Write(new Dictionary<string, object> { { "hookSpecificOutput", hso } });
        }

        static Dictionary<string, object> Hso(string evt, string key, object value)
        {
            return new Dictionary<string, object>
            {
                { "hookSpecificOutput", new Dictionary<string, object> { { "hookEventName", evt }, { key, value } } }
            };
        }
    }

    // ------------------------------------------------------------ pipe

    // Un pipe nomme reserve au compte courant. Chaque connexion = une demande :
    // "<mode> <taille>\n<json>", puis une ligne de reponse. Si le hook disparait
    // (session interrompue, timeout), la demande est retiree.
    class PipeServer
    {
        public static string Name { get { return "ClaudeNotch-" + WindowsIdentity.GetCurrent().User.Value; } }

        readonly Action<Pending> _onNew, _onGone;
        readonly Action<string> _log;

        public PipeServer(Action<Pending> onNew, Action<Pending> onGone, Action<string> log)
        {
            _onNew = onNew; _onGone = onGone; _log = log;
        }

        public void Start()
        {
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "pipe" };
            t.Start();
        }

        void AcceptLoop()
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                                                 PipeAccessRights.FullControl, AccessControlType.Allow));
            while (true)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    // Asynchronous : la lecture qui guette la deconnexion et l'ecriture de
                    // la reponse se font en meme temps sur le meme handle.
                    pipe = new NamedPipeServerStream(Name, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 0, 0, sec);
                    pipe.WaitForConnection();
                    var conn = pipe;
                    new Thread(() => Handle(conn)) { IsBackground = true, Name = "demande" }.Start();
                }
                catch (Exception ex)
                {
                    _log("pipe : " + ex.Message);
                    if (pipe != null) pipe.Dispose();
                    Thread.Sleep(1000);
                }
            }
        }

        void Handle(NamedPipeServerStream pipe)
        {
            Pending p = null;
            try
            {
                var header = ReadLine(pipe, 64);
                var parts = (header ?? "").Split(' ');
                int len;
                if (parts.Length != 2 || !int.TryParse(parts[1], out len) || len < 0 || len > 16 * 1024 * 1024)
                { pipe.Dispose(); return; }

                var buf = new byte[len];
                int got = 0;
                while (got < len)
                {
                    int n = pipe.Read(buf, got, len - got);
                    if (n <= 0) { pipe.Dispose(); return; }
                    got += n;
                }

                var payload = Json.Obj(Json.Parse(Encoding.UTF8.GetString(buf)));
                if (payload == null || (parts[0] != "permission" && parts[0] != "ask")) { pipe.Dispose(); return; }

                p = new Pending
                {
                    Kind = parts[0],
                    // Identifiant du fichier d'etat, fourni par le hook ; a defaut, assaini de la meme facon.
                    SessionId = Json.Str(payload, "bip_sess") ?? Logic.SanitizeId(Json.Str(payload, "session_id"), Json.Str(payload, "cwd")),
                    Cwd = Json.Str(payload, "cwd") ?? "",
                    Proj = Json.Str(payload, "bip_proj") ?? Json.Str(payload, "cwd") ?? "",
                    Payload = payload
                };
                var lk = new object();
                p.Send = line =>
                {
                    lock (lk)
                    {
                        var bytes = Encoding.UTF8.GetBytes(line + "\n");
                        pipe.Write(bytes, 0, bytes.Length);
                        pipe.Flush();
                        try { pipe.WaitForPipeDrain(); } catch { }
                        pipe.Dispose();
                    }
                };

                if (!Usable(p)) { p.Reply(""); return; }
                _onNew(p);

                // Le hook n'ecrit plus rien : Read ne revient que s'il ferme le pipe.
                var one = new byte[1];
                while (pipe.Read(one, 0, 1) > 0) { }
                if (!p.Done) { p.MarkGone(); _onGone(p); }
                pipe.Dispose();
            }
            catch (ObjectDisposedException) { /* reponse envoyee */ }
            catch (Exception ex)
            {
                if (p != null && !p.Done) { p.MarkGone(); _onGone(p); }
                else if (p == null) _log("demande illisible : " + ex.Message);
                try { pipe.Dispose(); } catch { }
            }
        }

        // L'encoche ne doit jamais retenir une demande qu'elle ne saura pas afficher.
        static bool Usable(Pending p)
        {
            var tool = p.ToolName;
            if (p.Kind == "permission") return tool != "" && tool != "ExitPlanMode" && tool != "AskUserQuestion";
            if (tool != "AskUserQuestion") return false;
            var qs = Json.Arr(Json.Get(p.ToolInput, "questions"));
            if (qs == null || qs.Length == 0) return false;
            foreach (var q in qs)
            {
                var qd = Json.Obj(q);
                if (qd == null || string.IsNullOrEmpty(Json.Str(qd, "question"))) return false;
                var opts = Json.Arr(Json.Get(qd, "options"));
                if (opts == null || opts.Length == 0) return false;
            }
            return true;
        }

        static string ReadLine(Stream s, int max)
        {
            var sb = new StringBuilder();
            while (sb.Length < max)
            {
                int b = s.ReadByte();
                if (b < 0) return null;
                if (b == '\n') return sb.ToString();
                sb.Append((char)b);
            }
            return null;
        }
    }

    // ------------------------------------------------------------ canal Bip Connect (lecture seule)

    // Second pipe, distinct de celui des demandes : Bip Connect (et "bip info") n'y envoie que de
    // quoi afficher (InfoMessage, Logic.cs). Rien de ce qui arrive ici ne peut repondre a une demande
    // ni toucher a l'etat d'une session. Une ligne JSON par connexion, une ligne de reponse :
    // "ok", ou "refusé : <raison>".
    class InfoServer
    {
        public static string Name { get { return "BipConnect-" + WindowsIdentity.GetCurrent().User.Value; } }

        readonly Func<InfoMessage, string> _onInfo;   // null si accepte, sinon la raison du refus
        readonly Action<string> _log;
        int _active;                                   // connexions en cours (4 au plus)
        // Plafond global, en plus de celui par source : changer de nom de source a chaque message ne
        // doit pas permettre d'inonder le fil de l'interface.
        const int GLOBAL_PER_MINUTE = 30;
        readonly Queue<DateTime> _recent = new Queue<DateTime>();
        static readonly TimeSpan READ_TIMEOUT = TimeSpan.FromSeconds(2);

        bool Allow()
        {
            lock (_recent)
            {
                var now = DateTime.UtcNow;
                while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromMinutes(1)) _recent.Dequeue();
                if (_recent.Count >= GLOBAL_PER_MINUTE) return false;
                _recent.Enqueue(now);
                return true;
            }
        }

        public InfoServer(Func<InfoMessage, string> onInfo, Action<string> log) { _onInfo = onInfo; _log = log; }

        public void Start() { new Thread(AcceptLoop) { IsBackground = true, Name = "bip-connect" }.Start(); }

        void AcceptLoop()
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            while (true)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    // Asynchronous : la lecture peut etre abandonnee au bout de READ_TIMEOUT.
                    pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                                     PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, sec);
                    pipe.WaitForConnection();
                    // Un client qui ne finit jamais sa ligne ne bloque qu'un fil ; au-dela de 4, refuse.
                    if (Interlocked.Increment(ref _active) > 4) { Interlocked.Decrement(ref _active); pipe.Dispose(); continue; }
                    var conn = pipe;
                    new Thread(() => { try { Handle(conn); } finally { Interlocked.Decrement(ref _active); } }) { IsBackground = true, Name = "info" }.Start();
                }
                catch (Exception ex)
                {
                    _log("bip connect : " + ex.Message);
                    if (pipe != null) pipe.Dispose();
                    Thread.Sleep(1000);
                }
            }
        }

        void Handle(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                try
                {
                    // Une ligne en READ_TIMEOUT au plus : un client qui n'ecrit rien ne garde pas sa place.
                    var buf = new MemoryStream();
                    var chunk = new byte[512];
                    var deadline = DateTime.UtcNow + READ_TIMEOUT;
                    while (true)
                    {
                        var left = deadline - DateTime.UtcNow;
                        if (left <= TimeSpan.Zero) return;
                        var read = pipe.ReadAsync(chunk, 0, chunk.Length);
                        if (!read.Wait(left)) return;   // la fermeture du pipe abandonne la lecture
                        int n = read.Result;
                        if (n <= 0) break;
                        int nl = Array.IndexOf(chunk, (byte)'\n', 0, n);
                        buf.Write(chunk, 0, nl >= 0 ? nl : n);
                        if (buf.Length > InfoMessage.MaxBytes) { Reply(pipe, "refusé : message trop long"); return; }
                        if (nl >= 0) break;
                    }
                    string error;
                    var msg = InfoMessage.Parse(Encoding.UTF8.GetString(buf.ToArray()), out error);
                    string refused = msg == null ? error : !Allow() ? "trop de messages (" + GLOBAL_PER_MINUTE + " par minute au total)" : _onInfo(msg);
                    Reply(pipe, refused == null ? "ok" : "refusé : " + refused);
                }
                // Toute erreur (encoche qui s'arrete, exception de l'interface) reste sur ce fil : une
                // exception non rattrapee sur un fil d'arriere-plan arreterait toute l'encoche.
                catch (Exception ex) { if (!(ex is IOException) && !(ex is AggregateException)) _log("bip connect : " + ex.Message); }
            }
        }

        static void Reply(Stream s, string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
        }
    }
}
