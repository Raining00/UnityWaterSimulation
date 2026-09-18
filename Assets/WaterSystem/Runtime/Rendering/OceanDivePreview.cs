using UnityEngine;

namespace WaterSystem.Ocean
{
    [AddComponentMenu("Water System/Demo/Dive Preview")]
    public sealed class OceanDivePreview : MonoBehaviour
    {
        [Tooltip("Demo only: disable this component to position the camera manually.")]
        public bool Animate = true;
        public float SeaLevel;
        [Min(1)] public float Period = 20;
        public Vector2 HeightRange = new Vector2(-6,3);
        Vector3 start;
        void OnEnable() => start=transform.position;
        void Update()
        {
            if (!Animate) return;
            float phase=(float)Time.timeAsDouble*2*Mathf.PI/Mathf.Max(1,Period);
            transform.position=new Vector3(start.x,SeaLevel+Mathf.Lerp(HeightRange.x,HeightRange.y,0.5f+0.5f*Mathf.Sin(phase)),start.z);
            transform.rotation=Quaternion.Euler(Mathf.Lerp(-55,15,0.5f+0.5f*Mathf.Sin(phase*0.5f)),0,0);
        }
    }
}
