using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// What one bot pilot believes about the other aircraft in the air.
    ///
    /// This is the piece that keeps the bots honest. A bot never reads another aircraft's transform
    /// to fly or shoot; it reads a <see cref="BotContact"/>, which only updates while the target is
    /// inside the search cone, inside visual range and not hidden behind terrain, and which carries
    /// a position estimate that is deliberately wrong by an amount that grows with range and shrinks
    /// with skill. Break line of sight and the bot keeps chasing a stale track until it gives up.
    /// </summary>
    public sealed class BotVision
    {
        private readonly List<BotContact> _contacts = new List<BotContact>();
        private readonly RaycastHit[] _losHits = new RaycastHit[8];

        /// <summary>Every aircraft this pilot has an opinion about, seen or remembered.</summary>
        public IReadOnlyList<BotContact> Contacts => _contacts;

        public void Clear()
        {
            _contacts.Clear();
        }

        /// <summary>
        /// Re-evaluates every candidate. Call on a slow clock (a few times a second): the sampling
        /// interval is itself part of the realism, since it puts a lag between what a target does and
        /// when the bot can possibly know about it.
        /// </summary>
        public void Tick(
            NetworkedAircraft self,
            PlaneRigidbody body,
            BotSkillProfile profile,
            int occlusionMask,
            float dt)
        {
            if (!self || !body)
                return;

            Vector3 eye = body.Position;
            Vector3 nose = body.TransformDirection(new Vector3(1f, 0f, 0f));
            float cosFov = Mathf.Cos(Mathf.Clamp(profile.visionHalfAngleDeg, 10f, 179f) * Mathf.Deg2Rad);
            float now = Time.time;

            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            for (int i = 0; i < all.Count; i++)
            {
                NetworkedAircraft other = all[i];
                if (!other || other == self || !other.IsSpawned || !other.IsAlive || other.Body == null)
                    continue;

                BotContact contact = Resolve(other);
                Vector3 truePosition = other.Body.Position;
                Vector3 toTarget = truePosition - eye;
                float distance = FlightSimMath.SafeMagnitude(toTarget);
                contact.Distance = distance;

                bool visible = distance > 1f
                               && distance <= EffectiveRange(profile, distance, toTarget, other)
                               && Vector3.Dot(toTarget / Mathf.Max(distance, 0.001f), nose) >= cosFov
                               && HasLineOfSight(eye, truePosition, body.transform, other.transform, occlusionMask, distance);

                contact.Visible = visible;

                if (visible)
                {
                    // Awareness is what a reaction time looks like in a state machine: a target has
                    // to stay in sight for a while before the pilot has actually processed it.
                    float rate = 1f / Mathf.Max(0.05f, profile.reactionSeconds);
                    contact.Awareness = Mathf.Min(1f, contact.Awareness + rate * dt);
                    contact.LastSeenTime = now;
                    contact.TrueDistance = distance;

                    Vector3 error = ResolveTrackingError(contact, profile, distance);
                    contact.EstimatedPosition = truePosition + error;
                    contact.EstimatedVelocity = other.Body.Velocity;
                }
                else
                {
                    // Fades rather than snapping to zero, so a target flickering behind a tower is
                    // not repeatedly re-acquired from scratch.
                    float decay = 1f / Mathf.Max(0.5f, profile.memorySeconds * 0.5f);
                    contact.Awareness = Mathf.Max(0f, contact.Awareness - decay * dt);

                    // Dead reckoning on the last known track. The estimate drifts exactly the way a
                    // pilot's mental picture of a bandit they lost sight of drifts.
                    contact.EstimatedPosition += contact.EstimatedVelocity * dt;
                }
            }

            PruneStale(profile, now);
        }

        /// <summary>
        /// Being shot at is information. The pilot cannot see behind the tail, but the airframe
        /// being hit tells them roughly where the shooter is, which is what turns a bounce into a
        /// fight instead of a free kill.
        /// </summary>
        public void NotifyIncomingFire(NetworkedAircraft shooter, Vector3 shooterPosition, Vector3 shooterVelocity, float confidence)
        {
            if (!shooter)
                return;

            BotContact contact = Resolve(shooter);
            contact.Awareness = Mathf.Max(contact.Awareness, Mathf.Clamp01(confidence));
            contact.LastSeenTime = Time.time;
            contact.EstimatedPosition = shooterPosition;
            contact.EstimatedVelocity = shooterVelocity;
            contact.WasShotBy = true;
            contact.LastFiredAtMeTime = Time.time;
        }

        public bool TryGet(NetworkedAircraft aircraft, out BotContact contact)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].Aircraft == aircraft)
                {
                    contact = _contacts[i];
                    return true;
                }
            }

            contact = null;
            return false;
        }

        private BotContact Resolve(NetworkedAircraft aircraft)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].Aircraft == aircraft)
                    return _contacts[i];
            }

            BotContact created = new BotContact
            {
                Aircraft = aircraft,
                EstimatedPosition = aircraft.Body ? aircraft.Body.Position : aircraft.transform.position,
                LastSeenTime = -999f,
                ErrorSeed = Random.Range(0f, 1000f)
            };
            _contacts.Add(created);
            return created;
        }

        private void PruneStale(BotSkillProfile profile, float now)
        {
            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                BotContact contact = _contacts[i];
                bool gone = contact.Aircraft == null || !contact.Aircraft.IsSpawned || !contact.Aircraft.IsAlive;
                bool forgotten = !contact.Visible && now - contact.LastSeenTime > profile.memorySeconds;
                if (gone || forgotten)
                    _contacts.RemoveAt(i);
            }
        }

        /// <summary>
        /// Spotting range is not a single number. A target crossing in front presents a wing and is
        /// easy to see; one flying straight at you is a dot. Nose-on contacts are worth roughly half
        /// the range of a beam contact.
        /// </summary>
        private static float EffectiveRange(BotSkillProfile profile, float distance, Vector3 toTarget, NetworkedAircraft target)
        {
            Vector3 targetNose = target.Body.TransformDirection(new Vector3(1f, 0f, 0f));
            Vector3 losDir = toTarget / Mathf.Max(distance, 0.001f);
            float aspect = 1f - Mathf.Abs(Vector3.Dot(losDir, targetNose));
            return profile.visualRange * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(aspect));
        }

        /// <summary>
        /// A smoothly wandering offset rather than per-frame white noise: a wrong mental picture is
        /// persistent for a second or two, which is what makes bot fire miss in a believable pattern
        /// instead of spraying symmetrically around the target.
        /// </summary>
        private static Vector3 ResolveTrackingError(BotContact contact, BotSkillProfile profile, float distance)
        {
            float magnitude = profile.trackingErrorPerKm * (distance / 1000f);
            if (magnitude < 0.01f)
                return Vector3.zero;

            float t = Time.time * 0.35f + contact.ErrorSeed;
            float x = Mathf.PerlinNoise(t, 0.13f) * 2f - 1f;
            float y = Mathf.PerlinNoise(0.57f, t) * 2f - 1f;
            float z = Mathf.PerlinNoise(t, t * 0.71f) * 2f - 1f;
            return new Vector3(x, y * 0.6f, z) * magnitude;
        }

        private bool HasLineOfSight(
            Vector3 from,
            Vector3 to,
            Transform self,
            Transform target,
            int mask,
            float distance)
        {
            Vector3 direction = (to - from) / Mathf.Max(distance, 0.001f);
            int n = Physics.RaycastNonAlloc(from, direction, _losHits, distance - 2f, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = _losHits[i].collider;
                if (!col)
                    continue;

                Transform t = col.transform;
                if (self && (t == self || t.IsChildOf(self)))
                    continue;
                if (target && (t == target || t.IsChildOf(target)))
                    continue;

                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// One bot's belief about one aircraft. A class rather than a struct because it is mutated in
    /// place across frames and the drift of the estimate over time is the whole point.
    /// </summary>
    public sealed class BotContact
    {
        public NetworkedAircraft Aircraft;

        /// <summary>Where the pilot thinks the target is. Not where it is.</summary>
        public Vector3 EstimatedPosition;

        public Vector3 EstimatedVelocity;

        /// <summary>Straight-line range at the last evaluation, metres.</summary>
        public float Distance;

        /// <summary>Range at the last sighting, metres.</summary>
        public float TrueDistance;

        /// <summary>True while the target is in the search cone, in range and unobstructed.</summary>
        public bool Visible;

        /// <summary>0 = has not registered, 1 = fully processed. Climbs over the reaction time.</summary>
        public float Awareness;

        public float LastSeenTime;

        public bool WasShotBy;

        public float LastFiredAtMeTime;

        public float ErrorSeed;

        public bool Acquired => Awareness >= 0.999f;

        public float TimeSinceSeen => Time.time - LastSeenTime;
    }
}
