using UnityEngine;

namespace WaterSystem.Ocean
{
    // Both voxel bodies and hull sections use the same FFT query/readback batch.
    internal interface IOceanSurfaceQueryClient
    {
        MonoBehaviour QueryBehaviour { get; }
        OceanRenderer Ocean { get; }
        int SamplePointCount { get; }
        int QueryVersion { get; }
        Vector3 GetWorldSamplePoint(int index);
        void AcceptSurfaceResults(OceanSurfaceQueryResult[] source, int start, int count,
            long requestId, double simulationTime, int expectedVersion);
        void ClearSurfaceResults();
    }
}
