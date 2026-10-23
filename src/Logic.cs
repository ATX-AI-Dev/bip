// Logique pure partagee : ni interface, ni fichier, ni appel systeme. Testee par tests\run.cmd.

using System;
using System.Collections.Generic;
using System.Linq;

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
    }
}
