using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        [SerializeField] Vector3Int samplesPerAxis = new Vector3Int(3, 8, 3);
        [Tooltip("Allow trigger colliders to contribute to the buoyancy volume.")]
        [SerializeField] bool includeTriggerColliders;
        [Tooltip("World-space tolerance used when checking whether a voxel center is inside a collider.")]
        [SerializeField, Min(0.000001f)] float insideTolerance = 0.0001f;
        [Header("Buoyancy forces")]
        [Tooltip("Mass per cubic meter of displaced water. A value of 1000 approximates fresh water.")]
        [SerializeField, Min(0f)] float waterDensity = 1000f;
        [Tooltip("Velocity damping per second, scaled by displaced water mass. Helps the body settle instead of bouncing at the surface.")]
        [SerializeField, Min(0f)] float waterDrag = 4f;
        [Tooltip("Fraction of one voxel volume assigned to the single fallback point when no voxel center lies inside a collider.")]
        [SerializeField, Range(0f, 1f)] float fallbackVolumeFraction = 0.25f;
        [SerializeField, HideInInspector] Vector3[] localSamplePoints = Array.Empty<Vector3>();
        [SerializeField, HideInInspector] Bounds localSamplingBounds;
        [SerializeField, HideInInspector] Vector3 localSampleCellSize;
        [ReadOnly(true), SerializeField] float localSampleVolume;

        Rigidbody _rigidbody;
        Collider[] _colliders = Array.Empty<Collider>();
        OceanSurfaceQueryResult[] latestSurfaceResults = Array.Empty<OceanSurfaceQueryResult>();
        private float submergedFractions;
        int queryVersion;

        public int SamplePointCount => localSamplePoints?.Length ?? 0;
        public IReadOnlyList<Vector3> LocalSamplePoints => localSamplePoints;
        internal OceanRenderer Ocean => ocean;
        internal int QueryVersion => queryVersion;
        public long LatestSurfaceRequestId { get; private set; } = -1;
        public double LatestSurfaceSimulationTime { get; private set; }

        /// <summary>Returns the latest completed GPU samples in local sample-point order.</summary>
        public bool TryGetSurfaceResults(out IReadOnlyList<OceanSurfaceQueryResult> results)
        {
            results = latestSurfaceResults;
            return LatestSurfaceRequestId >= 0 && latestSurfaceResults.Length == SamplePointCount;
        }

        internal void AcceptSurfaceResults(OceanSurfaceQueryResult[] source, int start, int count,
            long requestId, double simulationTime, int expectedVersion)
        {
            if (queryVersion != expectedVersion || count != SamplePointCount || requestId <= LatestSurfaceRequestId)
                return;
            if (latestSurfaceResults.Length != count)
                latestSurfaceResults = new OceanSurfaceQueryResult[count];
            Array.Copy(source, start, latestSurfaceResults, 0, count);
            LatestSurfaceRequestId = requestId;
            LatestSurfaceSimulationTime = simulationTime;
        }

        void InvalidateSurfaceResults()
        {
            unchecked { queryVersion++; }
            latestSurfaceResults = Array.Empty<OceanSurfaceQueryResult>();
            LatestSurfaceRequestId = -1;
            LatestSurfaceSimulationTime = 0;
        }

        internal void ClearSurfaceResults() => InvalidateSurfaceResults();

        void Reset() => GenerateSamplePoints();

        void OnValidate()
        {
            samplesPerAxis.x = Mathf.Clamp(samplesPerAxis.x, 1, 16);
            samplesPerAxis.y = Mathf.Clamp(samplesPerAxis.y, 1, 16);
            samplesPerAxis.z = Mathf.Clamp(samplesPerAxis.z, 1, 16);
            insideTolerance = Mathf.Max(0.000001f, insideTolerance);
            waterDensity = Mathf.Max(0f, waterDensity);
            waterDrag = Mathf.Max(0f, waterDrag);
            fallbackVolumeFraction = Mathf.Clamp01(fallbackVolumeFraction);
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

        void FixedUpdate()
        {
            if (!Application.isPlaying || _rigidbody == null || _rigidbody.isKinematic)
                return;
            if (!TryGetSurfaceResults(out var results) || results.Count == 0)
                return;

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            float worldSampleVolume = localSampleVolume * Mathf.Abs(localToWorld.determinant);
            Debug.Log("WorldSampleVolume: " + worldSampleVolume);
            Vector3 gravity = Physics.gravity;
            if (worldSampleVolume <= 0f || waterDensity <= 0f || gravity.sqrMagnitude <= 0f)
                return;

            // Project the voxel's three transformed edges onto world up. This keeps
            // partial submersion meaningful for rotated and non-uniformly scaled bodies.
            float worldSampleHeight =
                Mathf.Abs(localToWorld.MultiplyVector(new Vector3(localSampleCellSize.x, 0f, 0f)).y) +
                Mathf.Abs(localToWorld.MultiplyVector(new Vector3(0f, localSampleCellSize.y, 0f)).y) +
                Mathf.Abs(localToWorld.MultiplyVector(new Vector3(0f, 0f, localSampleCellSize.z)).y);
            if (worldSampleHeight <= 0f)
                return;
            
            // Bound the damping contribution so it cannot cancel more than the
            // body's linear momentum in one physics step, even for a light body.
            float maxDampingPerPoint = _rigidbody.mass /
                                       (Mathf.Max(Time.fixedDeltaTime, 0.000001f) * results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                Vector3 point = localToWorld.MultiplyPoint3x4(localSamplePoints[i]);
                float depth = results[i].Position.y - point.y;
                float submergedFraction = Mathf.Clamp01(0.5f + depth / worldSampleHeight);
                // Archimedes' force: density * submerged volume * -gravity.
                // ForceMode.Force lets the Rigidbody mass determine acceleration.
                Vector3 buoyancy = -gravity * (waterDensity * worldSampleVolume * submergedFraction);
                float damping = Mathf.Min(
                    waterDensity * worldSampleVolume * submergedFraction * waterDrag,
                    maxDampingPerPoint);
                Vector3 waterResistance = -_rigidbody.GetPointVelocity(point) * damping;
                _rigidbody.AddForceAtPosition(buoyancy + waterResistance, point, ForceMode.Force);
            }
        }

        /// <summary>
        /// Builds collider-backed voxel centers and stores them in this body's local space.
        /// This is an authoring/initialization operation, not something to call every physics step.
        /// </summary>
        [ContextMenu("Regenerate Buoyancy Sample Points")]
        public void GenerateSamplePoints()
        {
            InvalidateSurfaceResults();
            CacheComponents();
            if (_colliders.Length == 0)
            {
                localSamplePoints = Array.Empty<Vector3>();
                localSamplingBounds = default;
                localSampleCellSize = default;
                localSampleVolume = 0f;
                return;
            }

            if (!TryBuildLocalBounds(out localSamplingBounds))
            {
                localSamplePoints = Array.Empty<Vector3>();
                localSampleCellSize = default;
                localSampleVolume = 0f;
                return;
            }

            int countX = Mathf.Max(1, samplesPerAxis.x);
            int countY = Mathf.Max(1, samplesPerAxis.y);
            int countZ = Mathf.Max(1, samplesPerAxis.z);
            Vector3 cellSize = new Vector3(
                localSamplingBounds.size.x / countX,
                localSamplingBounds.size.y / countY,
                localSamplingBounds.size.z / countZ);
            localSampleCellSize = cellSize;
            localSampleVolume = cellSize.x * cellSize.y * cellSize.z;

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
                // The collider may occupy only a thin fraction of this cell. Avoid
                // assigning the entire voxel volume to the synthetic fallback point.
                localSampleVolume *= fallbackVolumeFraction;
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
            // Regenerate at activation: disabled scene objects can have stale or
            // zeroed serialized sample data after their colliders change in the Editor.
            GenerateSamplePoints();
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
            InvalidateSurfaceResults();
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

            if (TryGetSurfaceResults(out var results))
            {
                foreach (var result in results)
                {
                    Vector3 surface = result.Position;
                    Vector3 sample = surface - Vector3.up * result.SignedDepth;

                    Gizmos.color = result.SignedDepth > 0f ? Color.cyan : Color.yellow;
                    Gizmos.DrawSphere(surface, radius * 0.6f);
                    Gizmos.DrawLine(sample, surface);

                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(surface, surface + result.Normal * radius * 2f);
                }
            }
        }
    }
}
