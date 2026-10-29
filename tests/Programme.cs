// Program factice pour les tests : Model.cs (journal du jour, capture rapide) lit Program.Root, defini en vrai dans
// App.cs avec l'interface WPF. Ici, un dossier temporaire propre a chaque lancement des tests.

using System;
using System.IO;

namespace ClaudeNotch
{
    static class Program
    {
        public static readonly string Root = Path.Combine(Path.GetTempPath(), "bip-tests-programme-" + Guid.NewGuid().ToString("N"));
        public static string StateDir { get { return Path.Combine(Root, "state"); } }
        public static void Log(string msg) { }
    }
}
