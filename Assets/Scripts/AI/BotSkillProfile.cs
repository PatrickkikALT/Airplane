using System;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Everything that separates a nervous trainee from an ace, in one serializable block.
    ///
    /// The numbers are deliberately kept as absolute quantities rather than abstract multipliers so
    /// they can be reasoned about in the units the rest of the flight model uses: metres, seconds,
    /// degrees. <see cref="FromSkill"/> interpolates a full profile from a single 0..1 dial, which is
    /// what the spawner uses to build a squadron with a spread of competence.
    /// </summary>
    [Serializable]
    public sealed class BotSkillProfile
    {
        [Tooltip("Where this pilot sits between rookie (0) and ace (1). Only used as a record; the " +
                 "fields below are what the pilot actually flies on.")]
        [Range(0f, 1f)] public float skill = 0.5f;

        [Header("Eyesight")]
        [Tooltip("Furthest an aircraft can be spotted head-on in clear air, metres.")]
        public float visualRange = 2200f;

        [Tooltip("Half-angle of the search cone, degrees. Nothing outside it exists to this pilot.")]
        public float visionHalfAngleDeg = 100f;

        [Tooltip("Seconds of continuous sight before a contact registers as a target.")]
        public float reactionSeconds = 1.2f;

        [Tooltip("How long a lost contact is still chased on its last known track, seconds.")]
        public float memorySeconds = 14f;

        [Tooltip("Position estimate error per kilometre of range, metres. This is what makes a bot " +
                 "shoot at where it thinks you are rather than where you are.")]
        public float trackingErrorPerKm = 26f;

        [Header("Gunnery")]
        [Tooltip("Range the pilot considers worth shooting at, metres.")]
        public float gunRange = 620f;

        [Tooltip("Random boresight error while tracking, degrees.")]
        public float aimErrorDeg = 3.2f;

        [Tooltip("Alignment with the firing solution needed to squeeze the trigger, degrees.")]
        public float firingConeDeg = 2.8f;

        [Tooltip("Length of one burst, seconds.")]
        public float burstSeconds = 0.7f;

        [Tooltip("Trigger-off time between bursts, seconds.")]
        public float burstGapSeconds = 1.0f;

        [Header("Airmanship")]
        [Tooltip("Load factor the pilot is willing to pull, G.")]
        public float maxLoadFactor = 6f;

        [Tooltip("Bank angle used in a hard turn, degrees.")]
        public float maxBankDeg = 78f;

        [Tooltip("Angle of attack the pilot will not knowingly exceed, degrees.")]
        public float aoaLimitDeg = 15f;

        [Tooltip("Patrol true airspeed, m/s.")]
        public float cruiseSpeed = 105f;

        [Tooltip("Airspeed sought once a fight starts, m/s.")]
        public float combatSpeed = 145f;

        [Tooltip("Altitude above ground at which recovery overrides everything else, metres.")]
        public float minSafeAltitude = 160f;

        [Header("Nerve")]
        [Tooltip("Hit-point fraction below which the pilot starts breaking off, 0..1.")]
        [Range(0f, 1f)] public float evadeHealthFraction = 0.35f;

        [Tooltip("How long an evasive manoeuvre is committed to, seconds.")]
        public float evadeSeconds = 5f;

        /// <summary>
        /// Builds a profile by interpolating between a rookie and an ace. Everything a low-skill
        /// pilot is bad at (spotting, tracking, gunnery, nerve) moves together, which is what makes
        /// a mixed flight read as a mixed flight rather than as noisy clones.
        /// </summary>
        public static BotSkillProfile FromSkill(float skill01)
        {
            float t = Mathf.Clamp01(skill01);

            return new BotSkillProfile
            {
                skill = t,

                visualRange = Mathf.Lerp(1300f, 3200f, t),
                visionHalfAngleDeg = Mathf.Lerp(80f, 115f, t),
                reactionSeconds = Mathf.Lerp(2.2f, 0.5f, t),
                memorySeconds = Mathf.Lerp(7f, 20f, t),
                trackingErrorPerKm = Mathf.Lerp(55f, 8f, t),

                gunRange = Mathf.Lerp(420f, 700f, t),
                aimErrorDeg = Mathf.Lerp(7f, 0.9f, t),
                firingConeDeg = Mathf.Lerp(6f, 1.8f, t),
                burstSeconds = Mathf.Lerp(1.4f, 0.5f, t),
                burstGapSeconds = Mathf.Lerp(1.6f, 0.5f, t),

                maxLoadFactor = Mathf.Lerp(4f, 7.5f, t),
                maxBankDeg = Mathf.Lerp(62f, 82f, t),
                aoaLimitDeg = Mathf.Lerp(11f, 16f, t),
                cruiseSpeed = Mathf.Lerp(95f, 115f, t),
                combatSpeed = Mathf.Lerp(125f, 160f, t),
                minSafeAltitude = Mathf.Lerp(280f, 110f, t),

                evadeHealthFraction = Mathf.Lerp(0.55f, 0.25f, t),
                evadeSeconds = Mathf.Lerp(6.5f, 3.5f, t)
            };
        }

        public BotSkillProfile Clone()
        {
            return (BotSkillProfile)MemberwiseClone();
        }
    }
}
