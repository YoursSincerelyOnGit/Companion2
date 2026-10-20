using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Companion;

public partial class MainWindow : Window
{
    private enum State
    {
        Idle,
        Walking,
        Sitting,
        Celebrating,
        Hiding,     // Off-screen
        Peeking,    // Edge pop-up
        Intro,      // Superhero land
        Chasing     // Irritated
    }

    private sealed record SpriteDef(BitmapImage Image, double Height);

    // ---- Tuning knobs (device-independent pixels) --------------------------
    private const double WalkSpeed = 130;
    private const double ChaseSpeed = 350;       // Faster when irritated
    private const double FollowDistance = 240;
    private const double Slack = 30;
    private const double SitAfterSeconds = 4;
    private const double CelebrateSeconds = 2.5;
    // ------------------------------------------------------------------------

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, SpriteDef> _sprites = new();
    private string _currentSprite = "";

    private double _lastTime;
    private double _animTime;

    private State _state = State.Idle;
    private int _facing = 1;
    private bool _following = true;
    private bool _dragging;

    private Point _lastCursor;
    private double _lastCursorMoveTime;
    private double _celebrateUntil;
    private string _celebrateSprite = "thumbs";
    private int _celebrateCount;

    // New state variables
    private bool _isIntroPlayed = false;
    private DateTime _hideStartTime = DateTime.Now;
    private double _introFallVelocity = 0;
    private readonly Random _rand = new Random();

    public MainWindow()
    {
        InitializeComponent();

        // Load all the new sprites
        _sprites["body"] = new SpriteDef(LoadSprite("body.png"), 240);
        _sprites["thumbs"] = new SpriteDef(LoadSprite("thumbs.png"), 220);
        _sprites["flex"] = new SpriteDef(LoadSprite("flex.png"), 140);
        _sprites["walk"] = new SpriteDef(LoadSprite("walk.png"), 240);
        _sprites["sprint"] = new SpriteDef(LoadSprite("sprint.png"), 240);
        _sprites["peek"] = new SpriteDef(LoadSprite("peek.png"), 240);
        _sprites["landing"] = new SpriteDef(LoadSprite("landing.png"), 240);

        SetSprite("body");

        // Start near the bottom-right of the primary screen's work area.
        Left = SystemParameters.WorkArea.Right - Width - 60;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
    }

    private static BitmapImage LoadSprite(string file)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri($"pack://application:,,,/Assets/{file}", UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void SetSprite(string key)
    {
        if (_currentSprite == key) return;
        var def = _sprites[key];
        Sprite.Source = def.Image;
        Sprite.Height = def.Height;
        _currentSprite = key;
    }

    private double GetScreenEdge(bool leftEdge)
    {
        if (leftEdge) return SystemParameters.VirtualScreenLeft - (Width * 0.8); // Hide most of him
        return SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - (Width * 0.2);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.HideFromAltTab(hwnd);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _lastTime = _clock.Elapsed.TotalSeconds;
        _lastCursor = GetCursorDip();
        _lastCursorMoveTime = _lastTime;

        CompositionTarget.Rendering += OnRendering;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    // ---- Game loop ---------------------------------------------------------

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTime, 0.1);
        _lastTime = now;
        _animTime += dt;

        if (_dragging) return;

        // --- 1. Superhero Intro ---
        if (!_isIntroPlayed)
        {
            if (_state != State.Intro)
            {
                _state = State.Intro;
                Top = SystemParameters.VirtualScreenTop - Height; // Start above screen
                Left = SystemParameters.WorkArea.Right - Width - 100;
                _introFallVelocity = 0;
            }

            _introFallVelocity += 2500 * dt; // Gravity
            Top += _introFallVelocity * dt;

            if (Top >= SystemParameters.WorkArea.Bottom - Height - 20)
            {
                Top = SystemParameters.WorkArea.Bottom - Height - 20; // Hit the floor
                _isIntroPlayed = true;
                _state = State.Idle;
                _lastCursorMoveTime = now; // Give a brief pause before he starts walking
            }
            ApplyPose();
            return;
        }

        // --- 2. Celebration Override ---
        if (_state == State.Celebrating)
        {
            if (now >= _celebrateUntil)
            {
                _state = State.Idle;
                _lastCursor = GetCursorDip();
                _lastCursorMoveTime = now;
            }
            ApplyPose();
            return;
        }

        // Get Cursor Data
        Point cursor = GetCursorDip();
        double cursorDist = (cursor - _lastCursor).Length;
        if (cursorDist > 4)
        {
            _lastCursor = cursor;
            _lastCursorMoveTime = now;
        }

        Point me = new Point(Left + Width / 2, Top + Height / 2);
        Vector toCursor = cursor - me;
        double dist = toCursor.Length;
        bool cursorStill = now - _lastCursorMoveTime > SitAfterSeconds;

        // --- 3. Update State Logic ---
        UpdateState(dist, cursorStill, cursorDist);

        // --- 4. Movement Logic ---
        if ((_state == State.Walking || _state == State.Chasing) && dist > 0.001)
        {
            Vector dir = toCursor / dist;

            // He moves faster if he's chasing
            double currentSpeed = (_state == State.Chasing) ? ChaseSpeed : WalkSpeed;

            double step = Math.Min(currentSpeed * dt, dist - FollowDistance);
            if (step > 0)
            {
                MoveTo(me + dir * step);
            }
            if (Math.Abs(dir.X) > 0.2)
            {
                _facing = dir.X > 0 ? 1 : -1;
            }
        }

        ApplyPose();
    }

    private void UpdateState(double distToCursor, bool cursorStill, double cursorSpeed)
    {
        if (!_following)
        {
            _state = State.Idle;
            return;
        }

        // Hiding Logic
        if (_state == State.Hiding)
        {
            if ((DateTime.Now - _hideStartTime).TotalSeconds > _rand.Next(4, 8))
            {
                _state = State.Peeking;
                Point cursor = GetCursorDip();
                bool leftEdge = cursor.X < SystemParameters.PrimaryScreenWidth / 2;

                Top = Math.Clamp(cursor.Y - Height / 2, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
                Left = GetScreenEdge(leftEdge);
            }
            return;
        }

        if (_state == State.Peeking)
        {
            // Peek for 2 seconds before coming back out (using the app's Stopwatch clock)
            if (_clock.Elapsed.TotalSeconds - _lastCursorMoveTime > 2)
            {
                _state = State.Idle;
            }
            return;
        }

        bool tooFar = distToCursor > FollowDistance + Slack;

        switch (_state)
        {
            case State.Walking:
            case State.Chasing:
                if (distToCursor <= FollowDistance) _state = State.Idle;
                break;

            case State.Idle:
                if (tooFar)
                {
                    // If the cursor is moving super fast, he gets irritated and chases
                    _state = cursorSpeed > 30 ? State.Chasing : State.Walking;
                }
                else if (cursorStill) _state = State.Sitting;
                break;

            case State.Sitting:
                if (tooFar) _state = State.Walking;
                else if (!cursorStill) _state = State.Idle;
                break;
        }

        // Random Hiding Chance (1 in 200 chance per frame while walking)
        if (_state == State.Walking && _rand.Next(0, 200) == 0)
        {
            _state = State.Hiding;
            _hideStartTime = DateTime.Now;

            bool hideLeft = Left < SystemParameters.PrimaryScreenWidth / 2;
            Left = hideLeft ? SystemParameters.VirtualScreenLeft - Width : SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        }
    }

    private void ApplyPose()
    {
        string sprite = "body";
        double bob = 0, squash = 1, lean = 0, flip = _facing;

        switch (_state)
        {
            case State.Intro:
                sprite = "landing";
                flip = 1;
                break;

            case State.Walking:
                sprite = "walk";
                bob = -Math.Abs(Math.Sin(_animTime * 12)) * 6;
                lean = Math.Sin(_animTime * 12) * 4;
                break;

            case State.Chasing:
                sprite = "sprint";
                bob = -Math.Abs(Math.Sin(_animTime * 20)) * 8;
                lean = 8; // Leans forward aggressively
                break;

            case State.Hiding:
                Sprite.Opacity = 0; // Invisible while off-screen
                break;

            case State.Peeking:
                Sprite.Opacity = 1;
                sprite = "peek";
                // Face toward the center of the screen if on an edge
                flip = (Left < SystemParameters.PrimaryScreenWidth / 2) ? 1 : -1;
                break;

            case State.Idle:
                Sprite.Opacity = 1;
                sprite = "body";
                squash = 1 + Math.Sin(_animTime * 2) * 0.012;
                break;

            case State.Sitting:
                Sprite.Opacity = 1;
                sprite = "body";
                squash = 0.86;
                break;

            case State.Celebrating:
                Sprite.Opacity = 1;
                sprite = _celebrateSprite;
                flip = 1;
                bob = -Math.Abs(Math.Sin(_animTime * 8)) * 10;
                break;
        }

        SetSprite(sprite);
        Shadow.Opacity = (_state == State.Celebrating || _state == State.Intro || _state == State.Hiding) ? 0 : 1;

        Pose.ScaleX = flip;
        Pose.ScaleY = squash;
        Lean.Angle = lean;
        Shift.Y = bob;
    }

    private void StartCelebrate()
    {
        _state = State.Celebrating;
        _celebrateUntil = _clock.Elapsed.TotalSeconds + CelebrateSeconds;
        _celebrateSprite = (_celebrateCount++ % 2 == 0) ? "thumbs" : "flex";
    }

    // ---- Helpers -----------------------------------------------------------

    private void MoveTo(Point center)
    {
        double minX = SystemParameters.VirtualScreenLeft;
        double minY = SystemParameters.VirtualScreenTop;
        double maxX = minX + SystemParameters.VirtualScreenWidth - Width;
        double maxY = minY + SystemParameters.VirtualScreenHeight - Height;

        Left = Math.Clamp(center.X - Width / 2, minX, maxX);
        Top = Math.Clamp(center.Y - Height / 2, minY, maxY);
    }

    private Point GetCursorDip()
    {
        Native.GetCursorPos(out var p);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
        }
        return new Point(p.X, p.Y);
    }

    // ---- Input -------------------------------------------------------------

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }

        _dragging = false;
        _state = State.Idle;
        _lastCursorMoveTime = _clock.Elapsed.TotalSeconds;
    }

    private void FollowItem_Click(object sender, RoutedEventArgs e)
    {
        _following = FollowItem.IsChecked;
    }

    private void Celebrate_Click(object sender, RoutedEventArgs e)
    {
        StartCelebrate();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}