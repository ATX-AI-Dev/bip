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
            GlobalSystemMediaTransportControlsSessionManager mgr;
            try { mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); }
            catch (Exception ex) { _failed = true; log("musique : sessions media indisponibles (" + ex.Message + ")"); return; }
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
            var status = s.GetPlaybackInfo().PlaybackStatus;
            if (string.IsNullOrEmpty(p.Title) && status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return null;
            return new NowPlaying
            {
                App = Logic.MediaAppName(s.SourceAppUserModelId),
                Title = Logic.Visible(p.Title ?? ""),
                Artist = Logic.Visible(p.Artist ?? ""),
                Playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            };
        }

        static void Order(Action<GlobalSystemMediaTransportControlsSessionManager> a)
        {
            if (!Available) return;
            lock (_lock) _orders.Enqueue(a);
            _wake.Set();
        }

        public static void TogglePlayPause() { Order(m => { var s = m.GetCurrentSession(); if (s != null) Wait(s.TryTogglePlayPauseAsync()); }); }
        public static void Next() { Order(m => { var s = m.GetCurrentSession(); if (s != null) Wait(s.TrySkipNextAsync()); }); }

        // Pause pour un appel : seulement si quelque chose joue ; "done" recoit vrai si la pause a ete faite.
        public static void PauseIfPlaying(Action<bool> done)
        {
            Order(m =>
            {
                var s = m.GetCurrentSession();
                bool paused = s != null && s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                              && Wait(s.TryPauseAsync());
                done(paused);
            });
        }

        // Reprise apres l'appel : seulement si le meme morceau (titre) est toujours en pause. Un autre morceau ou une autre
        // application entre-temps : rien n'est relance.
        public static void ResumeIfPaused(string title)
        {
            Order(m =>
            {
                var s = m.GetCurrentSession();
                if (s == null || s.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) return;
                if (Logic.Visible(Wait(s.TryGetMediaPropertiesAsync()).Title ?? "") != (title ?? "")) return;
                Wait(s.TryPlayAsync());
            });
        }
    }
}
