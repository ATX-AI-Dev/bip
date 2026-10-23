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

        Console.WriteLine(_fail == 0 ? "Tous les tests passent." : _fail + " test(s) en échec.");
        return _fail == 0 ? 0 : 1;
    }
}
