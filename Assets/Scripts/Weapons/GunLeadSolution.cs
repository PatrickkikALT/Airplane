using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.Weapons
{
    public static class GunLeadSolution
    {
        public static bool TrySolve(
            Vector3 muzzle,
            Vector3 shooterVelocity,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float muzzleSpeed,
            bool leadTargetMotion,
            Vector3 gravity,
            float quadraticDragK,
            float maxTime,
            out Vector3 boresight,
            out float timeOfFlight,
            out Vector3 intercept,
            out Vector3 aimPoint)
        {
            boresight = Vector3.zero;
            timeOfFlight = 0f;
            intercept = targetPosition;
            aimPoint = targetPosition;

            Vector3 toTarget = targetPosition - muzzle;
            float range = FlightSimMath.SafeMagnitude(toTarget);
            if (range < 1f || muzzleSpeed < 1f)
                return false;

            Vector3 vt = leadTargetMotion ? targetVelocity : Vector3.zero;
            Vector3 g = leadTargetMotion ? gravity : Vector3.zero;
            Vector3 los = toTarget / range;
            float opening = Vector3.Dot(vt - shooterVelocity, los);
            if (opening >= muzzleSpeed * 0.98f)
                return false;

            float t = range / muzzleSpeed;
            Vector3 direction = los;
            float cap = Mathf.Max(0.05f, maxTime);

            for (int i = 0; i < 8; i++)
            {
                t = Mathf.Clamp(t, 1e-4f, cap);
                intercept = targetPosition + vt * t;
                Vector3 drop = g * (0.5f * t * t);
                Vector3 requiredV0 = (intercept - muzzle - drop) / t;
                Vector3 additive = requiredV0 - shooterVelocity;
                float addSpeed = FlightSimMath.SafeMagnitude(additive);
                if (addSpeed < 1e-3f)
                    return false;

                direction = additive / addSpeed;
                Vector3 actualV0 = shooterVelocity + direction * muzzleSpeed;
                Vector3 remaining = intercept - muzzle - drop;
                float remainingRange = FlightSimMath.SafeMagnitude(remaining);
                if (remainingRange < 1e-3f)
                    break;

                Vector3 remainingDir = remaining / remainingRange;
                float closingSpeed = Vector3.Dot(actualV0, remainingDir);
                if (closingSpeed < 1f)
                    return false;

                float nextT = remainingRange / closingSpeed;
                if (quadraticDragK > 1e-8f)
                {
                    float speed = FlightSimMath.SafeMagnitude(actualV0);
                    float kv = quadraticDragK * Mathf.Max(speed, 1e-3f);
                    nextT = (Mathf.Exp(Mathf.Min(12f, quadraticDragK * remainingRange)) - 1f) / Mathf.Max(kv, 1e-8f);
                }

                t = nextT;
            }

            if (t <= 1e-4f || t >= cap * 0.999f)
                return false;

            intercept = targetPosition + vt * t;
            Vector3 finalDrop = g * (0.5f * t * t);
            Vector3 requiredAdd = (intercept - muzzle - finalDrop) / t - shooterVelocity;
            if (FlightSimMath.SafeMagnitude(requiredAdd) > muzzleSpeed * 1.15f)
                return false;

            float aimRange = FlightSimMath.SafeMagnitude(intercept - muzzle);
            boresight = direction;
            timeOfFlight = t;
            aimPoint = muzzle + direction * aimRange;
            return true;
        }

        public static bool TrySolveBattery(
            AircraftGun[] guns,
            GunTriggerChannel channel,
            PlaneRigidbody shooter,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            out Vector3 muzzle,
            out Vector3 shotAxis,
            out Vector3 shooterVelocity,
            out float muzzleSpeed,
            out bool leadTargetMotion,
            out Vector3 boresight,
            out float timeOfFlight,
            out Vector3 intercept,
            out Vector3 aimPoint)
        {
            muzzle = Vector3.zero;
            shotAxis = Vector3.zero;
            shooterVelocity = Vector3.zero;
            muzzleSpeed = 0f;
            leadTargetMotion = false;
            boresight = Vector3.zero;
            timeOfFlight = 0f;
            intercept = targetPosition;
            aimPoint = targetPosition;

            if (guns == null || shooter == null)
                return false;

            Vector3 positionSum = Vector3.zero;
            Vector3 axisSum = Vector3.zero;
            Vector3 velocitySum = Vector3.zero;
            float speedSum = 0f;
            float dragSum = 0f;
            float lifeSum = 0f;
            float rangeSum = 0f;
            int count = 0;
            bool anyBallistic = false;

            for (int i = 0; i < guns.Length; i++)
            {
                AircraftGun gun = guns[i];
                if (!gun || !gun.isActiveAndEnabled || gun.TriggerChannel != channel)
                    continue;
                if (gun.AmmoRemaining == 0)
                    continue;

                gun.GetMuzzleWorld(shooter, out Vector3 origin, out Vector3 axis);
                positionSum += origin;
                axisSum += axis;
                velocitySum += shooter.GetPointVelocity(origin);
                speedSum += gun.MuzzleSpeed;
                rangeSum += gun.MaxRange;
                lifeSum += gun.ProjectileLifetime;
                anyBallistic |= gun.FireMode == GunFireMode.Projectile;

                AtmosphereSample atmo = AtmosphericModel.SampleAt(origin);
                float mass = Mathf.Max(1e-6f, gun.ProjectileMass);
                dragSum += 0.5f * atmo.Density * gun.ProjectileCd * gun.ProjectileArea / mass;
                count++;
            }

            if (count == 0 || axisSum.sqrMagnitude < 1e-6f)
                return false;

            muzzle = positionSum / count;
            shotAxis = axisSum.normalized;
            shooterVelocity = velocitySum / count;
            muzzleSpeed = speedSum / count;
            leadTargetMotion = anyBallistic;
            float dragK = anyBallistic ? dragSum / count : 0f;
            float maxRange = rangeSum / count;
            float maxTime = anyBallistic
                ? Mathf.Max(0.2f, lifeSum / count)
                : Mathf.Max(0.05f, maxRange / Mathf.Max(1f, muzzleSpeed));
            Vector3 gravity = anyBallistic ? AtmosphericModel.SampleGravity() : Vector3.zero;

            return TrySolve(
                muzzle,
                shooterVelocity,
                targetPosition,
                targetVelocity,
                muzzleSpeed,
                leadTargetMotion,
                gravity,
                dragK,
                maxTime,
                out boresight,
                out timeOfFlight,
                out intercept,
                out aimPoint);
        }

        public static bool TryGetKinematics(Transform root, out Vector3 position, out Vector3 velocity)
        {
            position = Vector3.zero;
            velocity = Vector3.zero;
            if (!root)
                return false;

            PlaneRigidbody plane = root.GetComponentInParent<PlaneRigidbody>();
            if (plane)
            {
                position = plane.Position;
                velocity = plane.Velocity;
                return true;
            }

            Rigidbody rb = root.GetComponentInParent<Rigidbody>();
            if (rb)
            {
                position = rb.worldCenterOfMass;
                velocity = rb.linearVelocity;
                return true;
            }

            position = root.position;
            velocity = Vector3.zero;
            return true;
        }
    }
}
