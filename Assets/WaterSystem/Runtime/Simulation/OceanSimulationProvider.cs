using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    public readonly struct OceanSimulationContext
    {
        public readonly OceanRenderer Ocean;
        public OceanSimulationContext(OceanRenderer ocean) { Ocean = ocean; }
        public float SeaLevel => Ocean.transform.position.y;
    }

    public readonly struct OceanSimulationFrame
    {
        public readonly double Time;
        public readonly float DeltaTime;
        public readonly int FrameIndex;
        public OceanSimulationFrame(double time, float deltaTime, int frameIndex)
        { Time = time; DeltaTime = deltaTime; FrameIndex = frameIndex; }
    }

    /// <summary>
    /// Optional FFT adapter. One provider instance belongs to one ocean.
    /// Initialize allocates spectra/textures; RecordSimulation records GPU work once per rendered frame;
    /// BindResources supplies textures/parameters per camera; Release frees all owned resources.
    /// Implementations must tolerate Release after partial initialization. No FFT is required by the geometry.
    /// </summary>
    public abstract class OceanSimulationProvider : MonoBehaviour
    {
        [Tooltip("Explicitly opt in only when the provider supports edit-mode resource allocation and updates.")]
        public bool RunInEditMode;

        // X = maximum horizontal displacement in either X/Z; Y = maximum absolute vertical displacement.
        public virtual Vector2 MaximumDisplacement => Vector2.zero;
        public abstract void Initialize(in OceanSimulationContext context);
        public abstract void RecordSimulation(CommandBuffer commands, in OceanSimulationFrame frame);
        public abstract void BindResources(MaterialPropertyBlock properties, Camera camera);
        public abstract void Release();
    }
}
