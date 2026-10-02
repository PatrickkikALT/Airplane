using UnityEngine;

namespace Airplane.FlightSimulation
{
    public static class LevelFlightSpeed
    {
        public static float SeaLevelTrueAirspeed(PlaneRigidbody body, AircraftEngine[] engines, AeroSurface[] surfaces)
        {
            if (!body || engines == null || engines.Length == 0 || surfaces == null || surfaces.Length == 0)
                return 0f;

            Transform root = body.transform;
            Quaternion[] aero = new Quaternion[surfaces.Length];
            Vector3[] axes = new Vector3[engines.Length];
            for (int i = 0; i < surfaces.Length; i++)
                aero[i] = surfaces[i] ? surfaces[i].AeroInBody(root) : Quaternion.identity;
            for (int i = 0; i < engines.Length; i++)
                axes[i] = engines[i] ? engines[i].ThrustAxisBody(root) : Vector3.forward;

            AtmosphereSample atmo = AtmosphericModel.SampleAltitudeDefault(0f);
            float mass = Mathf.Max(1f, body.Mass);
            float weight = mass * AtmosphericModel.StandardGravity;
            float hi = 80f;
            for (int i = 0; i < engines.Length; i++)
                if (engines[i] && engines[i].ZeroThrustAirspeed > hi)
                    hi = engines[i].ZeroThrustAirspeed;

            hi *= 1.5f;
            for (int i = 0; i < 8 && hi < 500f; i++)
            {
                if (!TryLevel(hi, out float tangentHi) || tangentHi <= 0f)
                    break;
                hi *= 1.35f;
            }

            float lo = 15f;
            if (!TryLevel(hi, out _))
                return 0f;

            for (int i = 0; i < 28; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (!TryLevel(mid, out float tangent) || tangent > 0f)
                    lo = mid;
                else
                    hi = mid;
            }

            return hi;

            bool TryLevel(float speed, out float tangent)
            {
                tangent = 0f;
                const float pitchLo = -25f * FlightSimMath.Deg2Rad;
                const float pitchHi = 28f * FlightSimMath.Deg2Rad;
                const int samples = 18;
                float prevPitch = pitchLo;
                float prevVertical = Vertical(speed, pitchLo);
                bool bracket = false;
                float bracketA = 0f;
                float bracketB = 0f;
                float lowestPositive = float.MaxValue;
                float lowestPitch = pitchLo;
                for (int s = 1; s <= samples; s++)
                {
                    float pitch = Mathf.Lerp(pitchLo, pitchHi, s / (float)samples);
                    float vertical = Vertical(speed, pitch);
                    if (!bracket && prevVertical <= 0f && vertical > 0f)
                    {
                        bracketA = prevPitch;
                        bracketB = pitch;
                        bracket = true;
                    }

                    if (vertical > 0f && vertical < lowestPositive)
                    {
                        lowestPositive = vertical;
                        lowestPitch = pitch;
                    }

                    prevPitch = pitch;
                    prevVertical = vertical;
                }

                float theta;
                if (bracket)
                {
                    float a = bracketA;
                    float b = bracketB;
                    for (int n = 0; n < 18; n++)
                    {
                        float mid = 0.5f * (a + b);
                        if (Vertical(speed, mid) > 0f)
                            b = mid;
                        else
                            a = mid;
                    }

                    theta = 0.5f * (a + b);
                }
                else if (lowestPositive < float.MaxValue)
                {
                    theta = lowestPitch;
                }
                else
                {
                    return false;
                }

                tangent = Tangent(speed, theta);
                return true;
            }

            float Vertical(float speed, float pitch)
            {
                Vector3 up = new(0f, Mathf.Cos(pitch), Mathf.Sin(pitch));
                return Vector3.Dot(NetForce(speed, pitch), up) - weight;
            }

            float Tangent(float speed, float pitch)
            {
                Vector3 flight = new(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch));
                return Vector3.Dot(NetForce(speed, pitch), flight);
            }

            Vector3 NetForce(float speed, float pitch)
            {
                Vector3 velocity = new Vector3(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch)) * speed;
                Vector3 force = Vector3.zero;
                for (int i = 0; i < engines.Length; i++)
                {
                    if (!engines[i])
                        continue;
                    force += axes[i] * engines[i].ThrustAt(speed, atmo.Density, 1f);
                }

                for (int i = 0; i < surfaces.Length; i++)
                {
                    if (!surfaces[i])
                        continue;
                    force += surfaces[i].ForceBody(aero[i], velocity, atmo);
                }

                return force;
            }
        }
    }
}