// Tests du journal d'audit : masquage des secrets, cle des regles suggerees, chainage.
// Lances par tests\run.cmd (et par la release GitHub avant de publier).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClaudeNotch;

static class AuditTests
{
    static int _fail;

    static void Check(string label, bool ok)
    {
        Console.WriteLine((ok ? "  ok    " : "  ÉCHEC ") + label);
        if (!ok) _fail++;
    }

    // Dossier temporaire et empreinte de fin de chaine qu'il a laissee dans le registre.
    static void Clean(string root)
    {
        try { Directory.Delete(root, true); } catch { }
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Bip\Audit", true))
                if (k != null) k.DeleteValue(Audit.HeadName(root), false);
        }
        catch { }
    }

    static void Masked(string input, string secret)
    {
        var m = Audit.Mask(input);
        Check("masque : " + input, !m.Contains(secret) && m.Contains("[masqué]"));
    }

    static int Main()
    {
        Console.WriteLine("Masquage des secrets");
        Masked("curl -H \"Authorization: Bearer abcdefghijklmnop123\" x", "abcdefghijklmnop123");
        Masked("set DB_PASSWORD=hunter2hunter2", "hunter2hunter2");
        Masked("export GITHUB_TOKEN=ghp_abcdefghijklmnopqrstu", "abcdefghijklmnopqrstu");
        Masked("ANTHROPIC_API_KEY=sk-ant-api03-abcdefghijklmnop", "abcdefghijklmnop");
        Masked("psql postgres://admin:S3cretPass@db:5432/app", "S3cretPass");
        Masked("curl -u florian:motdepasse123 https://x", "motdepasse123");
        Masked("az storage --connection-string \"AccountKey=QWERTYUIOPasdfgh==;\"", "QWERTYUIOPasdfgh");
        Masked("https://api.example.com/v1?access_token=zzzzyyyyxxxx", "zzzzyyyyxxxx");
        Check("texte ordinaire intact", Audit.Mask("npm run build -- --prod") == "npm run build -- --prod");

        Console.WriteLine("Cle des regles suggerees");
        Func<string, string> k = Audit.RuleKey;
        Check("npm test -> npm test", k("npm test") == "npm test");
        Check("pytest -q -> pytest", k("pytest -q") == "pytest");
        Check("git status -> git status", k("git status") == "git status");
        Check("npm run test -> npm run test", k("npm run test") == "npm run test");
        Check("npm run (sans script) -> aucune", k("npm run") == null);
        Check("git --no-pager log -> aucune", k("git --no-pager log") == null);
        Check("git -C x status -> aucune", k("git -C x status") == null);
        Check("rm a.tmp -> aucune", k("rm a.tmp") == null);
        Check("python script.py -> aucune", k("python script.py") == null);
        Check("node -e ... -> aucune", k("node -e \"x\"") == null);
        Check("curl url -> aucune", k("curl https://x") == null);
        Check("git push -> aucune", k("git push origin main") == null);
        Check("commande composee -> aucune", k("npm test && npm run build") == null);
        Check("redirection -> aucune", k("echo x > f.txt") == null);

        Console.WriteLine("Chainage des empreintes");
        var root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 5; i++)
                Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo " + i } });
            int n;
            Check("chaine intacte (5 evenements)", Audit.Verify(root, out n) == null && n == 5);
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            var lines = File.ReadAllLines(file);
            // Course : empreinte du registre lue juste avant que deux lignes s'ajoutent (hooks actifs).
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Bip\Audit"))
                key.SetValue(Audit.HeadName(root), Audit.Sha(lines[2]));
            Check("empreinte un peu en retard sur le journal : intact", Audit.Verify(root, out n) == null && n == 5);
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Bip\Audit"))
                key.SetValue(Audit.HeadName(root), Audit.Sha(lines[4]));
            lines[2] = lines[2].Replace("echo 2", "echo 9");
            File.WriteAllLines(file, lines);
            Check("ligne modifiee detectee", Audit.Verify(root, out n) != null);
            File.WriteAllLines(file, new[] { lines[0], lines[1] });
            Check("fin tronquee detectee", Audit.Verify(root, out n) != null);
        }
        finally { Clean(root); }

        Console.WriteLine("Regroupement et reprise");
        Check("docker compose ps -> docker compose ps", k("docker compose ps") == "docker compose ps");
        Check("docker compose run -> aucune", k("docker compose run web sh") == null);
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Hooks paralleles : le resultat arrive avant l'outil dans le fichier.
            Audit.Write(root, "fait", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "npm test" }, { "tuid", "t1" } });
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "npm test" }, { "tuid", "t1" } });
            var rows = Audit.Rows(root, 1);
            Check("resultat avant l'outil rattache", rows.Count == 1 && rows[0].Outcome == "réussi");

            // Ecriture interrompue : la ligne suivante repart proprement.
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            File.AppendAllText(file, "{\"ev\":\"outil\",\"obj\":\"coup");
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo ok" } });
            var last = File.ReadAllLines(file);
            Check("ligne coupee isolee", last.Length == 4 && last[3].StartsWith("{") && last[3].Contains("echo ok"));
        }
        finally { Clean(root); }

        Console.WriteLine("Lecteur JSON");
        var o = Json.Obj(Json.Parse("{\"a\":\"\\u00e9t\\u00e9 \\\"x\\\"\\n\",\"n\":42,\"big\":12345678901,\"f\":1.5e2,\"neg\":-3,\"t\":true,\"z\":null,\"arr\":[1,{\"b\":[]}],\"e\":{}}"));
        Check("chaine avec echappements", Json.Str(o, "a") == "été \"x\"\n");
        Check("nombres : int, long, decimal", Json.Get(o, "n") is int && Json.Get(o, "big") is long && Convert.ToDouble(Json.Get(o, "f")) == 150 && (int)Json.Get(o, "neg") == -3);
        Check("vrai, null, tableau, objet vide", (bool)Json.Get(o, "t") && Json.Get(o, "z") == null && Json.Arr(Json.Get(o, "arr")).Length == 2 && Json.Obj(Json.Get(o, "e")).Count == 0);
        Check("aller-retour Write -> Parse", Json.Str(Json.Obj(Json.Parse(Json.Write(o))), "a") == "été \"x\"\n");
        bool threw = false;
        try { Json.Parse("{\"a\":1,}"); } catch (FormatException) { threw = true; }
        Check("JSON invalide refuse", threw);
        threw = false;
        try { Json.Parse("{\"a\":\"coup"); } catch (FormatException) { threw = true; }
        Check("ligne coupee refusee", threw);

        Console.WriteLine("Lecture incrementale du journal");
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 3; i++)
                Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo " + i }, { "tuid", "u" + i } });
            Check("3 actions lues", Audit.Rows(root, 1).Count == 3);
            Audit.Write(root, "fait", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo 1" }, { "tuid", "u1" } });
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo 3" }, { "tuid", "u3" } });
            var rows = Audit.Rows(root, 1);
            Check("ajouts lus, resultat rattache a une action deja en cache", rows.Count == 4 && rows[1].Outcome == "réussi");
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            File.AppendAllText(file, "{\"ev\":\"outil\",\"sess\":\"s\",\"tool\":\"Bash\",\"obj\":\"moi");
            Check("ligne en cours d'ecriture ignoree", Audit.Rows(root, 1).Count == 4);
            File.AppendAllText(file, "tie\"}\n");
            Check("ligne terminee lue ensuite", Audit.Rows(root, 1).Count == 5);
            File.WriteAllText(file, "");
            Check("fichier raccourci relu en entier", Audit.Rows(root, 1).Count == 0);
        }
        finally { Clean(root); }

        Console.WriteLine("Sous-agents");
        Func<int, long> T = s => new DateTime(2026, 10, 14, 15, 38, s).Ticks;
        // Capture reelle du 2026-10-14 : deux en arriere-plan (A, B), un au premier plan (C).
        var ag = Agents.Parse(new[]
        {
            "lance|" + T(24) + "|tA|Compte les fichiers sources",
            "lance|" + T(26) + "|tB|Cherche les constantes de délai",
            "lance|" + T(28) + "|tC|Vérifie la version du CHANGELOG",
            "id|" + T(29) + "|tA|agA",
            "debut|" + T(29) + "|agA", "debut|" + T(29) + "|agB", "debut|" + T(29) + "|agC",
            "id|" + T(29) + "|tB|agB",
            "fin|" + T(35) + "|agA",
            "fin|" + T(35) + "|agC", "id|" + T(35) + "|tC|agC", "fin|" + T(35) + "|agC",
        });
        Check("trois sous-agents, ordre de lancement", ag.Count == 3 && ag[1].Description == "Cherche les constantes de délai");
        Check("arriere-plan relie par la reponse", ag[0].Id == "agA" && ag[1].Id == "agB");
        Check("premier plan apparie par ordre", ag[2].Id == "agC");
        Check("fins : A et C finis, B en cours", ag[0].End != null && ag[1].End == null && ag[2].End != null);
        // Premier plan pas encore relie (reponse pas arrivee) : appariement par le debut libre.
        var ag2 = Agents.Parse(new[] { "lance|" + T(1) + "|t1|Un", "lance|" + T(2) + "|t2|Deux", "debut|" + T(3) + "|x1", "debut|" + T(3) + "|x2", "fin|" + T(9) + "|x2" });
        Check("deux au premier plan, appariement dans l'ordre", ag2[0].Id == "x1" && ag2[1].Id == "x2" && ag2[1].End != null && ag2[0].End == null);
        var ag3 = Agents.Parse(new[] { "lance|" + T(1) + "|t1|Un", "echec|" + T(4) + "|t1", "ligne illisible" });
        Check("echec de l'outil : termine en echec", ag3.Count == 1 && ag3[0].Failed && ag3[0].End != null);

        Console.WriteLine("Titre de la session");
        Check("dernier titre retenu", Agents.LastTitle("{\"type\":\"custom-title\",\"customTitle\":\"Claude Voyant\"}\n{\"x\":1}\n{\"type\":\"custom-title\",\"customTitle\":\"Claude Voyant - Bip\"}\n") == "Claude Voyant - Bip");
        Check("titre avec accents et guillemets", Agents.LastTitle("{\"customTitle\":\"Feuille de route \\\"IA\\\" \\u00e9t\\u00e9\"}") == "Feuille de route \"IA\" été");
        Check("pas de titre", Agents.LastTitle("{\"type\":\"user\"}") == null);
        var tr = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var filler = new string('x', 300 * 1024);   // titre au-dela du premier bloc lu depuis la fin
            File.WriteAllText(tr, "{\"type\":\"custom-title\",\"customTitle\":\"Ancien\"}\n{\"m\":\"" + filler + "\"}\n");
            Check("titre trouve en remontant plusieurs blocs", Agents.Title(tr) == "Ancien");
            File.AppendAllText(tr, "{\"type\":\"custom-title\",\"customTitle\":\"Renommé\"}\n");
            Check("renommage lu dans les nouvelles lignes", Agents.Title(tr) == "Renommé");
            Check("transcript absent : pas de titre", Agents.Title(tr + ".absent") == null);
        }
        finally { try { File.Delete(tr); } catch { } }

        Console.WriteLine("Regles suggerees : liste blanche (S1)");
        // CLI a un mot : une regle "Bash(x:*)" couvrirait aussi ses commandes destructrices.
        foreach (var c in new[] { "terraform destroy", "aws s3 ls", "az group list", "gh pr list", "gcloud config list", "psql -c x", "sqlcmd -Q x",
                                  "find . -name x", "sed -i s/a/b/ f", "vercel ls", "git branch", "git stash list", "git tag v1", "npm run deploy",
                                  "docker run alpine", "kubectl delete pod x", "Get-ChildItem" })
            Check(c + " -> aucune", k(c) == null);
        // Relecture Gemini : ecriture de fichiers (--output, --fix), conteneurs, scripts au suffixe libre.
        foreach (var c in new[] { "git diff --output=x", "git log -p", "git show HEAD", "eslint src", "docker compose up -d", "docker compose build",
                                  "npm run build:deploy", "npm run check-and-deploy", "npm run build_destroy", "npm run test:publish" })
            Check(c + " -> aucune", k(c) == null);
        Check("npm run test:e2e -> npm run test:e2e", k("npm run test:e2e") == "npm run test:e2e");
        Check("terraform plan -> terraform plan", k("terraform plan -out x") == "terraform plan");
        Check("kubectl get pods -> kubectl get", k("kubectl get pods") == "kubectl get");
        Check("npm run test:unit -> npm run test:unit", k("npm run test:unit") == "npm run test:unit");
        Check("uv run pytest -> uv run pytest", k("uv run pytest -q") == "uv run pytest");
        Check("cargo test -> cargo test", k("cargo test --all") == "cargo test");

        Console.WriteLine("Masquage : formats ajoutes (S2)");
        Masked("mysql -u root -pS3cr3tP4ss db", "S3cr3tP4ss");
        Masked("sshpass -p hunter2pw ssh h", "hunter2pw");
        Masked("az login --service-principal -u id -p Pa55w0rd! --tenant t", "Pa55w0rd!");
        Masked("docker login -p MyDockerPass reg.io", "MyDockerPass");
        Masked("echo -----BEGIN RSA PRIVATE KEY-----MIIEpAIBAAKCAQEA\nabc\n-----END RSA PRIVATE KEY-----", "MIIEpAIBAAKCAQEA");
        Masked("curl https://hooks.slack.com/services/T000/B000/XXXXSECRETXXXX", "XXXXSECRETXXXX");
        Masked("curl https://discord.com/api/webhooks/123/TOKENSECRETabc", "TOKENSECRETabc");
        Masked("sshpass -p \"mon mot de passe\" ssh h", "mot de passe");
        Masked("mysql -u root -p\"pass word\" db", "word");
        Masked("curl https://hooks.slack.com/triggers/T0/123/abcSECRETdef", "abcSECRETdef");
        Check("mysql -P3306 (port) intact", Audit.Mask("mysql -h db -P3306 -u root") == "mysql -h db -P3306 -u root");
        Check("git log login -p intact", Audit.Mask("git log login -p src/App.cs") == "git log login -p src/App.cs");
        Check("mysql -p seul (invite) intact", Audit.Mask("mysql -u root -p db") == "mysql -u root -p db");
        Check("mkdir -p intact", Audit.Mask("mkdir -p src/a") == "mkdir -p src/a");

        Console.WriteLine("Entrees des outils MCP (S3)");
        var mcp = Audit.Summarize("mcp__vault__write_note", new Dictionary<string, object>
        {
            { "rel_path", "projets/x.md" }, { "content", "Ligne 1 confidentielle\nLigne 2" }, { "overwrite", true },
            { "body", new string('a', 500) }, { "tags", new object[] { "a", "b" } }, { "meta", new Dictionary<string, object> { { "k", "v" } } }
        });
        Check("chemin court garde", mcp.Contains("projets/x.md"));
        Check("contenu multiligne remplace par sa taille", !mcp.Contains("confidentielle") && mcp.Contains("[30 caractères]"));
        Check("texte long remplace par sa taille", !mcp.Contains("aaaa") && mcp.Contains("[500 caractères]"));
        Check("booleen, tableau, objet resumes", mcp.Contains("true") && mcp.Contains("[2 éléments]") && mcp.Contains("{…}"));
        var sql = Audit.Summarize("mcp__postgres__query", new Dictionary<string, object> { { "query", "SELECT ssn FROM users" }, { "db", "prod" } });
        Check("champ de contenu court remplace aussi", !sql.Contains("ssn") && sql.Contains("prod"));
        var cr = Audit.Summarize("mcp__x__y", new Dictionary<string, object> { { "titre", "a\rb" } });
        Check("retour chariot seul traite comme multiligne", !cr.Contains("a\\rb"));

        Console.WriteLine("Rapport robuste (R1)");
        Check("dernier dossier", Audit.LastSegment("D:\\Projets\\Bip\\") == "Bip" && Audit.LastSegment("/home/a/b") == "b" && Audit.LastSegment("D:") == "D:");
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Dossier avec une tabulation : Path.GetFileName levait une exception, et tout le rapport echouait.
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo x" }, { "dir", "D:\\a\tb" } });
            string project = null;
            try { project = Audit.Rows(root, 1)[0].Project; } catch { }
            Check("dossier avec caractere de controle lisible", project == "a\tb");
        }
        finally { Clean(root); }

        Console.WriteLine("Encoche repliee : attente jamais cachee (F1)");
        // (nom, attend, debut de l'attente) dans l'ordre de lancement
        var sess = new[] { Tuple.Create("s1", false, 0), Tuple.Create("s2", true, 5), Tuple.Create("s3", false, 0), Tuple.Create("s4", false, 0),
                           Tuple.Create("s5", false, 0), Tuple.Create("s6", false, 0), Tuple.Create("s7", true, 2), Tuple.Create("s8", true, 1) };
        Func<Tuple<string, bool, int>, bool> waits = s => s.Item2;
        Func<Tuple<string, bool, int>, DateTime> since = s => new DateTime(2026, 1, 1).AddMinutes(s.Item3);
        var pick = string.Join(",", Logic.Collapsed(sess, 5, waits, since).Select(s => s.Item1));
        Check("attentes hors des 5 promues, ordre garde : " + pick, pick == "s1,s2,s3,s7,s8");
        pick = string.Join(",", Logic.Collapsed(sess.Take(5).ToList(), 5, waits, since).Select(s => s.Item1));
        Check("5 sessions ou moins : inchange", pick == "s1,s2,s3,s4,s5");

        Console.WriteLine("bip run entre guillemets (F2)");
        Func<string, bool> none = f => false;
        Check("guillemets autour de tout retires", Logic.UnwrapCommand("\"npm run build && npm test\"", none) == "npm run build && npm test");
        Check("chemin entre guillemets garde", Logic.UnwrapCommand("\"C:\\Program Files\\x.exe\" arg", none) == "\"C:\\Program Files\\x.exe\" arg");
        Check("plusieurs groupes de guillemets gardes", Logic.UnwrapCommand("\"a\" \"b\"", none) == "\"a\" \"b\"");
        Check("sans guillemets inchange", Logic.UnwrapCommand(" npm test ", none) == "npm test");
        // Relecture Gemini : un programme seul dont le chemin a des espaces garde ses guillemets.
        Check("programme existant seul garde ses guillemets",
              Logic.UnwrapCommand("\"C:\\Program Files\\app.exe\"", f => f == "C:\\Program Files\\app.exe") == "\"C:\\Program Files\\app.exe\"");

        Console.WriteLine("Phase 1 : fichier d'etat commun");
        var legacy = StateRecord.Parse("busy|D:\\Projets\\x|1234|claude|C:\\t.jsonl");
        Check("ancien format claude lu", legacy.Harness == "claude" && legacy.Proc == "claude" && legacy.Pid == 1234 && legacy.Transcript == "C:\\t.jsonl" && legacy.State == "busy");
        var legacyRun = StateRecord.Parse("error|D:\\x|55|run|npm test|3|2026-10-28T10:00:00.0000000+01:00");
        Check("ancien format run lu", legacyRun.IsRun && legacyRun.Proc == null && legacyRun.Label == "npm test" && legacyRun.Code == "3" && legacyRun.Start != null);
        var rec = new StateRecord { Harness = "codex", State = "waiting", Dir = "D:\\a|b", Proc = "codex", Pid = 99, Transcript = "t" };
        var back = StateRecord.Parse(rec.Write());
        Check("aller-retour JSON (codex, dossier avec |)", back.Harness == "codex" && back.Proc == "codex" && back.Dir == "D:\\a|b" && back.Pid == 99 && back.State == "waiting");
        var runRec = new StateRecord { Harness = "run", State = "ready", Pid = 7, Label = "x", Code = "0", Start = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local) };
        var runBack = StateRecord.Parse(runRec.Write());
        Check("aller-retour JSON run avec heure de lancement", runBack.IsRun && runBack.Start == runRec.Start && runBack.Code == "0");
        Check("outil invalide ramene a claude", StateRecord.Parse("{\"harness\":\"../x\",\"state\":\"busy\",\"proc\":\"a b\"}").Harness == "claude"
                                              && StateRecord.Parse("{\"harness\":\"../x\",\"state\":\"busy\",\"proc\":\"a b\"}").Proc == null);
        Check("fichier vide ou illisible", StateRecord.Parse("") == null && StateRecord.Parse("   ") == null);

        Console.WriteLine("Phase 1 : budget d'attention");
        var t0 = new DateTime(2026, 10, 28, 10, 0, 0);
        Func<int, string, Notice> N = (p, key) => new Notice { Priority = p, Key = key, Created = t0, Expires = t0.AddMinutes(5) };
        var at = new Attention();
        at.Add(N(1, "run")); at.Add(N(0, "demande")); at.Add(N(3, "info"));
        Check("la demande passe en premier", at.Next(t0, false, null).Key == "demande");
        Check("rien n'interrompt une annonce de meme priorite ou moindre", at.Next(t0, false, 1) == null);
        Check("puis la commande", at.Next(t0, false, null).Key == "run");
        at.Add(N(0, "demande2"));
        Check("une demande interrompt une info", at.Next(t0, false, 3).Key == "demande2");
        Check("une info n'interrompt pas une demande", at.Next(t0, false, 0) == null);
        at = new Attention();
        at.Add(N(1, "a")); at.Add(new Notice { Priority = 2, Key = "sourdine", Created = t0, Expires = t0.AddMinutes(1), EvenQuiet = true });
        Check("ne pas deranger : seule l'annonce de la sourdine passe", at.Next(t0, true, null).Key == "sourdine" && at.Next(t0, true, null) == null);
        Check("annonce gardee pour apres la sourdine", at.Next(t0, false, null).Key == "a");
        at = new Attention();
        at.Add(N(1, "vieux"));
        Check("annonce expiree abandonnee", at.Next(t0.AddMinutes(6), false, null) == null && at.Count == 0);
        bool still = false;
        at.Add(new Notice { Priority = 0, Key = "d", Created = t0, Expires = t0.AddHours(1), Valid = () => still });
        Check("demande deja repondue abandonnee", at.Next(t0, false, null) == null);
        at.Add(N(1, "k")); at.Add(new Notice { Priority = 1, Key = "k", Created = t0.AddSeconds(1), Expires = t0.AddMinutes(5), Payload = "nouveau" });
        Check("meme cle : la nouvelle remplace l'ancienne", at.Count == 1 && (string)at.Next(t0, false, null).Payload == "nouveau");
        Func<string, Notice> I = key => new Notice { Priority = 3, Key = key, Source = "connect:agenda", Created = t0, Expires = t0.AddHours(1) };
        at.Add(I("i1")); at.Add(I("i2"));
        Check("plafond par source : une info", at.Next(t0, false, null).Key == "i1" && at.Next(t0.AddSeconds(30), false, null) == null);
        Check("la suivante est abandonnee, pas differee", at.Count == 0 && at.Next(t0 + Attention.SourceInterval, false, null) == null);
        at.Add(I("i3"));
        Check("une nouvelle info apres l'intervalle s'annonce", at.Next(t0 + Attention.SourceInterval, false, null).Key == "i3");
        at.Add(N(0, "x")); at.Add(N(2, "y")); at.Drop(n => n.Priority <= 1);
        Check("panneau ouvert : demandes et commandes retirees de la file", at.Count == 1);

        Console.WriteLine("Phase 1 : caracteres invisibles et extraits (S4, S5)");
        Check("inversion de texte rendue visible", Logic.Visible("rm\u202Efdp.exe") == "rm[U+202E]fdp.exe");
        Check("saut de ligne et tabulation gardes", Logic.Visible("a\n\tb") == "a\n\tb");
        int more;
        var head = Logic.Head("1\r\n2\n3\n4", 2, out more);
        Check("extrait : 2 premieres lignes et 2 de plus", head.Length == 2 && head[1] == "2" && more == 2);
        Logic.Head("1\n2", 400, out more);
        Check("extrait : tout tient", more == 0);

        Console.WriteLine("Phase 1 : messages de Bip Connect");
        string err;
        var im = InfoMessage.Parse("{\"source\":\"agenda\",\"title\":\"Point équipe\",\"text\":\"Teams\\ndans 10 min\",\"priority\":2,\"ttl\":600}", out err);
        Check("message valide", im != null && im.Source == "agenda" && im.Priority == 2 && im.Ttl == TimeSpan.FromMinutes(10) && im.Text == "Teams dans 10 min");
        Check("source invalide refusee", InfoMessage.Parse("{\"source\":\"Agenda X\",\"title\":\"t\"}", out err) == null && err.StartsWith("source"));
        Check("titre manquant refuse", InfoMessage.Parse("{\"source\":\"a\"}", out err) == null);
        Check("JSON illisible refuse", InfoMessage.Parse("pas du json", out err) == null);
        Check("priorite jamais au niveau d'une demande", InfoMessage.Parse("{\"source\":\"a\",\"title\":\"t\",\"priority\":0}", out err).Priority == 2);
        Check("priorite par defaut : 3", InfoMessage.Parse("{\"source\":\"a\",\"title\":\"t\"}", out err).Priority == 3);
        Check("duree bornee (10 s a 1 h)", InfoMessage.Parse("{\"source\":\"a\",\"title\":\"t\",\"ttl\":99999}", out err).Ttl == TimeSpan.FromHours(1)
                                         && InfoMessage.Parse("{\"source\":\"a\",\"title\":\"t\",\"ttl\":1}", out err).Ttl == TimeSpan.FromSeconds(10));
        var longMsg = InfoMessage.Parse("{\"source\":\"a\",\"title\":\"" + new string('x', 200) + "\",\"text\":\"" + new string('y', 500) + "\"}", out err);
        Check("titre et texte bornes", longMsg.Title.Length == 80 && longMsg.Text.Length == 280);
        Check("caracteres invisibles du titre rendus visibles", InfoMessage.Parse("{\"source\":\"a\",\"title\":\"x\\u202Ey\"}", out err).Title == "x[U+202E]y");

        Console.WriteLine("Phase 1 : relecture de securite");
        Check("identifiant assaini comme nom de fichier", Logic.SanitizeId("a/b\\c:d", null) == "a_b_c_d");
        Check("identifiant absent : dossier, espaces et deux-points remplaces", Logic.SanitizeId(null, "D:\\mon projet") == "D__mon_projet");
        Check("identifiant vide ou fait de points : session", Logic.SanitizeId("..", null) == "session" && Logic.SanitizeId("", "") == "session");
        Check("blancs de remplissage rendus visibles", Logic.Visible("a\u2800b\u3164c\u034Fd") == "a[U+2800]b[U+3164]c[U+034F]d");
        Check("source reservee refusee", InfoMessage.Parse("{\"source\":\"claude\",\"title\":\"t\"}", out err) == null && err == "source réservée"
                                       && InfoMessage.Parse("{\"source\":\"bip\",\"title\":\"t\"}", out err) == null);
        Check("transcript reseau ignore", Agents.Title("\\\\serveur\\partage\\x.jsonl") == null && Agents.Title("//serveur/x") == null);
        Check("transcript relatif ignore", Agents.Title("x.jsonl") == null);

        Console.WriteLine("Phase 2 : Antigravity");
        // Entrees reelles capturees le 28/10/2026 par un hook espion (abregees).
        var agyStart = Json.Obj(Json.Parse("{\"conversationId\":\"c98\",\"lastUserInput\":\"Relis la branche fix\\nsecond\",\"workspacePaths\":[\"C:/Users/Ludo/agy-test\"]}"));
        Check("dossier : premier espace de travail, barres Windows", Logic.AgyDir(agyStart) == "C:\\Users\\Ludo\\agy-test");
        Check("titre : premiere ligne de la demande", Logic.AgyTitle(agyStart) == "Relis la branche fix");
        Check("titre long borne a 60", Logic.AgyTitle(Json.Obj(Json.Parse("{\"lastUserInput\":\"" + new string('x', 200) + "\"}"))).Length == 60);
        Check("sans espace de travail : dossier vide", Logic.AgyDir(Json.Obj(Json.Parse("{}"))) == "");
        Check("SessionStart : au repos", Logic.AgyState("SessionStart", agyStart) == "idle");
        Check("PreToolUse, PostInvocation : en cours", Logic.AgyState("PreToolUse", agyStart) == "busy" && Logic.AgyState("PostInvocation", agyStart) == "busy");
        Check("Stop sans erreur, tout fini : pret", Logic.AgyState("Stop", Json.Obj(Json.Parse("{\"error\":\"\",\"fullyIdle\":true}"))) == "ready");
        Check("Stop avec taches de fond : toujours en cours", Logic.AgyState("Stop", Json.Obj(Json.Parse("{\"error\":\"\",\"fullyIdle\":false}"))) == "busy");
        Check("Stop en erreur : erreur", Logic.AgyState("Stop", Json.Obj(Json.Parse("{\"error\":\"quota\",\"fullyIdle\":true}"))) == "error");
        Check("decision neutre : JSON valide, allow", Json.Str(Json.Obj(Json.Parse(Logic.AgyNeutralPreToolUse)), "decision") == "allow");
        var agyRec = StateRecord.Parse(new StateRecord { Harness = "agy", State = "busy", Dir = "D:\\x", Title = "Relis \u202E la branche" }.Write());
        Check("titre garde dans le fichier d'etat, rendu visible", agyRec.Harness == "agy" && agyRec.Title == "Relis [U+202E] la branche");
        Check("commande Antigravity resumee", Audit.Summarize("run_command", Json.Obj(Json.Parse("{\"CommandLine\":\"dir /s\",\"WaitMsBeforeAsync\":5000}"))) == "dir /s");

        Console.WriteLine("Phase 3 : capture rapide");
        var at10 = new DateTime(2026, 10, 29, 10, 12, 0);
        Check("ligne horodatee", Logic.CaptureLine("  idée : bip qui danse  ", at10) == "- 2026-10-29 10:12 · idée : bip qui danse");
        Check("une seule ligne", Logic.CaptureLine("a\nb\r\nc\td", at10) == "- 2026-10-29 10:12 · a b  c d");
        Check("vide : rien", Logic.CaptureLine("   ", at10) == null && Logic.CaptureLine(null, at10) == null);
        Check("caractere invisible rendu visible", Logic.CaptureLine("x‮y", at10).EndsWith("x[U+202E]y"));
        Check("500 caracteres au plus", Logic.CaptureLine(new string('z', 900), at10).Length == "- 2026-10-29 10:12 · ".Length + 500);
        Check("en-tete au format des notes du vault", Logic.InboxHeader.StartsWith("---\nname: ideation-inbox\n") && Logic.InboxHeader.Contains("type: inbox"));

        Console.WriteLine("Phase 3 : bilan du matin");
        Check("nuit : depuis la veille a 20 h", Logic.NightStart(at10) == new DateTime(2026, 10, 28, 20, 0, 0));
        Check("matin de 5 h a midi", Logic.IsMorning(at10) && !Logic.IsMorning(at10.Date.AddHours(4)) && !Logic.IsMorning(at10.Date.AddHours(12)));
        Func<string, string, string, Tuple<DateTime, string, string, string>> E = (evt, src, name) => Tuple.Create(at10, evt, src, name);
        var night = Logic.NightSummary(new[] {
            E("termine", "s1", "Audit"), E("termine", "s2", "Veille"), E("travail", "s1", ""), E("commande-ok", "run-1", "npm test"),
            E("commande-ko", "run-2", "build"), E("info3", "logres", "Quota à 80 %"), E("info2", "paperclip", "Agent Perceval en erreur"),
            E("info2", "hermes", "Moteur Hermes indisponible") }, 42, 1);
        Check("bilan : sessions, commandes, actions", night.Count == 2 && night[0] == "2 sessions ont fini · 2 commandes (1 en échec) · 42 actions d'agents · 1 refus");
        Check("bilan : alertes, dont importantes et la derniere", night[1] == "3 alertes reçues · 2 importantes, dernière : Moteur Hermes indisponible");
        Check("nuit calme : rien a dire", Logic.NightSummary(new[] { E("travail", "s", "") }, 0, 0).Count == 0);
        Check("une seule action, sans refus", Logic.NightSummary(new Tuple<DateTime, string, string, string>[0], 1, 0)[0] == "1 action d'agent");

        Console.WriteLine("Phase 4 : agenda recu de Logres (contrat docs/CONTRAT-BIP-AGENDA.md)");
        Func<string, object> J = s => Json.Parse(s);
        string ev1 = "{\"agenda\":\"Pro\",\"titre\":\"Point hebdo\",\"debut\":\"2026-11-02T08:00:00.000Z\",\"fin\":\"2026-11-02T08:30:00.000Z\",\"journee\":false,\"lien\":\"https://teams.microsoft.com/l/meetup-join/abc\"}";
        string ev2 = "{\"agenda\":\"Google\",\"titre\":\"Toussaint\",\"debut\":\"2026-11-01\",\"fin\":\"2026-11-02\",\"journee\":true,\"lien\":null}";
        Func<string, string, string> AG = (evs, errs) => "{\"empreinte\":\"0123456789abcdef\",\"luA\":\"2026-11-02T07:50:00.000Z\",\"evenements\":[" + evs + "],\"erreurs\":[" + errs + "]}";
        var agd = AgendaLogic.Parse(J(AG(ev1 + "," + ev2, "{\"agenda\":\"Perso\",\"erreur\":\"agenda indisponible (code 403)\",\"depuis\":\"2026-11-02T07:00:00.000Z\"}")));
        Check("agenda : lu, trie, heures UTC converties en heure locale", agd != null && agd.Meetings.Count == 2 && agd.Meetings[0].Title == "Toussaint"
              && agd.Meetings[1].Start == new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc).ToLocalTime() && agd.Issues.Count == 1 && agd.Issues[0].Agenda == "Perso");
        Check("agenda : journee entiere a minuit local, fin exclue", agd != null && agd.Meetings[0].AllDay && agd.Meetings[0].Start == new DateTime(2026, 11, 1) && agd.Meetings[0].End == new DateTime(2026, 11, 2));
        Check("agenda : champ en trop ou manquant rejete en entier",
              AgendaLogic.Parse(J(AG(ev1.Replace("\"lien\"", "\"lieu\":\"Salle\",\"lien\""), ""))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace(",\"journee\":false", ""), ""))) == null
              && AgendaLogic.Parse(J("{\"empreinte\":\"0123456789abcdef\",\"luA\":null,\"evenements\":[],\"erreurs\":[],\"adresse\":\"x\"}")) == null);
        Check("agenda : lien hors Teams, Meet, Zoom ou non https rejete",
              AgendaLogic.Parse(J(AG(ev1.Replace("https://teams.microsoft.com", "https://evil.example"), ""))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace("https://teams.microsoft.com", "http://teams.microsoft.com"), ""))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace("https://teams.microsoft.com", "https://teams.microsoft.com.evil.example"), ""))) == null);
        Check("agenda : empreinte, dates et titre bornes",
              AgendaLogic.Parse(J(AG(ev1, "").Replace("0123456789abcdef", "XYZ"))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace("2026-11-02T08:30:00.000Z", "2026-11-02 08:30"), ""))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace("2026-11-02T08:30:00.000Z", "2026-11-01T08:30:00.000Z"), ""))) == null
              && AgendaLogic.Parse(J(AG(ev1.Replace("Point hebdo", new string('x', 121)), ""))) == null);
        var sansFin = AgendaLogic.Parse(J(AG(ev2.Replace("\"fin\":\"2026-11-02\"", "\"fin\":\"2026-11-01\""), "")));
        Check("agenda : journee entiere sans fin = un jour (relecture Gemini)", sansFin != null && sansFin.Meetings[0].End == new DateTime(2026, 11, 2)
              && AgendaLogic.OfDay(sansFin.Meetings, new DateTime(2026, 11, 1)).Count == 1);
        Check("agenda : vide accepte (aucun agenda declare)", AgendaLogic.Parse(J("{\"empreinte\":\"0123456789abcdef\",\"luA\":null,\"evenements\":[],\"erreurs\":[]}")) != null);
        Check("lien : Meet et sous-domaine Zoom acceptes ; identifiants, port refuses",
              AgendaLogic.JoinAllowed("https://meet.google.com/abc") && AgendaLogic.JoinAllowed("https://us02web.zoom.us/j/1")
              && !AgendaLogic.JoinAllowed("https://u:p@meet.google.com/x") && !AgendaLogic.JoinAllowed("https://meet.google.com:8443/x") && !AgendaLogic.JoinAllowed(null));

        var day = new DateTime(2026, 11, 2);
        var ms = new List<Meeting> {
            new Meeting { Agenda = "Pro", Title = "Point", Start = day.AddHours(9), End = day.AddHours(9.5) },
            new Meeting { Agenda = "Pro", Title = "Revue", Start = day.AddHours(14), End = day.AddHours(15) },
            new Meeting { Agenda = "Google", Title = "Demain", Start = day.AddDays(1).AddHours(8), End = day.AddDays(1).AddHours(9) },
            new Meeting { Agenda = "Google", Title = "Toussaint", Start = day.AddDays(-1), End = day, AllDay = true },
            new Meeting { Agenda = "Google", Title = "Conges", Start = day, End = day.AddDays(3), AllDay = true } };
        Check("prochaine : celle du jour dans les 2 h, en cours d'abord, jamais une journee entiere",
              AgendaLogic.Next(ms, day.AddHours(7.5), TimeSpan.FromHours(2)).Title == "Point"
              && AgendaLogic.Next(ms, day.AddHours(9.25), TimeSpan.FromHours(2)).Title == "Point"
              && AgendaLogic.Next(ms, day.AddHours(10), TimeSpan.FromHours(2)) == null
              && AgendaLogic.Next(ms, day.AddHours(23), TimeSpan.FromHours(10)) == null);
        Check("libelle : heure ou en cours", AgendaLogic.Label(ms[1], day.AddHours(13)) == "14:00 Revue" && AgendaLogic.Label(ms[1], day.AddHours(14.5)) == "en cours · Revue");
        Check("en cours : bornes exactes", AgendaLogic.Current(ms, day.AddHours(9)) == ms[0] && AgendaLogic.Current(ms, day.AddHours(9.5)) == null);
        var of = AgendaLogic.OfDay(ms, day.AddHours(12));
        Check("reunions du jour : journees entieres d'abord, fin exclue", of.Count == 3 && of[0].Title == "Conges" && of[1].Title == "Point" && of[2].Title == "Revue");
        Check("ligne du panneau", AgendaLogic.Line(ms[0]) == "09:00–09:30  Point · Pro" && AgendaLogic.Line(ms[4]) == "journée  Conges · Google");
        var st = AgendaLogic.Status(agd, day.AddHours(8), day.AddHours(9));
        Check("etat : perime apres 30 min sans nouvelles, erreurs par agenda", st[0].StartsWith("pas de nouvelles de Logres depuis 08:00")
              && st.Any(l => l.StartsWith("Perso : agenda indisponible (code 403) depuis")) && AgendaLogic.Status(null, day, day)[0] == "aucun agenda reçu de Logres");

        Console.WriteLine("Phase 3 : detail du bilan et musique en cours");
        var det = Logic.NightDetails(new[] {
            Tuple.Create(at10.AddHours(-8), "info2", "paperclip", "Agent Perceval en erreur"),
            Tuple.Create(at10.AddHours(-12), "termine", "s1", "Audit · repo/"),
            Tuple.Create(at10.AddHours(-11), "travail", "s1", ""),
            Tuple.Create(at10.AddHours(-10), "commande-ko", "run-2", "npm run build") });
        Check("detail : dans l'ordre, heure et nature", det.Count == 3 && det[0] == "22:12  session finie : Audit · repo/"
                                                        && det[1] == "00:12  commande en échec : npm run build" && det[2] == "02:12  alerte importante (paperclip) : Agent Perceval en erreur");
        var many20 = Enumerable.Range(0, 25).Select(i => Tuple.Create(at10.AddMinutes(-i), "termine", "s", "n" + i));
        var d25 = Logic.NightDetails(many20);
        Check("detail : 20 lignes au plus, puis +N", d25.Count == 21 && d25[20] == "+5 autres");
        Check("application : Chrome", Logic.MediaAppName("Chrome") == "Chrome" && Logic.MediaAppName(@"C:\Program Files\Google\Chrome\Application\chrome.exe") == "Chrome");
        Check("application : Spotify (Store)", Logic.MediaAppName("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "Spotify");
        Check("application inconnue : nom sans extension", Logic.MediaAppName(@"D:\outils\MonLecteur.exe") == "MonLecteur" && Logic.MediaAppName(null) == "lecteur");
        Check("ligne : titre — artiste", Logic.MediaLine("Dark Tech House", "Gotha Wave") == "Dark Tech House — Gotha Wave");
        Check("ligne : titre seul, artiste seul, rien", Logic.MediaLine("T", "") == "T" && Logic.MediaLine("", "A") == "A" && Logic.MediaLine(null, null) == "lecture en cours");

        Console.WriteLine("Phase 3 : ecriture de la capture et journal sur plusieurs jours (Model.cs)");
        Directory.CreateDirectory(Program.Root);
        try
        {
            var inbox = Path.Combine(Program.Root, "ideation", "inbox.md");
            Check("dossier absent : rien n'est ecrit, rien n'est cree", Capture.Append(inbox, "- x") != null && !Directory.Exists(Path.GetDirectoryName(inbox)));
            Directory.CreateDirectory(Path.GetDirectoryName(inbox));
            Check("premiere capture : fichier cree", Capture.Append(inbox, "- 2026-10-29 10:12 · un") == null);
            var t3 = File.ReadAllText(inbox);
            Check("en-tete puis la ligne", t3.StartsWith("---") && t3.Contains("# Boite de capture") && t3.TrimEnd().EndsWith("· un"));
            File.AppendAllText(inbox, "ligne ajoutee a la main sans fin de ligne");
            Check("seconde capture", Capture.Append(inbox, "- 2026-10-29 10:13 · deux") == null);
            var l3 = File.ReadAllLines(inbox);
            Check("repart sur une ligne propre, en-tete non repete", l3.Last() == "- 2026-10-29 10:13 · deux" && l3.Count(l => l == "---") == 2);

            var jdir = Path.Combine(Program.Root, "journal");
            Directory.CreateDirectory(jdir);
            var hier = new DateTime(2026, 10, 28);
            File.WriteAllText(Path.Combine(jdir, "2026-10-28.log"), "19:00:00|termine|a|trop tot\n21:30:00|termine|b|hier soir\n");
            File.WriteAllText(Path.Combine(jdir, "2026-10-29.log"), "03:00:00|info2|paperclip|Agent en erreur\n11:00:00|termine|c|trop tard\n");
            var ev3 = Journal.Between(hier.AddHours(20), new DateTime(2026, 10, 29, 10, 0, 0));
            Check("minuit franchi, bornes respectees", ev3.Count == 2 && ev3[0].Item4 == "hier soir" && ev3[1].Item3 == "paperclip");
            Check("jour sans fichier : rien, sans erreur", Journal.Between(new DateTime(2026, 10, 20), new DateTime(2026, 10, 21)).Count == 0);
        }
        finally { try { Directory.Delete(Program.Root, true); } catch { } }

        Console.WriteLine(_fail == 0 ? "Tous les tests passent." : _fail + " test(s) en échec.");
        return _fail == 0 ? 0 : 1;
    }
}
