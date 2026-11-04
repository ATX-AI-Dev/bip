// Rinel, la mascotte de ClaudeNotch (kit design/rinel-mascotte, tokens.json) : Herve de Rinel,
// chevalier de la Table Ronde et cartographe, tout rond, casque a plume, parchemin sous le bras.
// La classe s'appelle encore Bip dans le code (nom technique conserve).
// Portage en code du BipControl livre en XAML : le projet se compile sans XAML,
// avec le csc du .NET Framework (C# 5). Geometrie, couleurs, mouvements et durees
// sont ceux du kit, a l'identique.
//
// Grille 32 x 32 mise a l'echelle par le Viewbox : poser le controle a 16 px
// (encoche repliee), 20 px (lignes) ou 40 px (cartes, forme entrouverte).
// A 16 px, 2 unites = 1 px.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeNotch
{
    // Neutral : hors kit, Bip seul dans l'encoche vide, qui parle a l'utilisateur.
    enum BipState { Waiting, Running, Ready, Error, Idle, Neutral }

    class Bip : Viewbox
    {
        static readonly Dictionary<BipState, Color> StateColors = new Dictionary<BipState, Color>
        {
            { BipState.Waiting, Hex(0xFF5D52) },
            { BipState.Running, Hex(0xF5A524) },
            { BipState.Ready,   Hex(0x3FD07F) },
            { BipState.Error,   Hex(0x8E96A3) },
            { BipState.Idle,    Hex(0x4A4A4E) },
            { BipState.Neutral, Hex(0xDAD6CF) },   // blanc casse
        };
        static readonly Brush Face = Frozen(Hex(0x141414)), FaceIdle = Frozen(Hex(0x161616)), BadgeFill = Frozen(Hex(0xFFC93D));
        static readonly Brush Parch = Frozen(Hex(0xF1E6C8)), ParchEnd = Frozen(Hex(0xCDBB8A));
        static readonly Brush Helmet = Frozen(Color.FromArgb(0x4D, 255, 255, 255)), Brim = Frozen(Color.FromArgb(0x6B, 0, 0, 0)), Vein = Frozen(Color.FromArgb(0x40, 0, 0, 0));
        static readonly Random Rng = new Random();

        readonly SolidColorBrush _brush = new SolidColorBrush(StateColors[BipState.Idle]);
        readonly ScaleTransform _scale = new ScaleTransform();
        readonly RotateTransform _tilt = new RotateTransform();
        readonly TranslateTransform _shift = new TranslateTransform();
        readonly ScaleTransform _plumeScale = new ScaleTransform(1, 1, 16, 10);
        readonly RotateTransform _plumeRotate = new RotateTransform(0, 16, 10);
        readonly ScaleTransform _badgeScale = new ScaleTransform(0, 0, 26, 7.5);
        readonly RotateTransform _armRotate = new RotateTransform(0, 23.5, 20);
        readonly Canvas _plumeUp, _plumeLaid, _plumeDrooped, _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle, _badge;
        readonly DispatcherTimer _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        BipState _state = BipState.Idle;
        DateTime _waitStart, _lastTremble;

        // Debut de l'attente si la session attendait deja avant la creation du controle :
        // le rythme d'impatience repart alors du bon niveau.
        public DateTime? WaitingSince;

        // Images par seconde des animations en boucle. L'encoche repliee (Bip de 16 px) passe a 8.
        public int Fps = 15;

        public BipState State
        {
            get { return _state; }
            set
            {
                if (value == _state) return;
                var from = _state;
                _state = value;
                if (IsLoaded) Apply(from, value, true);
            }
        }

        public Bip()
        {
            Stretch = Stretch.Uniform;
            UseLayoutRounding = true;

            var root = new Canvas { Width = 32, Height = 32, RenderTransformOrigin = new Point(0.5, 0.875) };
            var tg = new TransformGroup();
            tg.Children.Add(_scale);
            tg.Children.Add(_tilt);
            tg.Children.Add(_shift);
            root.RenderTransform = tg;
            Child = root;

            // Plume (casque) : dressee (tous les etats sauf repos et erreur), couchee (repos), tombante (erreur)
            _plumeUp = new Canvas();
            _plumeUp.Children.Add(Shape("M12.2,10.6 C11,5.4 14.6,1.4 21.2,1.6 C19,3.6 18.4,6.6 19.8,10.6 Z", _brush));
            _plumeUp.Children.Add(Line("M14,9.6 C14.2,6.6 16,4.4 18.6,3.2", 0.8, Vein));
            var pg = new TransformGroup();
            pg.Children.Add(_plumeScale);
            pg.Children.Add(_plumeRotate);
            _plumeUp.RenderTransform = pg;
            root.Children.Add(_plumeUp);
            _plumeLaid = new Canvas { Opacity = 0 };
            _plumeLaid.Children.Add(Shape("M11.6,10.8 C13,8 18,7.4 22.4,9.8 C19,9.6 16,10 13.6,11.2 Z", _brush));
            root.Children.Add(_plumeLaid);
            _plumeDrooped = new Canvas { Opacity = 0 };
            _plumeDrooped.Children.Add(Shape("M12.6,10.8 C10,9 7.4,9.4 5.2,12.6 C8.4,11.8 10.6,12.2 13.8,11.4 Z", _brush));
            root.Children.Add(_plumeDrooped);

            // Bras et parchemin du cartographe (derriere le corps)
            var armGroup = new Canvas { RenderTransform = _armRotate };
            armGroup.Children.Add(Line("M23.5,20 L27.6,15.6", 2.8, _brush));
            var scroll = new Canvas { RenderTransform = new RotateTransform(-35, 28.6, 14.4) };
            scroll.Children.Add(Rect(25.4, 12.9, 6.4, 3, 1.5, Parch));
            scroll.Children.Add(Ell(24.4, 12.9, 3, 3, ParchEnd));
            scroll.Children.Add(Ell(29.8, 12.9, 3, 3, ParchEnd));
            armGroup.Children.Add(scroll);
            root.Children.Add(armGroup);

            // Pieds, corps rond, casque (calotte claire + bandeau sombre)
            root.Children.Add(Rect(10, 26.4, 4.2, 3.6, 1.3, _brush));
            root.Children.Add(Rect(17.8, 26.4, 4.2, 3.6, 1.3, _brush));
            root.Children.Add(Ell(7, 10, 18, 18, _brush));
            root.Children.Add(Shape("M7.2,17 A9,9 0 0 1 24.8,17 Z", Helmet));
            root.Children.Add(Rect(6.6, 16.2, 18.8, 1.9, 0.95, Brim));

            // Visages : un seul visible, fondu croise de 150 ms
            _faceRunning = Group(Line("M10.6,20.6 H14.2", 2, Face), Line("M17.8,20.6 H21.4", 2, Face), Line("M14.6,24.6 H17.4", 1.6, Face));
            _faceWaiting = Group(Ell(10.5, 18.5, 3.8, 3.8, Face), Ell(17.7, 18.5, 3.8, 3.8, Face), Ell(14.3, 23, 3.4, 4, Face));
            _faceReady = Group(Line("M10.6,21 Q12.4,18.4 14.2,21", 2.1, Face), Line("M17.8,21 Q19.6,18.4 21.4,21", 2.1, Face),
                               Line("M13.6,23.6 Q16,26.4 18.4,23.6", 1.7, Face));
            _faceError = Group(Ell(11.5, 20.1, 2.2, 2.2, Face), Ell(18.3, 20.1, 2.2, 2.2, Face), Line("M14.4,25.2 Q16,24 17.6,25.2", 1.5, Face));
            _faceIdle = Group(Line("M10.6,20.6 Q12.4,22.2 14.2,20.6", 1.8, FaceIdle), Line("M17.8,20.6 Q19.6,22.2 21.4,20.6", 1.8, FaceIdle));
            foreach (var f in new[] { _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle }) root.Children.Add(f);

            // Pastille "!" (erreur)
            _badge = Group(Ell(21.8, 3.3, 8.4, 8.4, BadgeFill), Rect(25.3, 4.8, 1.4, 3.6, 0.7, Face), Ell(25.15, 9.25, 1.7, 1.7, Face));
            _badge.RenderTransform = _badgeScale;
            root.Children.Add(_badge);

            _waitTimer.Tick += OnWaitTick;
            Loaded += (s, e) => Apply(_state, _state, false);
            Unloaded += (s, e) => StopLoops();
        }

        void Apply(BipState from, BipState to, bool animate)
        {
            StopLoops();

            _brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(StateColors[to], Ms(animate ? 250 : 0)));

            int face = animate ? 150 : 0;
            Fade(_faceWaiting, to == BipState.Waiting ? 1 : 0, face);
            Fade(_faceRunning, to == BipState.Running ? 1 : 0, face);
            Fade(_faceReady, to == BipState.Ready || to == BipState.Neutral ? 1 : 0, face);
            Fade(_faceError, to == BipState.Error ? 1 : 0, face);
            Fade(_faceIdle, to == BipState.Idle ? 1 : 0, face);

            Fade(_plumeUp, to == BipState.Idle || to == BipState.Error ? 0 : 1, animate ? 400 : 0);
            Fade(_plumeLaid, to == BipState.Idle ? 1 : 0, animate ? 400 : 0);
            Fade(_plumeDrooped, to == BipState.Error ? 1 : 0, animate ? 400 : 0);
            var plumeAnim = new DoubleAnimation(to == BipState.Waiting ? 1.2 : 1.0, Ms(animate ? 200 : 0))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            _plumeScale.BeginAnimation(ScaleTransform.ScaleXProperty, plumeAnim);
            _plumeScale.BeginAnimation(ScaleTransform.ScaleYProperty, plumeAnim);

            // Position de repos du bras (le parchemin) selon l'etat
            double rest = to == BipState.Running ? 10 : to == BipState.Ready ? -14 : to == BipState.Error ? 60 : to == BipState.Idle ? 55 : 0;
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _armRotate.Angle = rest;

            Fade(_badge, to == BipState.Error ? 1 : 0, animate ? 150 : 0);
            var badgeAnim = new DoubleAnimation(to == BipState.Error ? 1 : 0, Ms(animate ? 200 : 0))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            _badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, badgeAnim);
            _badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, badgeAnim);

            switch (to)
            {
                case BipState.Waiting:
                    _waitStart = WaitingSince ?? DateTime.Now;
                    _lastTremble = DateTime.Now;
                    BreatheCycle();
                    _waitTimer.Start();
                    break;
                case BipState.Running:
                    Float();
                    Sway(1600, 4, -6, -6, 8);
                    break;
                case BipState.Neutral:
                    Float();
                    Sway(1600, 0, 0, -5, 7);
                    Hello();
                    break;
                case BipState.Ready:
                    if (animate && from != BipState.Ready) Hop();
                    break;
                case BipState.Error:
                    // Affaissement X 1,04 / Y 0,94 en 250 ms, puis fige
                    var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                    _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    break;
            }
        }

        void StopLoops()
        {
            _waitTimer.Stop();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            _shift.BeginAnimation(TranslateTransform.YProperty, null);
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _plumeRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // Bras (parchemin) et plume qui oscillent doucement, en degres.
        void Sway(double halfMs, double armFrom, double armTo, double plumeFrom, double plumeTo)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            if (armFrom != armTo)
                _armRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(armFrom, armTo, Ms(halfMs))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease }, Fps));
            _plumeRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(plumeFrom, plumeTo, Ms(halfMs * 2))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease }, Fps));
        }

        // Coucou de l'encoche vide : deux signes du bras, une pause (cycle 3,2 s).
        void Hello()
        {
            var k = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = Ms(3200) };
            int[] t = { 0, 256, 576, 896, 1216, 1472, 3200 };
            double[] v = { 0, -16, 12, -16, 12, 0, 0 };
            for (int n = 0; n < t.Length; n++) k.KeyFrames.Add(new LinearDoubleKeyFrame(v[n], Kt(t[n])));
            Timeline.SetDesiredFrameRate(k, Fps);
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, k);
        }

        // ---- En attente : respiration qui accelere, bras qui s'agite, tremblement

        // 1,6 s, puis 1,1 s apres 1 min, 0,8 s apres 3 min.
        double BreathePeriodMs()
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            return waited < 60 ? 1600 : waited < 180 ? 1100 : 800;
        }

        // Un cycle a la fois : le rythme ne change qu'en fin de cycle, sans a-coup.
        void BreatheCycle()
        {
            if (_state != BipState.Waiting || !IsLoaded) return;
            var half = Ms(BreathePeriodMs() / 2);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var sy = Loop(new DoubleAnimation(1, 1.08, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            var sx = Loop(new DoubleAnimation(1, 1.04, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            var arm = Loop(new DoubleAnimation(-10, 12, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            sy.Completed += (s, e) => BreatheCycle();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, arm);
            _plumeRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(-8, 10, half) { AutoReverse = true, EasingFunction = ease }, Fps));
        }

        void OnWaitTick(object sender, EventArgs e)
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            if ((DateTime.Now - _lastTremble).TotalSeconds < (waited >= 180 ? 4 : 6)) return;
            _lastTremble = DateTime.Now;
            // -4 / +4 / -3 / 0 degres en 300 ms
            var k = new DoubleAnimationUsingKeyFrames { Duration = Ms(300) };
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-4, Kt(60)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(4, Kt(150)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-3, Kt(225)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, Kt(300)));
            _tilt.BeginAnimation(RotateTransform.AngleProperty, k);
        }

        // ---- En cours : flottement Y 0 -> -2 u et rotation +-2 deg, cycle 3,2 s,
        // depart decale de 0 a 1 s pour que plusieurs Bip ne flottent pas en phase.
        void Float()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var delay = TimeSpan.FromMilliseconds(Rng.Next(0, 1000));
            _shift.BeginAnimation(TranslateTransform.YProperty, Loop(new DoubleAnimation(0, -2, Ms(1600))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }, Fps));
            _tilt.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(-2, 2, Ms(1600))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }, Fps));
        }

        // ---- Pret : un saut, une seule fois (tassement, montee -6 u, reception, repos)
        void Hop()
        {
            var o = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var i = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 1, 1.06, 0.96, 1.08, 1 }, o, i));
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 1, 0.90, 1.05, 0.90, 1 }, o, i));
            _shift.BeginAnimation(TranslateTransform.YProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 0, 0, -6.0, 0, 0 }, o, i));
        }

        // ---- Reaction au clic : un saut, un clignement des yeux ou un petit signe.
        public void Poke()
        {
            switch (Rng.Next(3))
            {
                case 0:
                    Hop();
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
                    t.Tick += (s, e) => { t.Stop(); RestoreLoops(); };
                    t.Start();
                    break;
                case 1: Blink(); break;
                default: Wave(); break;
            }
        }

        // Yeux fermes 120 ms (le visage endormi), puis rouverts.
        void Blink()
        {
            if (_state == BipState.Idle) return;
            UIElement face = _state == BipState.Waiting ? _faceWaiting : _state == BipState.Running ? _faceRunning
                           : _state == BipState.Error ? _faceError : _faceReady;
            face.BeginAnimation(OpacityProperty, Steps(new[] { 0, 60, 180, 240 }, new[] { 1.0, 0, 0, 1 }));
            _faceIdle.BeginAnimation(OpacityProperty, Steps(new[] { 0, 60, 180, 240 }, new[] { 0.0, 1, 1, 0 }));
        }

        // Le bras s'agite deux fois, puis les mouvements de l'etat reprennent. Seulement quand le bras
        // est dresse : au repos et en erreur il est rabattu, il ne saute pas.
        void Wave()
        {
            if (_state == BipState.Waiting || _state == BipState.Neutral || _state == BipState.Idle || _state == BipState.Error) return;
            var wave = new DoubleAnimation(-10, 12, Ms(180))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromMilliseconds(100), FillBehavior = FillBehavior.Stop };
            wave.Completed += (s, e) => RestoreLoops();
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, wave);
        }

        // Relance les boucles de l'etat courant (flottement, oscillations) apres un saut ou un signe.
        void RestoreLoops()
        {
            if (!IsLoaded) return;
            if (_state == BipState.Running) { Float(); Sway(1600, 4, -6, -6, 8); }
            else if (_state == BipState.Neutral) { Float(); Sway(1600, 0, 0, -5, 7); Hello(); }
        }

        static DoubleAnimationUsingKeyFrames Steps(int[] ms, double[] v)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            for (int n = 0; n < ms.Length; n++) a.KeyFrames.Add(new LinearDoubleKeyFrame(v[n], Kt(ms[n])));
            return a;
        }

        // Trames du saut : la 3e image monte (ease out), la 4e redescend (ease in).
        static DoubleAnimationUsingKeyFrames Keys(int[] ms, double[] v, IEasingFunction up, IEasingFunction down)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            for (int n = 0; n < ms.Length; n++)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(v[n], Kt(ms[n]), n == 2 ? up : n == 3 ? down : null));
            return a;
        }

        // ---- Utilitaires de dessin

        static Rectangle Rect(double x, double y, double w, double h, double r, Brush fill)
        {
            var e = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill };
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            return e;
        }

        static Ellipse Ell(double x, double y, double w, double h, Brush fill)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            return e;
        }

        static Path Line(string data, double thickness, Brush stroke)
        {
            return new Path
            {
                Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
        }

        static Path Shape(string data, Brush fill)
        {
            return new Path { Data = Geometry.Parse(data), Fill = fill };
        }

        static Canvas Group(params UIElement[] children)
        {
            var c = new Canvas { Opacity = 0 };
            foreach (var ch in children) c.Children.Add(ch);
            return c;
        }

        static void Fade(UIElement el, double to, int ms)
        {
            if (ms <= 0) { el.BeginAnimation(OpacityProperty, null); el.Opacity = to; return; }
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Ms(ms)));
        }

        // Animations en boucle, lentes et de quelques pixels : pas besoin de 60 images/s.
        // Bip : 15, et 8 dans l'encoche repliee (son flottement y fait 1 px) ; l'eclat de l'encoche : 30, sinon
        // son balayage saccade. Les transitions courtes restent a 60.
        public static DoubleAnimation Loop(DoubleAnimation a, int fps = 15) { Timeline.SetDesiredFrameRate(a, fps); return a; }

        static Duration Ms(double ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }
        static KeyTime Kt(int ms) { return KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)); }
        static Color Hex(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
