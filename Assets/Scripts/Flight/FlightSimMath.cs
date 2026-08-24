using System.Runtime.CompilerServices;
using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Integration method used by <see cref="PlaneRigidbody"/>.
    /// Semi-implicit Euler is the default: cheap, symplectic-ish, and stable with aero damping.
    /// RK4 is more accurate for high-rate tumbling / large sub-steps at a higher CPU cost.
    /// </summary>
    public enum IntegrationScheme
    {
        SemiImplicitEuler = 0,
        RK4 = 1
    }

    
    /// <summary>
    /// Role of a lifting / drag surface. The flight controller maps stick axes onto these types.
    /// </summary>
    public enum AeroControlType
    {
        None = 0,
        Aileron = 1,
        Elevator = 2,
        Rudder = 3,
        Flap = 4,
        Airbrake = 5
    }

    /// <summary>
    /// Force model used by <see cref="AeroSurface"/>.
    /// </summary>
    public enum AeroSurfaceMode
    {
        /// <summary>Cambered airfoil with attached / stall / post-stall (flat-plate) blend, full 360°.</summary>
        LiftingAirfoil = 0,
        /// <summary>Fuselage / bluff body: mostly parasitic drag plus weak α/β restoring forces.</summary>
        BluffBody = 1
    }

    /// <summary>
    /// Compact 3×3 matrix for the body-axis inertia tensor and its inverse.
    /// Stored in row-major order. Allocations: none (value type).
    /// </summary>
    public struct Mat3
    {
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;

        public Mat3(
            float m00, float m01, float m02,
            float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            this.m00 = m00; this.m01 = m01; this.m02 = m02;
            this.m10 = m10; this.m11 = m11; this.m12 = m12;
            this.m20 = m20; this.m21 = m21; this.m22 = m22;
        }

        public static Mat3 Diagonal(float xx, float yy, float zz)
        {
            return new Mat3(xx, 0f, 0f, 0f, yy, 0f, 0f, 0f, zz);
        }

        /// <summary>
        /// Builds the inertia tensor in the standard rigid-body form:
        /// [[ Ixx, -Ixy, -Ixz ],
        ///  [-Ixy,  Iyy, -Iyz ],
        ///  [-Ixz, -Iyz,  Izz ]].
        /// </summary>
        public static Mat3 Inertia(float ixx, float iyy, float izz, float ixy, float ixz, float iyz)
        {
            return new Mat3(
                ixx, -ixy, -ixz,
                -ixy, iyy, -iyz,
                -ixz, -iyz, izz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 Multiply(Vector3 v)
        {
            return new Vector3(
                m00 * v.x + m01 * v.y + m02 * v.z,
                m10 * v.x + m11 * v.y + m12 * v.z,
                m20 * v.x + m21 * v.y + m22 * v.z);
        }

        public bool TryInvert(out Mat3 inverse)
        {
            float a = m00, b = m01, c = m02;
            float d = m10, e = m11, f = m12;
            float g = m20, h = m21, i = m22;

            float A = e * i - f * h;
            float B = f * g - d * i;
            float C = d * h - e * g;
            float det = a * A + b * B + c * C;
            if (det > -1e-12f && det < 1e-12f)
            {
                inverse = default;
                return false;
            }

            float invDet = 1f / det;
            inverse = new Mat3(
                A * invDet, (c * h - b * i) * invDet, (b * f - c * e) * invDet,
                B * invDet, (a * i - c * g) * invDet, (c * d - a * f) * invDet,
                C * invDet, (b * g - a * h) * invDet, (a * e - b * d) * invDet);
            return true;
        }
    }

    /// <summary>
    /// Snapshot of the state. Value type so RK4 can clone without GC.
    /// </summary>
    public struct RigidBodyState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Orientation;
        public Vector3 AngularVelocityBody;
    }

    /// <summary>
    /// State derivatives used by RK4: dr/dt = v, dv/dt = a, dq/dt = qDot, dω/dt = α.
    /// </summary>
    public struct RigidBodyDerivatives
    {
        public Vector3 Velocity;
        public Vector3 Acceleration;
        public Quaternion OrientationDot;
        public Vector3 AngularAccelerationBody;
    }

    /// <summary>
    /// Allocation-free math used by the solver and aero.
    /// </summary>
    public static class FlightSimMath
    {
        public const float Deg2Rad = Mathf.Deg2Rad;
        public const float Rad2Deg = Mathf.Rad2Deg;
        public const float Pi = Mathf.PI;
        public const float TwoPi = Mathf.PI * 2f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 SafeNormalize(Vector3 v, float epsSqr = 1e-12f)
        {
            float magSqr = v.x * v.x + v.y * v.y + v.z * v.z;
            if (magSqr < epsSqr)
                return Vector3.zero;
            float inv = 1f / (float)System.Math.Sqrt(magSqr);
            return new Vector3(v.x * inv, v.y * inv, v.z * inv);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeMagnitude(Vector3 v, float epsSqr = 1e-12f)
        {
            float magSqr = v.x * v.x + v.y * v.y + v.z * v.z;
            return magSqr < epsSqr ? 0f : (float)System.Math.Sqrt(magSqr);
        }

        /// <summary>Wraps an angle in radians to (−π, π].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WrapPi(float a)
        {
            a = (a + Pi) % TwoPi;
            if (a < 0f) a += TwoPi;
            return a - Pi;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float denom = edge1 - edge0;
            if (denom > -1e-8f && denom < 1e-8f)
                return x >= edge1 ? 1f : 0f;
            float t = (x - edge0) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x)
        {
            if (x < 0f) return 0f;
            if (x > 1f) return 1f;
            return x;
        }

        /// <summary>
        /// Body-frame quaternion derivative: dq/dt = 0.5 · q * ω, with ω = (ωx, ωy, ωz, 0).
        /// Matches Unity's left-handed q * v body-to-world convention.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion QuaternionDerivative(Quaternion q, Vector3 omegaBody)
        {
            Quaternion w = new Quaternion(omegaBody.x, omegaBody.y, omegaBody.z, 0f);
            Quaternion qDot = q * w;
            qDot.x *= 0.5f;
            qDot.y *= 0.5f;
            qDot.z *= 0.5f;
            qDot.w *= 0.5f;
            return qDot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion IntegrateQuaternionEuler(Quaternion q, Vector3 omegaBody, float dt)
        {
            Quaternion qDot = QuaternionDerivative(q, omegaBody);
            q.x += qDot.x * dt;
            q.y += qDot.y * dt;
            q.z += qDot.z * dt;
            q.w += qDot.w * dt;
            return Normalize(q);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion AddScaled(Quaternion q, Quaternion qDot, float dt)
        {
            return new Quaternion(
                q.x + qDot.x * dt,
                q.y + qDot.y * dt,
                q.z + qDot.z * dt,
                q.w + qDot.w * dt);
        }

        public static Quaternion Normalize(Quaternion q)
        {
            float magSqr = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (magSqr < 1e-16f)
                return Quaternion.identity;
            float inv = 1f / (float)System.Math.Sqrt(magSqr);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        /// <summary>
        /// Unity body axes: +X forward, +Y up, +Z right.
        /// Angle of attack is the pitch of the velocity vector in the body X/Y plane.
        /// Positive α = nose above the velocity vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleOfAttack(Vector3 velocityBody)
        {
            return Mathf.Atan2(-velocityBody.y, velocityBody.x);
        }

        /// <summary>Positive β = velocity from the right</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sideslip(Vector3 velocityBody)
        {
            return Mathf.Atan2(velocityBody.z, velocityBody.x);
        }
    }
}
