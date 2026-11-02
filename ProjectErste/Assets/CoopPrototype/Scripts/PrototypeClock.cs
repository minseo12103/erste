namespace ShapeCoop
{
    // The clock belongs to a game, not to an individual attempt.
    public sealed class PrototypeClock
    {
        double started, final;
        public bool Completed { get; private set; }
        public void Begin(double now) { started = now; final = 0; Completed = false; }
        public double Elapsed(double now) => Completed ? final : System.Math.Max(0, now - started);
        public void Complete(double now) { if (Completed) return; final = Elapsed(now); Completed = true; }
    }
}
