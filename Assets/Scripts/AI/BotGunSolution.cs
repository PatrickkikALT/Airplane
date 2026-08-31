using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Where the nose has to point for the rounds to arrive.
    ///
    /// Rounds leave the muzzle at the airframe's velocity plus muzzle speed, so a bot that simply
    /// aims at the target misses by its own crossing speed. Solving the intercept properly is what
    /// lets a bot pull lead through a turning fight the way a human learns to.
    /// </summary>
    public static class BotGunSolution
    {
        /// <summary>
        /// Solves for the boresight direction that puts a round on <paramref name="targetPosition"/>.
        ///
        /// <paramref name="leadTargetMotion"/> is set for ballistic guns, where the round takes real
        /// time to arrive and the target moves during the flight. Hitscan guns resolve instantly, so
        /// the only correction they need is for the shooter's own velocity skewing the round's path.
        /// Both cases collapse to the same fixed point, iterated a handful of times: the round's
        /// world velocity must be the closing vector required to cover the range in the flight time.
        /// </summary>
        public static bool TrySolve(
            Vector3 muzzle,
            Vector3 shooterVelocity,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float muzzleSpeed,
            bool leadTargetMotion,
            out Vector3 boresight,
            out float timeOfFlight)
        {
            boresight = Vector3.zero;
            timeOfFlight = 0f;

            Vector3 toTarget = targetPosition - muzzle;
            float range = FlightSimMath.SafeMagnitude(toTarget);
            if (range < 1f || muzzleSpeed < 1f)
                return false;

            Vector3 relativeVelocity = (leadTargetMotion ? targetVelocity : Vector3.zero) - shooterVelocity;

            // Unsolvable if the closing geometry outruns the round. Cannot happen with an 800 m/s
            // muzzle and a propeller aircraft, but a guard keeps a tuned-down gun from producing NaN.
            if (FlightSimMath.SafeMagnitude(relativeVelocity) >= muzzleSpeed * 0.98f)
                return false;

            float t = range / muzzleSpeed;
            Vector3 direction = toTarget / range;

            for (int i = 0; i < 4; i++)
            {
                Vector3 closing = toTarget / Mathf.Max(t, 1e-4f) + relativeVelocity;
                float closingSpeed = FlightSimMath.SafeMagnitude(closing);
                if (closingSpeed < 1e-3f)
                    return false;

                direction = closing / closingSpeed;
                Vector3 required = direction * muzzleSpeed - relativeVelocity;
                float requiredSpeed = FlightSimMath.SafeMagnitude(required);
                if (requiredSpeed < 1e-3f)
                    return false;

                t = range / requiredSpeed;
            }

            boresight = direction;
            timeOfFlight = t;
            return true;
        }

        /// <summary>
        /// Perturbs an aim direction by a slowly wandering error of up to
        /// <paramref name="errorDeg"/>. Slow rather than per-frame so bursts walk across a target
        /// instead of scattering evenly around it, which is how imprecise shooting actually looks.
        /// </summary>
        public static Vector3 ApplyAimError(Vector3 direction, float errorDeg, float seed, float rateHz = 0.6f)
        {
            if (errorDeg < 0.01f)
                return direction;

            float t = Time.time * rateHz + seed;
            float yaw = (Mathf.PerlinNoise(t, seed * 0.37f) * 2f - 1f) * errorDeg;
            float pitch = (Mathf.PerlinNoise(seed * 0.71f, t) * 2f - 1f) * errorDeg;

            Vector3 right = Vector3.Cross(direction, Vector3.up);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(direction, Vector3.forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, direction).normalized;

            return (Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right)) * direction;
        }
    }
}
