// Logique pure partagee : ni interface, ni fichier, ni appel systeme. Testee par tests\run.cmd.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeNotch
{
    static class Logic
    {
        // Encoche repliee : les "max" premieres sessions, dans l'ordre de lancement. Une session
        // qui attend au-dela prend la place de la derniere qui n'attend pas (plus ancienne attente
        // d'abord) : une demande ne se cache jamais dans "+N". L'ordre de lancement est garde.
        public static List<T> Collapsed<T>(IList<T> items, int max, Func<T, bool> waiting, Func<T, DateTime> since)
        {
            var shown = items.Take(max).ToList();
            foreach (var w in items.Skip(max).Where(waiting).OrderBy(since))
            {
                int slot = shown.FindLastIndex(x => !waiting(x));
                if (slot < 0) break;
                shown[slot] = w;
            }
            return shown.OrderBy(x => items.IndexOf(x)).ToList();
        }

        // bip run "npm run build && npm test" : sous PowerShell, une commande composee doit etre
        // entre guillemets, et cmd.exe prendrait alors le tout pour un seul nom de programme. Une
        // commande entierement entre guillemets, sans autre guillemet, est donc deballee, sauf si
        // c'est le chemin d'un programme existant ("C:\Program Files\app.exe") : deballe, cmd.exe
        // le couperait au premier espace.
        public static string UnwrapCommand(string command, Func<string, bool> isFile)
        {
            var c = (command ?? "").Trim();
            if (c.Length >= 2 && c[0] == '"' && c.IndexOf('"', 1) == c.Length - 1)
            {
                var inner = c.Substring(1, c.Length - 2).Trim();
                bool file = false;
                try { file = isFile != null && isFile(inner); } catch { }
                if (!file) return inner;
            }
            return c;
        }

        // Identifiant de session -> nom de fichier dans state\ : jamais de separateur ni de caractere
        // interdit. Le hook et l'encoche l'appliquent tous deux, pour que la demande retrouve sa session.
        public static string SanitizeId(string id, string dir)
        {
            if (string.IsNullOrEmpty(id)) id = (dir ?? "").Replace(' ', '_');
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return string.IsNullOrEmpty(id.Trim('.')) ? "session" : id;
        }

        // Caracteres invisibles hors des categories Format et Control (blancs de remplissage, joint de
        // graphemes...) : traites comme eux.
        static readonly HashSet<char> Blank = new HashSet<char>
            { '͏', 'ᅟ', 'ᅠ', '឴', '឵', '⠀', 'ㅤ', 'ﾠ' };

        // Rend visibles les caracteres invisibles ou de controle (U+202E et consorts) : le texte
        // affiche doit etre celui qui sera execute. Saut de ligne et tabulation gardes.
        public static string Visible(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                var cat = char.GetUnicodeCategory(c);
                if (((cat == UnicodeCategory.Format || cat == UnicodeCategory.Control) && c != '\n' && c != '\t') || Blank.Contains(c))
                    sb.Append("[U+").Append(((int)c).ToString("X4")).Append(']');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ Antigravity (agy)

        // Contrat verifie le 28/10/2026 (doc antigravity.google/docs/hooks et essai reel avec un hook
        // espion) : six evenements, entree JSON camelCase sans dossier courant ni nom d'evenement (le
        // hook le recoit en argument), aucun evenement "en attente de permission". PreToolUse exige
        // une decision : "allow" laisse la politique normale d'Antigravity decider.
        public const string AgyNeutralPreToolUse = "{\"decision\":\"allow\"}";

        public static readonly string[] AgyEvents = { "SessionStart", "PreInvocation", "PreToolUse", "PostToolUse", "PostInvocation", "Stop" };

        // Etat de la session apres un evenement Antigravity : idle, busy, ready, error.
        public static string AgyState(string evt, Dictionary<string, object> d)
        {
            switch (evt)
            {
                case "SessionStart": return "idle";
                case "Stop":
                    if (!string.IsNullOrEmpty(Json.Str(d, "error"))) return "error";
                    // Des taches de fond tournent encore : la session n'a pas fini.
                    return Json.Get(d, "fullyIdle") is bool && !(bool)Json.Get(d, "fullyIdle") ? "busy" : "ready";
                default: return "busy";
            }
        }

        // Premier dossier de l'espace de travail (Antigravity donne des barres obliques), ou "".
        public static string AgyDir(Dictionary<string, object> d)
        {
            var ws = Json.Arr(Json.Get(d, "workspacePaths"));
            var first = ws != null && ws.Length > 0 ? Convert.ToString(ws[0]) : "";
            return (first ?? "").Replace('/', '\\');
        }

        // Titre de la session : la premiere ligne de la derniere demande, rendue visible et bornee.
        public static string AgyTitle(Dictionary<string, object> d)
        {
            var s = Json.Str(d, "lastUserInput");
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Visible(s.Replace("\r", "").Split('\n')[0].Trim());
            return s.Length > 60 ? s.Substring(0, 59).TrimEnd() + "…" : s;
        }

        // ------------------------------------------------------------ capture rapide (phase 3)

        // En-tete d'ideation/inbox.md quand Bip le cree (format des notes du vault). Circuit : vault ideation/00-README.md.
        public const string InboxHeader =
            "---\n" +
            "name: ideation-inbox\n" +
            "description: \"Captures brutes d'idees de Ludo (Bip, Telegram...), a trier en seance d'ideation.\"\n" +
            "metadata:\n" +
            "  type: inbox\n" +
            "---\n\n" +
            "# Boite de capture\n\n";

        // Une capture : une ligne de liste horodatee, sur une seule ligne, caracteres invisibles rendus visibles, 500 caracteres
        // au plus. Null si le texte est vide.
        public static string CaptureLine(string text, DateTime now)
        {
            var t = Visible((text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim());
            if (t.Length == 0) return null;
            if (t.Length > 500) t = t.Substring(0, 499) + "…";
            return "- " + now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " · " + t;
        }

        // ------------------------------------------------------------ bilan du matin (phase 3)

        // Debut de "la nuit" pour le bilan : la veille a 20 h.
        public static DateTime NightStart(DateTime now) { return now.Date.AddHours(-4); }

        // Le bilan s'affiche de 5 h a midi.
        public static bool IsMorning(DateTime now) { return now.Hour >= 5 && now.Hour < 12; }

        // Lignes du bilan d'apres les evenements du journal depuis le debut de la nuit (heure, evenement, source, nom) :
        // sessions finies, commandes, actions des agents (journal d'audit), alertes de Logres (info2 = importante). Vide si rien.
        public static List<string> NightSummary(IEnumerable<Tuple<DateTime, string, string, string>> events, int agentActions, int refusals)
        {
            var ev = events.ToList();
            var lines = new List<string>();
            var parts = new List<string>();
            int done = ev.Count(e => e.Item2 == "termine");
            int ok = ev.Count(e => e.Item2 == "commande-ok"), ko = ev.Count(e => e.Item2 == "commande-ko");
            if (done > 0) parts.Add(done + (done > 1 ? " sessions ont fini" : " session a fini"));
            if (ok + ko > 0) parts.Add((ok + ko) + ((ok + ko) > 1 ? " commandes" : " commande") + (ko > 0 ? " (" + ko + " en échec)" : ""));
            if (agentActions > 0) parts.Add(agentActions + (agentActions > 1 ? " actions d'agents" : " action d'agent") + (refusals > 0 ? " · " + refusals + " refus" : ""));
            if (parts.Count > 0) lines.Add(string.Join(" · ", parts));
            var important = ev.Where(e => e.Item2 == "info2").ToList();
            int infos = ev.Count(e => e.Item2 == "info2" || e.Item2 == "info3");
            if (infos > 0)
            {
                var l = infos + (infos > 1 ? " alertes reçues" : " alerte reçue");
                if (important.Count > 0) l += " · " + important.Count + (important.Count > 1 ? " importantes" : " importante") + ", dernière : " + important.Last().Item4;
                lines.Add(l);
            }
            return lines;
        }

        // Les "max" premieres lignes d'un texte, et le nombre de lignes au-dela (0 si tout tient).
        public static string[] Head(string text, int max, out int more)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            more = Math.Max(0, lines.Length - max);
            return lines.Take(max).ToArray();
        }
    }

    // ------------------------------------------------------------ fichier d'etat commun

    // Une session suivie par l'encoche, quel que soit l'outil : state\<id>.state, en JSON.
    //   {"v":2,"harness":"claude","state":"busy","dir":"...","pid":123,"proc":"claude","transcript":"..."}
    //   {"v":2,"harness":"run","state":"ready","dir":"...","pid":456,"label":"npm test","code":"0","start":"..."}
    // "proc" : nom du processus qui prouve que la session vit (claude, codex...) ; absent pour bip run,
    // dont la preuve est le pid lui-meme et son heure de lancement. L'ancien format (champs separes par
    // "|") est encore lu : etat|dossier|pid|claude|transcript, ou etat|dossier|pid|run|commande|code|debut.
    class StateRecord
    {
        public string Harness = "claude", State, Dir = "", Proc, Transcript, Label, Code;
        public string Title;   // titre fourni par l'outil (Antigravity) ; Claude Code : lu dans le transcript
        public int Pid;
        public DateTime? Start;

        public bool IsRun { get { return Harness == "run"; } }

        static readonly Regex HarnessName = new Regex("^[a-z][a-z0-9-]{0,23}$");

        public static StateRecord Parse(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return null;
            var r = new StateRecord();
            if (raw[0] == '{')
            {
                var d = Json.Obj(Json.Parse(raw));
                if (d == null) return null;
                var h = Json.Str(d, "harness") ?? "claude";
                r.Harness = HarnessName.IsMatch(h) ? h : "claude";
                r.State = Json.Str(d, "state");
                r.Dir = Json.Str(d, "dir") ?? "";
                int pid; int.TryParse(Json.Str(d, "pid"), out pid); r.Pid = pid;
                var proc = Json.Str(d, "proc");
                r.Proc = proc != null && HarnessName.IsMatch(proc) ? proc : null;
                r.Transcript = Json.Str(d, "transcript");
                var title = Json.Str(d, "title");
                r.Title = title == null ? null : Logic.Visible(title.Length > 60 ? title.Substring(0, 60) : title);
                r.Label = Json.Str(d, "label");
                r.Code = Json.Str(d, "code");
                r.Start = Date(Json.Str(d, "start"));
                return r;
            }
            var p = raw.Split(new[] { '|' }, 7);
            r.State = p[0].Trim();
            r.Dir = p.Length > 1 ? p[1].Trim() : "";
            if (p.Length > 2) int.TryParse(p[2].Trim(), out r.Pid);
            bool run = p.Length > 3 && p[3].Trim() == "run";
            r.Harness = run ? "run" : "claude";
            r.Proc = run ? null : "claude";
            if (p.Length > 4) { if (run) r.Label = p[4].Trim(); else r.Transcript = p[4].Trim(); }
            if (p.Length > 5) r.Code = p[5].Trim();
            if (p.Length > 6) r.Start = Date(p[6].Trim());
            return r;
        }

        public string Write()
        {
            var d = new Dictionary<string, object> { { "v", 2 }, { "harness", Harness }, { "state", State }, { "dir", Dir ?? "" }, { "pid", Pid } };
            if (Proc != null) d["proc"] = Proc;
            if (!string.IsNullOrEmpty(Transcript)) d["transcript"] = Transcript;
            if (!string.IsNullOrEmpty(Title)) d["title"] = Title;
            if (Label != null) d["label"] = Label;
            if (Code != null) d["code"] = Code;
            if (Start != null) d["start"] = Start.Value.ToString("o", CultureInfo.InvariantCulture);
            return Json.Write(d);
        }

        static DateTime? Date(string s)
        {
            DateTime t;
            if (string.IsNullOrEmpty(s) || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) return null;
            return t.ToLocalTime();
        }
    }

    // ------------------------------------------------------------ budget d'attention

    // Ce que l'encoche annonce en s'entrouvrant, par ordre de priorite :
    //   0  une demande (permission, question) : toujours en tete, interrompt le reste
    //   1  une commande suivie qui finit (bip run)
    //   2  un resume (fin d'appel) ou une info importante de Bip Connect
    //   3  une info ordinaire de Bip Connect
    // Une seule annonce a la fois. Une annonce plus prioritaire interrompt celle en cours ; les autres
    // attendent leur tour et expirent. Au-dela de la priorite 1, une meme source ne s'annonce pas plus
    // d'une fois par intervalle : l'encoche ne devient pas un centre de notifications.
    class Notice
    {
        public int Priority;
        public string Key;           // meme cle : la nouvelle annonce remplace l'ancienne
        public string Source;        // plafond par source (priorite 2 et plus)
        public DateTime Created, Expires;
        public bool EvenQuiet;       // montree meme en "ne pas deranger" (l'annonce de la sourdine elle-meme)
        public Func<bool> Valid;     // la demande attend toujours, par exemple
        public object Payload;
    }

    class Attention
    {
        public static readonly TimeSpan SourceInterval = TimeSpan.FromMinutes(2);

        readonly List<Notice> _queue = new List<Notice>();
        readonly Dictionary<string, DateTime> _lastBySource = new Dictionary<string, DateTime>();

        public int Count { get { return _queue.Count; } }

        public void Add(Notice n)
        {
            _queue.RemoveAll(x => x.Key != null && x.Key == n.Key);
            _queue.Add(n);
        }

        public void Remove(string key) { _queue.RemoveAll(x => x.Key == key); }

        public void Drop(Predicate<Notice> which) { _queue.RemoveAll(which); }

        bool Alive(Notice n, DateTime now)
        {
            if (now >= n.Expires) return false;
            try { return n.Valid == null || n.Valid(); } catch { return false; }
        }

        // Prochaine annonce a montrer maintenant, ou null. "current" : la priorite de l'annonce
        // affichee (null si rien) ; seule une annonce strictement plus prioritaire l'interrompt.
        public Notice Next(DateTime now, bool quiet, int? current)
        {
            // Une info dont la source vient de s'annoncer est abandonnee, pas differee : elle reste
            // lisible dans le panneau, sans entrouverture surprise quelques minutes plus tard.
            _queue.RemoveAll(n => !Alive(n, now) || (n.Priority >= 2 && n.Source != null && Throttled(n.Source, now)));
            var next = _queue.Where(n => !quiet || n.EvenQuiet)
                             .OrderBy(n => n.Priority).ThenBy(n => n.Created).FirstOrDefault();
            if (next == null || (current != null && next.Priority >= current.Value)) return null;
            _queue.Remove(next);
            if (next.Priority >= 2 && next.Source != null) _lastBySource[next.Source] = now;
            return next;
        }

        bool Throttled(string source, DateTime now)
        {
            DateTime last;
            return _lastBySource.TryGetValue(source, out last) && now - last < SourceInterval;
        }
    }

    // ------------------------------------------------------------ messages de Bip Connect

    // Bip Connect (et "bip info") n'envoie a l'encoche que de quoi afficher : jamais une reponse a
    // une demande, jamais un changement d'etat de session. Une ligne JSON par connexion :
    //   {"source":"agenda","title":"Point equipe dans 10 min","text":"Teams","priority":2,"ttl":600}
    class InfoMessage
    {
        public const int MaxBytes = 4096;
        public string Source, Title, Text, Key;
        public int Priority;
        public TimeSpan Ttl;

        static readonly Regex SourceName = new Regex("^[a-z0-9][a-z0-9-]{0,23}$");

        // Noms qui pourraient faire passer une info pour un message de Bip ou d'un agent.
        static readonly HashSet<string> Reserved = new HashSet<string>
            { "bip", "claude", "codex", "antigravity", "agy", "agent", "session", "run", "permission", "demande", "question" };

        // Message valide (textes rendus visibles et bornes), ou null avec la raison du refus.
        public static InfoMessage Parse(string json, out string error)
        {
            error = null;
            Dictionary<string, object> d = null;
            try { d = Json.Obj(Json.Parse(json ?? "")); } catch { }
            if (d == null) { error = "JSON illisible"; return null; }
            var source = Json.Str(d, "source");
            if (source == null || !SourceName.IsMatch(source)) { error = "source invalide (a-z, 0-9, tiret, 24 caractères au plus)"; return null; }
            if (Reserved.Contains(source)) { error = "source réservée"; return null; }
            var title = Json.Str(d, "title");
            if (string.IsNullOrWhiteSpace(title)) { error = "titre manquant"; return null; }
            int prio = 3, ttl = 300, v;
            if (int.TryParse(Json.Str(d, "priority"), out v)) prio = v;
            if (int.TryParse(Json.Str(d, "ttl"), out v)) ttl = v;
            var key = Json.Str(d, "key");
            return new InfoMessage
            {
                Source = source,
                Title = Clip(Logic.Visible(title.Replace("\n", " ").Trim()), 80),
                Text = Clip(Logic.Visible((Json.Str(d, "text") ?? "").Replace("\n", " ").Trim()), 280),
                // Jamais au niveau d'une demande ni d'une commande : 2 ou 3 seulement.
                Priority = prio <= 2 ? 2 : 3,
                Ttl = TimeSpan.FromSeconds(Math.Max(10, Math.Min(3600, ttl))),
                Key = source + ":" + (key != null ? Clip(key, 64) : title.Trim())
            };
        }

        static string Clip(string s, int max) { return s.Length > max ? s.Substring(0, max - 1) + "…" : s; }
    }
}
