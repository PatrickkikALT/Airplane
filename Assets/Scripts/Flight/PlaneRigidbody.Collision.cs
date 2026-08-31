using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// One contact in a <see cref="PlaneCollision"/>. Mirrors Unity's <see cref="ContactPoint"/>.
    /// </summary>
    public readonly struct PlaneContactPoint
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Collider ThisCollider;
        public readonly Collider OtherCollider;
        /// <summary>Negative while overlapping.</summary>
        public readonly float Separation;

        public PlaneContactPoint(Vector3 point, Vector3 normal, Collider thisCollider, Collider otherCollider, float separation)
        {
            this.Point = point;
            this.Normal = normal;
            this.ThisCollider = thisCollider;
            this.OtherCollider = otherCollider;
            this.Separation = separation;
        }
    }

    /// <summary>
    /// Hit report from <see cref="PlaneRigidbody"/> collider contact.
    /// Same role as Unity's <see cref="Collision"/> for <c>OnCollisionEnter</c>.
    /// </summary>
    public struct PlaneCollision
    {
        public Collider Collider;
        public Collider ThisCollider;
        public Rigidbody Rigidbody;
        public PlaneRigidbody PlaneBody;
        public Transform Transform;
        public GameObject GameObject;
        public Vector3 RelativeVelocity;
        public Vector3 Impulse;
        public Vector3 Point;
        public Vector3 Normal;
        public float Separation;
        public int ContactCount;

        public PlaneContactPoint GetContact(int index)
        {
            if (index < 0 || index >= ContactCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new PlaneContactPoint(Point, Normal, ThisCollider, Collider, Separation);
        }
    }

    public sealed partial class PlaneRigidbody
    {
        private Rigidbody _proxyBody;
        private Collider[] _ownColliders = Array.Empty<Collider>();
        private BoxCollider _fallbackHull;
        private readonly Collider[] _overlapBuffer = new Collider[48];
        private readonly RaycastHit[] _rayBuffer = new RaycastHit[16];
        private readonly Contact[] _contacts = new Contact[32];
        private int _gizmoContactCount;

        private Dictionary<EntityId, PlaneCollision> _hitsThisTick = new Dictionary<EntityId, PlaneCollision>(16);
        private Dictionary<EntityId, PlaneCollision> _hitsLastTick = new Dictionary<EntityId, PlaneCollision>(16);
        private readonly Dictionary<EntityId, PlaneCollision> _dispatchThis = new Dictionary<EntityId, PlaneCollision>(16);
        private readonly Dictionary<EntityId, PlaneCollision> _dispatchLast = new Dictionary<EntityId, PlaneCollision>(16);

        private readonly HashSet<EntityId> _ignoredColliders = new HashSet<EntityId>();
        private readonly HashSet<ColliderPair> _ignoredPairs = new HashSet<ColliderPair>();

        /// <summary>Fired once when contact with a collider begins. Same timing idea as <c>OnCollisionEnter</c>.</summary>
        public event Action<PlaneCollision> CollisionEnter;

        /// <summary>Fired every physics tick while still overlapping that collider.</summary>
        public event Action<PlaneCollision> CollisionStay;

        /// <summary>Fired once when contact with a collider ends.</summary>
        public event Action<PlaneCollision> CollisionExit;

        /// <summary>
        /// Ignore contact between this body and <paramref name="collider"/>, the same role as
        /// <see cref="Physics.IgnoreCollision(Collider, Collider, bool)"/> for a whole aircraft hull.
        /// Does not affect raycasts.
        /// </summary>
        public void IgnoreCollision(Collider collider, bool ignore = true)
        {
            if (!collider)
                return;

            EntityId id = collider.GetEntityId();
            if (ignore)
                _ignoredColliders.Add(id);
            else
                _ignoredColliders.Remove(id);

            if (_ownColliders == null || _ownColliders.Length == 0)
                CacheOwnColliders();
            foreach (Collider own in _ownColliders)
            {
                if (own && own != collider)
                    Physics.IgnoreCollision(own, collider, ignore);
            }
        }

        /// <summary>
        /// Ignore contact between a specific pair of colliders. Either collider may belong to this
        /// body; the other is typically a projectile or another aircraft hull.
        /// </summary>
        public void IgnoreCollision(Collider collider1, Collider collider2, bool ignore = true)
        {
            if (!collider1 || !collider2 || collider1 == collider2)
                return;

            ColliderPair key = PairKey(collider1, collider2);
            if (ignore)
                _ignoredPairs.Add(key);
            else
                _ignoredPairs.Remove(key);

            Physics.IgnoreCollision(collider1, collider2, ignore);
        }

        public bool GetIgnoreCollision(Collider collider)
        {
            return collider && _ignoredColliders.Contains(collider.GetEntityId());
        }

        public bool GetIgnoreCollision(Collider collider1, Collider collider2)
        {
            if (!collider1 || !collider2)
                return false;
            if (_ignoredColliders.Contains(collider1.GetEntityId()) ||
                _ignoredColliders.Contains(collider2.GetEntityId()))
                return true;
            return _ignoredPairs.Contains(PairKey(collider1, collider2));
        }

        private static ColliderPair PairKey(Collider a, Collider b)
        {
            return new ColliderPair(a.GetEntityId(), b.GetEntityId());
        }

        private bool IsCollisionIgnored(Collider own, Collider other)
        {
            if (!own || !other)
                return true;
            if (_ignoredColliders.Contains(other.GetEntityId()) ||
                _ignoredColliders.Contains(own.GetEntityId()))
                return true;
            return _ignoredPairs.Contains(PairKey(own, other));
        }

        private void ConfigureKinematicProxy()
        {
            _proxyBody = GetComponent<Rigidbody>();
            if (_proxyBody == null)
                _proxyBody = gameObject.AddComponent<Rigidbody>();

            _proxyBody.mass = mass;
            _proxyBody.useGravity = false;
            _proxyBody.isKinematic = true;
            _proxyBody.interpolation = RigidbodyInterpolation.None;
            _proxyBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _proxyBody.detectCollisions = true;
            _proxyBody.constraints = RigidbodyConstraints.None;
        }

        private void CacheOwnColliders()
        {
            Collider[] found = GetComponentsInChildren<Collider>(true);
            int count = found.Count(c => c && c.enabled && !c.isTrigger);

            if (count == 0 && createFallbackHull && Application.isPlaying)
            {
                if (!_fallbackHull)
                {
                    _fallbackHull = gameObject.AddComponent<BoxCollider>();
                    _fallbackHull.size = fallbackHullSize;
                    _fallbackHull.center = fallbackHullCenter;
                    _fallbackHull.isTrigger = false;
                }

                found = GetComponentsInChildren<Collider>(true);
                count = found.Count(c => c && c.enabled && !c.isTrigger);
            }

            _ownColliders = new Collider[count];
            int w = 0;
            foreach (Collider c in found)
            {
                if (c && c.enabled && !c.isTrigger)
                    _ownColliders[w++] = c;
            }
        }

        private bool IsOwnCollider(Collider c)
        {
            if (!c)
                return false;
            if (_ownColliders.Any(t => t == c))
            {
                return true;
            }

            return c.transform == transform || c.transform.IsChildOf(transform);
        }

        private void ResolveColliderContacts()
        {
            if (!enableColliderContact)
                return;

            if (_ownColliders == null || _ownColliders.Length == 0)
                CacheOwnColliders();
            if (_ownColliders is { Length: 0 })
                return;

            ApplyToTransform(_position, _orientation);
            Physics.SyncTransforms();

            int iterations = Mathf.Max(1, collisionIterations);
            for (int iter = 0; iter < iterations; iter++)
            {
                int n = CollectContacts();
                _gizmoContactCount = n;
                if (n == 0)
                    break;
                SolveContacts(n);
                RecordCollisionHits(n);
                ApplyToTransform(_position, _orientation);
                Physics.SyncTransforms();
            }
        }

        private int CollectContacts()
        {
            int count = 0;
            int mask = collisionMask.value;
            float slop = Mathf.Max(0f, collisionSlop);

            foreach (Collider own in _ownColliders)
            {
                if (!own || !own.enabled || own.isTrigger)
                    continue;

                Bounds bounds = own.bounds;
                int hits = Physics.OverlapBoxNonAlloc(
                    bounds.center,
                    bounds.extents,
                    _overlapBuffer,
                    Quaternion.identity,
                    mask,
                    QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hits; h++)
                {
                    Collider other = _overlapBuffer[h];
                    if (!other || other == own || other.isTrigger || IsOwnCollider(other))
                        continue;
                    if (IsCollisionIgnored(own, other))
                        continue;

                    if (!Physics.ComputePenetration(
                            own, own.transform.position, own.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance))
                        continue;

                    if (distance <= slop * 0.25f || direction.sqrMagnitude < 1e-12f)
                        continue;

                    Vector3 n = direction.normalized;
                    Vector3 contactPoint = own.ClosestPoint(other.bounds.center);
                    if ((contactPoint - own.bounds.center).sqrMagnitude < 1e-10f)
                        contactPoint = own.bounds.center - n * (distance * 0.5f);

                    PlaneRigidbody otherPlane = other.GetComponentInParent<PlaneRigidbody>();
                    if (otherPlane == this)
                        continue;
                    if (otherPlane && otherPlane.IsCollisionIgnored(other, own))
                        continue;

                    // A plane that is not solving (a replicated proxy owned by another peer) cannot
                    // absorb an impulse or push itself out of an overlap, so it acts as immovable
                    // geometry. The lowest-id tie-break that stops a pair being solved twice only
                    // makes sense when both bodies actually solve.
                    bool otherPlaneStatic = otherPlane && !otherPlane._simulationEnabled;
                    if (otherPlane && !otherPlaneStatic && otherPlane.GetEntityId() < GetEntityId())
                        continue;

                    Rigidbody otherRb = other.attachedRigidbody;
                    if (otherPlane)
                        otherRb = null;
                    else if (otherRb && otherRb.isKinematic && otherRb == _proxyBody)
                        continue;

                    GetContactMaterials(own, other, out float restitution, out float friction);

                    if (count >= _contacts.Length)
                        return count;

                    _contacts[count++] = new Contact
                    {
                        Point = contactPoint,
                        Normal = n,
                        Penetration = distance,
                        Restitution = restitution,
                        Friction = friction,
                        ThisCollider = own,
                        OtherCollider = other,
                        OtherBody = otherRb,
                        OtherPlane = otherPlane,
                        OtherPlaneStatic = otherPlaneStatic
                    };
                }
            }

            return count;
        }

        private void SolveContacts(int count)
        {
            float invMassA = 1f / mass;
            float slop = Mathf.Max(0f, collisionSlop);
            float baumgarte = Mathf.Clamp01(collisionBaumgarte);

            for (int i = 0; i < count; i++)
            {
                Contact c = _contacts[i];
                Vector3 n = c.Normal;
                Vector3 rA = c.Point - _position;

                float invMassB = 0f;
                Vector3 rB = Vector3.zero;
                Vector3 vB = Vector3.zero;
                bool otherIsDynamicRb = c.OtherBody && !c.OtherBody.isKinematic;
                bool otherPlaneResponds = c.OtherPlane && !c.OtherPlaneStatic;
                if (c.OtherPlane)
                {
                    // A static proxy still contributes its velocity, so a head-on closing speed is
                    // right even though only this aircraft reacts.
                    invMassB = otherPlaneResponds ? 1f / c.OtherPlane.mass : 0f;
                    rB = c.Point - c.OtherPlane._position;
                    vB = c.OtherPlane.GetPointVelocity(c.Point);
                }
                else if (c.OtherBody)
                {
                    rB = c.Point - c.OtherBody.worldCenterOfMass;
                    vB = c.OtherBody.GetPointVelocity(c.Point);
                    if (otherIsDynamicRb && c.OtherBody.mass > 1e-6f)
                        invMassB = 1f / c.OtherBody.mass;
                }

                Vector3 vA = GetPointVelocity(c.Point);
                Vector3 vRel = vA - vB;
                c.RelativeVelocity = vRel;
                float vN = Vector3.Dot(vRel, n);

                float invMn = InverseMassAlong(rA, n, invMassA);
                if (otherPlaneResponds)
                    invMn += c.OtherPlane.InverseMassAlong(rB, n, invMassB);
                else if (!c.OtherPlane && otherIsDynamicRb)
                    invMn += RigidbodyInverseMassAlong(c.OtherBody, rB, n);
                if (invMn < 1e-8f)
                {
                    _contacts[i] = c;
                    continue;
                }

                float jn = 0f;
                if (vN < 0f)
                    jn = -(1f + c.Restitution) * vN / invMn;

                Vector3 impulse = n * jn;

                Vector3 vTan = vRel - n * vN;
                float vTanMag = vTan.magnitude;
                if (vTanMag > 1e-4f && c.Friction > 1e-6f)
                {
                    Vector3 t = vTan / vTanMag;
                    float invMt = InverseMassAlong(rA, t, invMassA);
                    if (otherPlaneResponds)
                        invMt += c.OtherPlane.InverseMassAlong(rB, t, invMassB);
                    else if (!c.OtherPlane && otherIsDynamicRb)
                        invMt += RigidbodyInverseMassAlong(c.OtherBody, rB, t);
                    if (invMt > 1e-8f)
                    {
                        float jt = -vTanMag / invMt;
                        float maxJt = c.Friction * Mathf.Abs(jn);
                        if (jt > maxJt) jt = maxJt;
                        else if (jt < -maxJt) jt = -maxJt;
                        impulse += t * jt;
                    }
                }

                c.Impulse = impulse;
                _contacts[i] = c;

                ApplyImpulseAtWorldPoint(impulse, c.Point);
                if (otherPlaneResponds)
                    c.OtherPlane.ApplyImpulseAtWorldPoint(-impulse, c.Point);
                else if (!c.OtherPlane && otherIsDynamicRb)
                {
                    c.OtherBody.WakeUp();
                    c.OtherBody.AddForceAtPosition(-impulse, c.Point, ForceMode.Impulse);
                }

                float correction = (c.Penetration - slop) * baumgarte;
                if (correction > 0f)
                {
                    float wA = invMassA;
                    float wB = otherPlaneResponds || (!c.OtherPlane && otherIsDynamicRb) ? invMassB : 0f;
                    float wSum = wA + wB;
                    if (wSum < 1e-8f)
                        wSum = wA;
                    Vector3 corr = n * correction;
                    _position += corr * (wA / wSum);
                    if (otherPlaneResponds)
                        c.OtherPlane._position -= corr * (wB / wSum);
                    else if (!c.OtherPlane && otherIsDynamicRb)
                        c.OtherBody.position -= corr * (wB / wSum);
                }
            }
        }

        private float InverseMassAlong(Vector3 rWorld, Vector3 n, float invMass)
        {
            Vector3 rXn = Vector3.Cross(rWorld, n);
            Vector3 torqueBody = Quaternion.Inverse(_orientation) * rXn;
            Vector3 alphaBody = _inertiaInverse.Multiply(torqueBody);
            Vector3 alphaWorld = _orientation * alphaBody;
            float angular = Vector3.Dot(Vector3.Cross(alphaWorld, rWorld), n);
            return Mathf.Max(1e-8f, invMass + angular);
        }

        private static float RigidbodyInverseMassAlong(Rigidbody rb, Vector3 rWorld, Vector3 n)
        {
            if (!rb || rb.isKinematic)
                return 0f;

            float invMass = rb.mass > 1e-6f ? 1f / rb.mass : 0f;
            Quaternion rot = rb.rotation * rb.inertiaTensorRotation;
            Vector3 tauLocal = Quaternion.Inverse(rot) * Vector3.Cross(rWorld, n);
            Vector3 I = rb.inertiaTensor;
            Vector3 alphaLocal = new Vector3(
                Mathf.Abs(I.x) > 1e-8f ? tauLocal.x / I.x : 0f,
                Mathf.Abs(I.y) > 1e-8f ? tauLocal.y / I.y : 0f,
                Mathf.Abs(I.z) > 1e-8f ? tauLocal.z / I.z : 0f);
            Vector3 alphaWorld = rot * alphaLocal;
            float angular = Vector3.Dot(Vector3.Cross(alphaWorld, rWorld), n);
            return Mathf.Max(0f, invMass + angular);
        }

        private void GetContactMaterials(Collider a, Collider b, out float restitution, out float friction)
        {
            PhysicsMaterial ma = a ? a.sharedMaterial : null;
            PhysicsMaterial mb = b ? b.sharedMaterial : null;

            float eA = ma ? ma.bounciness : collisionRestitution;
            float eB = mb ? mb.bounciness : collisionRestitution;
            float fA = ma ? ma.dynamicFriction : collisionFriction;
            float fB = mb ? mb.dynamicFriction : collisionFriction;

            PhysicsMaterialCombine bounceMode = DominantCombine(
                ma ? ma.bounceCombine : PhysicsMaterialCombine.Average,
                mb ? mb.bounceCombine : PhysicsMaterialCombine.Average);
            PhysicsMaterialCombine frictionMode = DominantCombine(
                ma ? ma.frictionCombine : PhysicsMaterialCombine.Average,
                mb ? mb.frictionCombine : PhysicsMaterialCombine.Average);

            restitution = Combine(eA, eB, bounceMode);
            friction = Combine(fA, fB, frictionMode);
        }

        private static PhysicsMaterialCombine DominantCombine(PhysicsMaterialCombine a, PhysicsMaterialCombine b)
        {
            return (PhysicsMaterialCombine)Mathf.Max((int)a, (int)b);
        }

        private static float Combine(float a, float b, PhysicsMaterialCombine mode)
        {
            return mode switch
            {
                PhysicsMaterialCombine.Multiply => a * b,
                PhysicsMaterialCombine.Minimum => Mathf.Min(a, b),
                PhysicsMaterialCombine.Maximum => Mathf.Max(a, b),
                _ => (a + b) * 0.5f
            };
        }

        private struct Contact
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Penetration;
            public float Restitution;
            public float Friction;
            public Vector3 RelativeVelocity;
            public Vector3 Impulse;
            public Collider ThisCollider;
            public Collider OtherCollider;
            public Rigidbody OtherBody;
            public PlaneRigidbody OtherPlane;
            /// <summary>Other plane exists but is not solving, so it takes no impulse and no correction.</summary>
            public bool OtherPlaneStatic;
        }

        private readonly struct ColliderPair : IEquatable<ColliderPair>
        {
            private readonly EntityId _a;
            private readonly EntityId _b;

            public ColliderPair(EntityId a, EntityId b)
            {
                _a = a;
                _b = b;
            }

            public bool Equals(ColliderPair other)
            {
                return (_a.Equals(other._a) && _b.Equals(other._b)) ||
                       (_a.Equals(other._b) && _b.Equals(other._a));
            }

            public override bool Equals(object obj) => obj is ColliderPair other && Equals(other);

            public override int GetHashCode() => _a.GetHashCode() ^ _b.GetHashCode();
        }

        private void RecordCollisionHits(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Contact c = _contacts[i];
                if (!c.OtherCollider)
                    continue;

                EntityId id = c.OtherCollider.GetEntityId();
                if (_hitsThisTick.TryGetValue(id, out PlaneCollision hit))
                {
                    hit.Impulse += c.Impulse;
                    hit.ContactCount++;
                    if (c.Penetration > -hit.Separation)
                    {
                        hit.Point = c.Point;
                        hit.Normal = c.Normal;
                        hit.Separation = -c.Penetration;
                        hit.ThisCollider = c.ThisCollider;
                    }
                    _hitsThisTick[id] = hit;
                }
                else
                {
                    Transform otherTransform = c.OtherCollider.transform;
                    _hitsThisTick[id] = new PlaneCollision
                    {
                        Collider = c.OtherCollider,
                        ThisCollider = c.ThisCollider,
                        Rigidbody = c.OtherBody,
                        PlaneBody = c.OtherPlane,
                        Transform = otherTransform,
                        GameObject = otherTransform.gameObject,
                        RelativeVelocity = c.RelativeVelocity,
                        Impulse = c.Impulse,
                        Point = c.Point,
                        Normal = c.Normal,
                        Separation = -c.Penetration,
                        ContactCount = 1
                    };
                }
            }
        }

        private void DispatchCollisionEvents()
        {
            // Snapshot first. A crash callback hides the wreck and calls SetSimulationEnabled(false),
            // which Clears these dictionaries; enumerating the live maps would throw.
            CopyHits(_hitsThisTick, _dispatchThis);
            CopyHits(_hitsLastTick, _dispatchLast);

            foreach (var kv in _dispatchThis)
            {
                if (_dispatchLast.ContainsKey(kv.Key))
                    InvokeCollisionEvent(CollisionStay, "OnPlaneCollisionStay", kv.Value);
                else
                    InvokeCollisionEvent(CollisionEnter, "OnPlaneCollisionEnter", kv.Value);
            }

            foreach (var kv in _dispatchLast)
            {
                if (!_dispatchThis.ContainsKey(kv.Key))
                    InvokeCollisionEvent(CollisionExit, "OnPlaneCollisionExit", kv.Value);
            }

            (_hitsLastTick, _hitsThisTick) = (_hitsThisTick, _hitsLastTick);
            _hitsThisTick.Clear();
        }

        private static void CopyHits(
            Dictionary<EntityId, PlaneCollision> source,
            Dictionary<EntityId, PlaneCollision> dest)
        {
            dest.Clear();
            foreach (var kv in source)
                dest[kv.Key] = kv.Value;
        }

        private void InvokeCollisionEvent(Action<PlaneCollision> evt, string message, PlaneCollision hit)
        {
            evt?.Invoke(hit);
            SendMessage(message, hit, SendMessageOptions.DontRequireReceiver);
        }

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, _rayBuffer, maxDistance, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < n; i++)
            {
                Collider col = _rayBuffer[i].collider;
                if (col == null || IsOwnCollider(col))
                    continue;
                if (_rayBuffer[i].distance < best)
                {
                    best = _rayBuffer[i].distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                hit = default;
                return false;
            }

            hit = _rayBuffer[bestIndex];
            return true;
        }
    }
}
