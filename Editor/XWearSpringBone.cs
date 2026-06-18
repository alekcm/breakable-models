// XWearSpringBone.cs
// Lightweight Verlet-based secondary physics for VRoid .xwear clothing
// (hair, ribbons, laces, dangling accessories).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace XWearImporter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("XWear/Spring Bone")]
    public class XWearSpringBone : MonoBehaviour
    {
        // --- Parameters from .xwear ------------------------------------------
        [Header("Pull / return force (0..1)")]
        [Range(0f, 1f)] public float pull = 0.5f;

        [Header("Per-segment stiffness (0..1)")]
        [Range(0f, 1f)] public float stiffness = 0.5f;

        [Header("Spring damping (0..1)")]
        [Range(0f, 1f)] public float spring = 0.5f;

        [Header("Gravity multiplier (0..1)")]
        [Range(0f, 1f)] public float gravity = 0f;

        [Header("Immobile threshold: chain freezes when displacement < this")]
        [Range(0f, 1f)] public float immobile = 0f;

        [Header("Maximum stretch (0..1)")]
        [Range(0f, 1f)] public float maxStretch = 0f;

        [Header("Maximum squish (0..1)")]
        [Range(0f, 1f)] public float maxSquish = 0f;

        [Header("Integration: 0=Verlet, 1=Semi-implicit Euler")]
        public int integrationType = 1;

        [Header("Maximum length change per segment per frame")]
        public float maxDeltaPerFrame = 0.05f;

        [Header("Bone collision radius")]
        public float radius = 0.01f;

        // --- Collision parameters --------------------------------------------
        [Header("Collisions")]
        [Tooltip("Enable collision detection against scene Colliders (body, walls, etc.).")]
        public bool useCollisions = true;

        [Tooltip("Radius around each particle used for the OverlapSphere query.")]
        public float collisionQueryRadius = 0.05f;

        [Tooltip("Layers to consider for collision (defaults to Default).")]
        public LayerMask collisionLayers = ~0;

        // --- Internal state ----------------------------------------------------
        private struct Particle
        {
            public Transform bone;
            public Vector3   restPosLocal;
            public Vector3   prevPosWorld;
            public Vector3   currentPosWorld;
        }

        private Particle[] _particles;
        private float[]    _segmentLengths;
        private bool       _initialized;
        private Transform  _rootBone;

        private readonly Collider[] _overlapBuf = new Collider[32];

        public void Initialize(Transform rootBone)
        {
            _rootBone = rootBone;
            if (_rootBone == null) { _initialized = false; return; }

            var list = new List<Particle>();
            CollectBones(_rootBone, list);
            _particles = list.ToArray();

            _segmentLengths = new float[_particles.Length];
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                if (i == 0)
                {
                    _segmentLengths[i] = 0f;
                    p.restPosLocal = p.bone.localPosition;
                }
                else
                {
                    var parent = _particles[i - 1].bone;
                    p.restPosLocal = parent.InverseTransformPoint(p.bone.position);
                    _segmentLengths[i] = Vector3.Distance(parent.position, p.bone.position);
                }
                p.currentPosWorld  = p.bone.position;
                p.prevPosWorld     = p.bone.position;
                _particles[i] = p;
            }
            _initialized = true;
        }

        void CollectBones(Transform t, List<Particle> list)
        {
            if (t != _rootBone && t.GetComponent<XWearSpringBone>() != null) return;
            list.Add(new Particle { bone = t });
            for (int i = 0; i < t.childCount; i++)
                CollectBones(t.GetChild(i), list);
        }

        void FixedUpdate()
        {
            if (!_initialized || _particles == null || _particles.Length == 0) return;
            if (immobile > 0f && !NeedsUpdate()) return;

            float dt = Time.fixedDeltaTime;

            // 1. Integrate
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                if (i == 0)
                {
                    p.currentPosWorld = p.bone.position;
                    p.prevPosWorld    = p.bone.position;
                    _particles[i] = p;
                    continue;
                }
                Vector3 velocity = p.currentPosWorld - p.prevPosWorld;
                Vector3 force = Physics.gravity * gravity + ReturnForce(i, dt);

                Vector3 newPos = p.currentPosWorld + velocity * (1f - spring) + force * (dt * dt);

                Vector3 delta = newPos - p.currentPosWorld;
                if (delta.magnitude > maxDeltaPerFrame) delta = delta.normalized * maxDeltaPerFrame;
                newPos = p.currentPosWorld + delta;

                p.prevPosWorld    = p.currentPosWorld;
                p.currentPosWorld = newPos;
                _particles[i] = p;
            }

            // 2. Constraint pass
            const int iterations = 4;
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 1; i < _particles.Length; i++)
                {
                    var parent = _particles[i - 1];
                    var child  = _particles[i];
                    Vector3 delta = child.currentPosWorld - parent.currentPosWorld;
                    float currentLen = delta.magnitude;
                    if (currentLen < 1e-5f) continue;
                    float restLen = _segmentLengths[i];
                    float maxLen = restLen * (1f + maxStretch);
                    float minLen = restLen * Mathf.Max(1e-3f, 1f - maxSquish);
                    float targetLen = Mathf.Clamp(currentLen, minLen, maxLen);
                    Vector3 correction = delta * (targetLen / currentLen - 1f) * 0.5f;
                    if (i == 1)
                        child.currentPosWorld -= correction * 2f;
                    else
                    {
                        parent.currentPosWorld += correction;
                        child.currentPosWorld  -= correction;
                    }
                    _particles[i - 1] = parent;
                    _particles[i]     = child;
                }
            }

            // 3. Collision pass
            if (useCollisions)
                ResolveCollisions();

            // 4. Apply back to Transforms
            for (int i = 1; i < _particles.Length; i++)
            {
                var p = _particles[i];
                var parent = _particles[i - 1].bone;
                p.bone.position = parent.TransformPoint(p.restPosLocal);
            }
        }

        void ResolveCollisions()
        {
            float queryRadius = Mathf.Max(collisionQueryRadius, 0.001f);
            for (int i = 1; i < _particles.Length; i++)
            {
                var p = _particles[i];

                int hits = Physics.OverlapSphereNonAlloc(
                    p.currentPosWorld, queryRadius + this.radius,
                    _overlapBuf, collisionLayers, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hits; h++)
                {
                    Collider col = _overlapBuf[h];
                    if (col == null) continue;

                    Vector3 closest = col.ClosestPoint(p.currentPosWorld);
                    Vector3 delta = p.currentPosWorld - closest;

                    float colRadius = GetColliderRadius(col);
                    float minDist   = colRadius + this.radius;

                    float dist = delta.magnitude;
                    if (dist < minDist && dist > 1e-5f)
                    {
                        p.currentPosWorld = closest + (delta / dist) * minDist;
                    }
                    else if (dist <= 1e-5f)
                    {
                        p.currentPosWorld = closest + Vector3.up * minDist;
                    }
                }
                _particles[i] = p;
            }
        }

        static float GetColliderRadius(Collider c)
        {
            switch (c)
            {
                case SphereCollider sc: return sc.radius * Mathf.Max(
                    sc.transform.lossyScale.x,
                    sc.transform.lossyScale.y,
                    sc.transform.lossyScale.z);
                case CapsuleCollider cc: return cc.radius * Mathf.Max(
                    cc.transform.lossyScale.x,
                    cc.transform.lossyScale.y,
                    cc.transform.lossyScale.z);
                case BoxCollider bc: return 0.5f * Mathf.Max(
                    bc.size.x * Mathf.Abs(bc.transform.lossyScale.x),
                    bc.size.y * Mathf.Abs(bc.transform.lossyScale.y),
                    bc.size.z * Mathf.Abs(bc.transform.lossyScale.z));
                case MeshCollider mc: return 0.5f * Mathf.Max(
                    mc.bounds.size.x, mc.bounds.size.y, mc.bounds.size.z);
                default: return 0f;
            }
        }

        Vector3 ReturnForce(int i, float dt)
        {
            var p = _particles[i];
            var parent = _particles[i - 1].bone;
            Vector3 restWorld = parent.TransformPoint(p.restPosLocal);
            Vector3 dir = restWorld - p.currentPosWorld;
            return dir * (pull + stiffness) * 10f;
        }

        bool NeedsUpdate()
        {
            for (int i = 1; i < _particles.Length; i++)
            {
                if ((_particles[i].currentPosWorld - _particles[i].bone.position).sqrMagnitude
                    > immobile * immobile) return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            if (_rootBone == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            for (int i = 1; i < _particles.Length; i++)
            {
                Gizmos.DrawWireSphere(_particles[i].bone.position, this.radius);
            }
        }
    }
}
