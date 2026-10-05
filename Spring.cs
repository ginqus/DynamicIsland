namespace DynamicIsland;

sealed class Spring
{
    const double Step = 1.0 / 240;
    const double RestDistance = 0.005, RestVelocity = 0.05;

    public Spring(double value, double stiffness = 300, double damping = 24)
    {
        Value = Target = value;
        Stiffness = stiffness;
        Damping = damping;
    }

    public double Value { get; set; }
    public double Velocity { get; set; }
    public double Target { get; set; }
    public double Stiffness { get; private set; }
    public double Damping { get; private set; }

    public void Tune(double stiffness, double damping)
    {
        Stiffness = stiffness;
        Damping = damping;
    }

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    public bool Advance(double dt)
    {
        while (dt > 0)
        {
            double h = Math.Min(Step, dt);
            double accel = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += accel * h;
            Value += Velocity * h;
            dt -= h;
        }

        if (Math.Abs(Value - Target) >= RestDistance || Math.Abs(Velocity) >= RestVelocity) return true;
        Snap(Target);
        return false;
    }
}
