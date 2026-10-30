// Musique en cours (phase 3) : les sessions media de Windows (GlobalSystemMediaTransportControlsSessionManager), qui
// couvrent Spotify, Deezer, YouTube ou SoundCloud dans le navigateur, sans compte ni acces reseau. Verifie le 30/10/2026
// sur le poste avec une vraie video YouTube dans Chrome (un Chrome cache hors ecran ne s'annonce pas).
//
// Compile par le csc du .NET Framework avec les metadonnees WinRT du systeme (C:\Windows\System32\WinMetadata) : sans
// SDK, "await" sur une operation WinRT n'est pas disponible, on attend l'operation directement. Tout se passe sur un fil
// a part : l'interface ne bloque jamais. Une panne (Windows trop ancien, API refusee) desactive la fonction, rien d'autre.

using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;

namespace ClaudeNotch
{
    // Ce qui joue, a l'instant du releve. Null : rien.
    class NowPlaying
    {
        public string App, Title, Artist;
        public bool Playing;

        public string Key { get { return App + "\u0001" + Title + "\u0001" + Artist + "\u0001" + Playing; } }
    }

    static class Media
    {
        static readonly object _lock = new object();
        static readonly Queue<Action<GlobalSystemMediaTransportControlsSessionManager>> _orders = new Queue<Action<GlobalSystemMediaTransportControlsSessionManager>>();
        static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        static Action<NowPlaying> _onChange;
        static bool _started, _failed;

        public static bool Available { get { return _started && !_failed; } }

        static T Wait<T>(IAsyncOperation<T> op)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (op.Status == AsyncStatus.Started && DateTime.UtcNow < deadline) Thread.Sleep(10);
            if (op.Status != AsyncStatus.Completed) throw new Exception("operation WinRT : " + op.Status);
            return op.GetResults();
        }

        // Releve toutes les 2 s sur un fil a part ; onChange recoit le nouvel etat (null : rien ne joue) a chaque changement.
        public static void Start(Action<NowPlaying> onChange, Action<string> log)
        {
            if (_started) return;
            _started = true;
            _onChange = onChange;
            // Le try englobe aussi la compilation de Loop : des types WinRT introuvables leveraient a cet endroit, sur un fil
            // d'arriere-plan, et une exception non rattrapee y arreterait toute l'encoche.
            var t = new Thread(() =>
            {
                try { Loop(log); }
                catch (Exception ex) { _failed = true; log("musique : arrêt du relevé (" + ex.Message + ")"); }
            }) { IsBackground = true, Name = "media" };
            t.Start();
        }

        static void Loop(Action<string> log)
        {
            // Le service peut etre lent juste apres l'ouverture de session : cinq essais espaces de 10 s avant d'abandonner.
            GlobalSystemMediaTransportControlsSessionManager mgr = null;
            for (int i = 0; mgr == null; i++)
            {
                try { mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); }
                catch (Exception ex)
                {
                    if (i >= 4) { _failed = true; log("musique : sessions media indisponibles (" + ex.Message + ")"); return; }
                    Thread.Sleep(10000);
                }
            }
            string last = null;
            while (true)
            {
                // Ordres (lecture, pause, suivant) d'abord : la reponse se voit au releve qui suit.
                while (true)
                {
                    Action<GlobalSystemMediaTransportControlsSessionManager> order = null;
                    lock (_lock) if (_orders.Count > 0) order = _orders.Dequeue();
                    if (order == null) break;
                    try { order(mgr); } catch (Exception ex) { log("musique : " + ex.Message); }
                }
                NowPlaying now = null;
                try { now = Read(mgr); } catch { }
                var key = now == null ? "" : now.Key;
                if (key != last) { last = key; try { _onChange(now); } catch { } }
                _wake.WaitOne(2000);
            }
        }

        static NowPlaying Read(GlobalSystemMediaTransportControlsSessionManager mgr)
        {
            var s = mgr.GetCurrentSession();
            if (s == null) return null;
            var p = Wait(s.TryGetMediaPropertiesAsync());
            var status = Status(s);
            if (string.IsNullOrEmpty(p.Title) && status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return null;
            return new NowPlaying
            {
                App = Logic.MediaAppName(s.SourceAppUserModelId),
                Title = Logic.Visible(p.Title ?? ""),
                Artist = Logic.Visible(p.Artist ?? ""),
                Playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            };
        }

        // Etat de lecture ; une session en cours de fermeture peut ne plus en donner.
        static GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status(GlobalSystemMediaTransportControlsSession s)
        {
            var info = s.GetPlaybackInfo();
            return info == null ? (GlobalSystemMediaTransportControlsSessionPlaybackStatus?)null : info.PlaybackStatus;
        }

        static void Order(Action<GlobalSystemMediaTransportControlsSessionManager> a)
        {
            if (!Available) return;
            lock (_lock) _orders.Enqueue(a);
            _wake.Set();
        }

        public static void TogglePlayPause() { Order(m => { var s = m.GetCurrentSession(); if (s != null) Wait(s.TryTogglePlayPauseAsync()); }); }
        public static void Next() { Order(m => { var s = m.GetCurrentSession(); if (s != null) Wait(s.TrySkipNextAsync()); }); }

        // Pause pour un appel : seulement si quelque chose joue. "done" recoit le titre mis en pause, lu sur la session
        // elle-meme (le releve a 2 s peut avoir un temps de retard), ou null si rien n'a ete mis en pause. Toujours appele.
        public static void PauseIfPlaying(Action<string> done)
        {
            Order(m =>
            {
                string paused = null;
                try
                {
                    var s = m.GetCurrentSession();
                    if (s != null && Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        var title = Logic.Visible(Wait(s.TryGetMediaPropertiesAsync()).Title ?? "");
                        if (Wait(s.TryPauseAsync())) paused = title;
                    }
                }
                finally { done(paused); }
            });
        }

        // Reprise apres l'appel : seulement si le meme morceau (titre) est toujours en pause. Un autre morceau ou une autre
        // application entre-temps : rien n'est relance.
        public static void ResumeIfPaused(string title)
        {
            Order(m =>
            {
                var s = m.GetCurrentSession();
                if (s == null || Status(s) != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) return;
                if (Logic.Visible(Wait(s.TryGetMediaPropertiesAsync()).Title ?? "") != (title ?? "")) return;
                Wait(s.TryPlayAsync());
            });
        }
    }
}
