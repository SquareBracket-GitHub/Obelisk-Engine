using System.Numerics;

namespace Pong;

internal enum MatchState { Title, Playing, GameOver }
internal enum MatchSound { Hit, Score, Win }

/// <summary>Fixed logical court; small steps prevent missed paddle collisions.</summary>
internal sealed class PongMatch
{
    public const int WinningScore = 5;
    public const float PaddleHeight = 96;
    public const float Radius = 9;
    public MatchState State { get; private set; }
    public int LeftScore { get; private set; }
    public int RightScore { get; private set; }
    public float LeftY { get; private set; } = 222;
    public float RightY { get; private set; } = 222;
    public Vector2 Ball { get; private set; } = new(480, 270);
    public Vector2 Velocity { get; private set; }
    public event Action<MatchSound>? Sound;
    private float _serveDelay;

    public void Start()
    {
        LeftScore = RightScore = 0;
        LeftY = RightY = 222;
        State = MatchState.Playing;
        Serve(1);
    }
    private void Serve(int direction)
    {
        Ball = new(480, 270);
        Velocity = new(340 * direction, 130);
        _serveDelay = 0.75f;
    }
    public void Update(float delta, float leftDirection, float rightDirection)
    {
        if (!float.IsFinite(delta) || delta < 0 || delta > 0.1f) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!float.IsFinite(leftDirection) || !float.IsFinite(rightDirection)) throw new ArgumentException("Paddle input must be finite.");
        if (State != MatchState.Playing) return;
        while (delta > 0 && State == MatchState.Playing)
        {
            var step = MathF.Min(delta, 1f / 240);
            delta -= step;
            LeftY = Math.Clamp(LeftY + Math.Clamp(leftDirection, -1, 1) * 420 * step, 20, 520 - PaddleHeight);
            RightY = Math.Clamp(RightY + Math.Clamp(rightDirection, -1, 1) * 420 * step, 20, 520 - PaddleHeight);
            if (_serveDelay > 0) { _serveDelay -= step; continue; }
            Ball += Velocity * step;
            if (Ball.Y < 20 + Radius || Ball.Y > 520 - Radius)
            {
                Ball = new(Ball.X, Math.Clamp(Ball.Y, 20 + Radius, 520 - Radius));
                Velocity = new(Velocity.X, -Velocity.Y);
                Sound?.Invoke(MatchSound.Hit);
            }
            if (Velocity.X < 0 && Ball.X - Radius <= 54 && Ball.X + Radius >= 40 && Overlaps(LeftY)) Bounce(LeftY, 1);
            else if (Velocity.X > 0 && Ball.X + Radius >= 906 && Ball.X - Radius <= 920 && Overlaps(RightY)) Bounce(RightY, -1);
            if (Ball.X < -Radius || Ball.X > 960 + Radius)
            {
                var leftWonPoint = Ball.X > 960;
                if (leftWonPoint) LeftScore++; else RightScore++;
                if (LeftScore == WinningScore || RightScore == WinningScore)
                {
                    State = MatchState.GameOver;
                    Sound?.Invoke(MatchSound.Win);
                }
                else { Sound?.Invoke(MatchSound.Score); Serve(leftWonPoint ? -1 : 1); }
            }
        }
    }
    private bool Overlaps(float y) => Ball.Y + Radius >= y && Ball.Y - Radius <= y + PaddleHeight;
    private void Bounce(float y, int direction)
    {
        Ball = new(direction > 0 ? 54 + Radius : 906 - Radius, Ball.Y);
        var offset = Math.Clamp((Ball.Y - y - PaddleHeight / 2) / (PaddleHeight / 2), -1, 1);
        Velocity = new(direction * MathF.Min(MathF.Abs(Velocity.X) * 1.06f, 580), offset * 290);
        Sound?.Invoke(MatchSound.Hit);
    }
}
