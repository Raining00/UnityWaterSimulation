using System;
using System.Collections.Generic;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("Water System/Bouyant Body")]
    [ExecuteAlways]
    public sealed class BouyantBody : MonoBehaviour
    {
        // For now directly resign which ocean this object belongs to in Unity Editor. This finally need to calculate which ocean this object belongs to by calculating.
        [SerializeField] OceanRenderer ocean;

        [Header("Buoyancy sample points")]
        [Tooltip("Number of candidate voxel centers along the body's local X/Y/Z bounds.")]
        [SerializeField] Vector3Int samplesPerAxis = new Vector3Int(3, 2, 3);
        [Tooltip("Allow trigger colliders to contribute to the buoyancy volume.")]
        [SerializeField] bool includeTriggerColliders;
        [Tooltip("World-space tolerance used when checking whether a voxel center is inside a collider.")]
        [SerializeField, Min(0.000001f)] float insideTolerance = 0.0001f;
        [SerializeField, HideInInspector] Vector3[] localSamplePoints = Array.Empty<Vector3>();
        [SerializeField, HideInInspector] Bounds localSamplingBounds;

        Rigidbody _rigidbody;
        Collider[] _colliders = Array.Empty<Collider>();

        public int SamplePointCount => localSamplePoints?.Length ?? 0;
        public IReadOnlyList<Vector3> LocalSamplePoints => localSamplePoints;

        void Awake()
        {
            CacheComponents();
            GenerateSamplePoints();
        }

        void Reset() => GenerateSamplePoints();

        void OnValidate()
        {
            samplesPerAxis.x = Mathf.Clamp(samplesPerAxis.x, 1, 16);
            samplesPerAxis.y = Mathf.Clamp(samplesPerAxis.y, 1, 16);
            samplesPerAxis.z = Mathf.Clamp(samplesPerAxis.z, 1, 16);
            insideTolerance = Mathf.Max(0.000001f, insideTolerance);
            GenerateSamplePoints();
        }

        void CacheComponents()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            var candidates = GetComponentsInChildren<Collider>(true);
            var owned = new List<Collider>(candidates.Length);
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.enabled) continue;
                if (!includeTriggerColliders && candidate.isTrigger) continue;

                // A nested Rigidbody owns its own colliders and must generate its own sample set.
                var attachedBody = candidate.attachedRigidbody;
                if (attachedBody != null && attachedBody != _rigidbody) continue;
                owned.Add(candidate);
            }
            _colliders = owned.ToArray();
        }

        /// <summary>
        /// Builds collider-backed voxel centers and stores them in this body's local space.
        /// This is an authoring/initialization operation, not something to call every physics step.
        /// </summary>
        [ContextMenu("Regenerate Buoyancy Sample Points")]
        public void GenerateSamplePoints()
        {
            CacheComponents();
            if (_colliders.Length == 0)
            {
                localSamplePoints = Array.Empty<Vector3>();
                localSamplingBounds = default;
                return;
            }

            if (!TryBuildLocalBounds(out localSamplingBounds))
            {
                localSamplePoints = Array.Empty<Vector3>();
                return;
            }

            int countX = Mathf.Max(1, samplesPerAxis.x);
            int countY = Mathf.Max(1, samplesPerAxis.y);
            int countZ = Mathf.Max(1, samplesPerAxis.z);
            Vector3 cellSize = new Vector3(
                localSamplingBounds.size.x / countX,
                localSamplingBounds.size.y / countY,
                localSamplingBounds.size.z / countZ);

            var points = new List<Vector3>(countX * countY * countZ);
            Vector3 minimum = localSamplingBounds.min;
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                Vector3 localPoint = minimum + new Vector3(
                    (x + 0.5f) * cellSize.x,
                    (y + 0.5f) * cellSize.y,
                    (z + 0.5f) * cellSize.z);
                if (IsInsideOwnedCollider(transform.TransformPoint(localPoint))) points.Add(localPoint);
            }

            // Thin or strongly concave shapes can miss every coarse voxel center. Keep one stable
            // fallback point so the component remains usable while the resolution is being tuned.
            if (points.Count == 0)
            {
                Vector3 worldPoint = _colliders[0].ClosestPoint(_colliders[0].bounds.center);
                points.Add(transform.InverseTransformPoint(worldPoint));
            }

            localSamplePoints = points.ToArray();
        }

        bool TryBuildLocalBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            foreach (var collider in _colliders)
            {
                Bounds worldBounds = collider.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                    if (!initialized)
                    {
                        bounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(localCorner);
                }
            }
            return initialized;
        }

        bool IsInsideOwnedCollider(Vector3 worldPoint)
        {
            float toleranceSquared = insideTolerance * insideTolerance;
            foreach (var collider in _colliders)
            {
                Vector3 closest = collider.ClosestPoint(worldPoint);
                if ((closest - worldPoint).sqrMagnitude <= toleranceSquared) return true;
            }
            return false;
        }

        public Vector3 GetWorldSamplePoint(int index)
        {
            if ((uint)index >= (uint)SamplePointCount) throw new ArgumentOutOfRangeException(nameof(index));
            return transform.TransformPoint(localSamplePoints[index]);
        }

        private void OnEnable()
        {
            if (ocean == null)
                return;

            var queries = ocean.SurfaceQueries;
            if (queries != null)
                queries.Register(this);
            else
                Debug.LogWarning($"{name} could not find OceanSurfaceQuerySystem.", this);
        }

        private void OnDisable()
        {
            if (ocean != null)
                ocean.SurfaceQueries?.Unregister(this);
        }

        void OnDrawGizmosSelected()
        {
            if (localSamplePoints == null || localSamplePoints.Length == 0) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.85f);
            float radius = Mathf.Max(0.015f, localSamplingBounds.size.magnitude * 0.0125f);
            foreach (var point in localSamplePoints) Gizmos.DrawSphere(point, radius);
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.25f);
            Gizmos.DrawWireCube(localSamplingBounds.center, localSamplingBounds.size);
            Gizmos.matrix = previous;
        }
    }
}
