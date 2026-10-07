namespace Eco.Minecarts.Physics;

/// <summary>Parameters expressed in Eco block units, seconds, kilograms, and newtons.</summary>
public sealed record MinecartTuning(
    double EmptyMassKg = 280,
    double MetersPerBlock = 1,
    double WheelInertiaMassFactor = 0.06,
    double RollingResistanceCoefficient = 0.008,
    double AerodynamicDrag = 0.65,
    double MaximumHandbrakeForceN = 4_800,
    double StaticResistanceN = 95,
    // Voxel bends have a 0.5 m radius. The old 4.5 m/s² threshold
    // derailed at only 1.5 m/s; flanged wheels must retain walking-speed carts.
    double WarningLateralAcceleration = 10,
    double DerailLateralAcceleration = 18,
    double DerailGraceSeconds = 0.45,
    double Gravity = 9.80665);

public readonly record struct MinecartState(
    double Distance,
    double Speed,
    double DerailOverloadSeconds,
    bool Derailed = false);

public readonly record struct MinecartInput(
    double CargoMassKg,
    double DriveForceN,
    double Handbrake,
    double Grade,
    double Curvature,
    double CantRadians = 0);

public readonly record struct MinecartStep(
    MinecartState State,
    double Acceleration,
    double NetForceN,
    double LateralAcceleration,
    bool CurveWarning);

/// <summary>
/// Deterministic one-dimensional dynamics for a cart constrained to a track centerline.
/// Network/world code owns track-edge traversal and supplies local grade and curvature.
/// </summary>
public static class RailPhysics
{
    public static MinecartStep Integrate(
        MinecartState state,
        MinecartInput input,
        MinecartTuning tuning,
        double deltaSeconds)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Integrate");
        ArgumentOutOfRangeException.ThrowIfNegative(input.CargoMassKg);
        ArgumentOutOfRangeException.ThrowIfNegative(input.Handbrake);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Handbrake, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(input.Curvature);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tuning.MetersPerBlock);

        if (state.Derailed)
            return new MinecartStep(state, 0, 0, 0, false);

        var mass = tuning.EmptyMassKg + input.CargoMassKg;
        var effectiveMass = mass * (1 + tuning.WheelInertiaMassFactor);
        var gravityForce = -mass * tuning.Gravity * input.Grade;
        var brakeMagnitude = tuning.MaximumHandbrakeForceN * input.Handbrake;
        var movingDirection = Math.Sign(state.Speed);

        var forceWithoutResistance = input.DriveForceN + gravityForce;
        var rollingMagnitude = tuning.RollingResistanceCoefficient * mass * tuning.Gravity;
        // Resistance can hold a heavy vehicle, never start it in the opposite direction.
        if (movingDirection == 0 && Math.Abs(forceWithoutResistance) <= Math.Max(tuning.StaticResistanceN, rollingMagnitude) + brakeMagnitude)
        {
            var held = state with { Speed = 0, DerailOverloadSeconds = 0 };
            return new MinecartStep(held, 0, 0, 0, false);
        }

        var forceDirection = movingDirection == 0 ? Math.Sign(forceWithoutResistance) : movingDirection;
        var rollingForce = -forceDirection * rollingMagnitude;
        var speedMetersPerSecond = state.Speed * tuning.MetersPerBlock;
        var dragForce = -tuning.AerodynamicDrag * speedMetersPerSecond * Math.Abs(speedMetersPerSecond);
        var brakeForce = -forceDirection * brakeMagnitude;
        var netForce = forceWithoutResistance + rollingForce + dragForce + brakeForce;
        var acceleration = netForce / effectiveMass / tuning.MetersPerBlock;
        var nextSpeed = state.Speed + acceleration * deltaSeconds;

        // Brakes and resistance stop at zero instead of numerically reversing the cart.
        if (movingDirection != 0 && Math.Sign(nextSpeed) != movingDirection && Math.Sign(netForce) == -movingDirection)
            nextSpeed = 0;

        var nextDistance = state.Distance + nextSpeed * deltaSeconds;
        var lateralAcceleration = nextSpeed * nextSpeed * input.Curvature * tuning.MetersPerBlock
                                  - tuning.Gravity * Math.Tan(input.CantRadians);
        var absoluteLateral = Math.Abs(lateralAcceleration);
        var overload = absoluteLateral > tuning.DerailLateralAcceleration
            ? state.DerailOverloadSeconds + deltaSeconds
            : Math.Max(0, state.DerailOverloadSeconds - deltaSeconds * 2);
        var derailed = overload >= tuning.DerailGraceSeconds;
        var next = new MinecartState(nextDistance, nextSpeed, overload, derailed);

        return new MinecartStep(
            next,
            acceleration,
            netForce,
            lateralAcceleration,
            absoluteLateral >= tuning.WarningLateralAcceleration);
    }
}
