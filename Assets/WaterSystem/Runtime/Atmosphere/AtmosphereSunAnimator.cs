using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Optional day cycle for the sun light: a simple solar position model driven by time of day and latitude.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("Water System/Atmosphere Sun Animator")]
    public sealed class AtmosphereSunAnimator : MonoBehaviour
    {
        [Range(0, 24)] public float TimeOfDay = 15;
        [Range(-90, 90)] public float Latitude = 35;
        [Tooltip("Solar declination in degrees: 0 at the equinoxes, +23.4 at the June solstice.")]
        [Range(-23.44f, 23.44f)] public float Declination;
        [Tooltip("World yaw (degrees) of geographic north.")]
        public float NorthYaw;
        [Tooltip("Hours of day per real second while playing. 0 keeps the sun still.")]
        [Range(0, 2)] public float Speed;

        [SerializeField, HideInInspector] bool matched;

        void Update()
        {
            if (Application.isPlaying && Speed > 0) TimeOfDay = Mathf.Repeat(TimeOfDay + Time.deltaTime * Speed, 24);
            Apply();
        }
        void Reset() => MatchCurrentRotation();
        // OnValidate also runs when the component is added: adopt the light's existing direction instead of re-aiming it.
        void OnValidate()
        {
            if (matched) Apply();
            else MatchCurrentRotation();
        }
        public void Apply()
        {
            Vector3 direction = SunDirection(TimeOfDay, Latitude, Declination, NorthYaw);
            var rotation = Quaternion.LookRotation(-direction, Vector3.up);
            // Only touch the transform when the result changes, so idle edit-mode updates leave the scene alone.
            if (Quaternion.Angle(transform.rotation, rotation) > 0.001f) transform.rotation = rotation;
        }
        /// <summary>World direction towards the sun.</summary>
        public static Vector3 SunDirection(float hours, float latitude, float declination, float northYaw)
        {
            float hourAngle = (hours - 12) * 15 * Mathf.Deg2Rad;
            float lat = latitude * Mathf.Deg2Rad, dec = declination * Mathf.Deg2Rad;
            float sinElevation = Mathf.Clamp(Mathf.Sin(lat) * Mathf.Sin(dec) + Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(hourAngle), -1, 1);
            float elevation = Mathf.Asin(sinElevation);
            float cosAzimuth = (Mathf.Sin(dec) - sinElevation * Mathf.Sin(lat)) / Mathf.Max(1e-5f, Mathf.Cos(elevation) * Mathf.Cos(lat));
            float azimuth = Mathf.Acos(Mathf.Clamp(cosAzimuth, -1, 1));
            if (hourAngle > 0) azimuth = 2 * Mathf.PI - azimuth;
            var local = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), sinElevation, Mathf.Cos(azimuth) * Mathf.Cos(elevation));
            return Quaternion.Euler(0, northYaw, 0) * local;
        }
        /// <summary>Choose an afternoon time, latitude and north yaw that reproduce the light's current direction.</summary>
        public void MatchCurrentRotation() => MatchDirection(-transform.forward);
        /// <summary>Choose an afternoon time, latitude and north yaw that reproduce a world direction towards the sun.</summary>
        public void MatchDirection(Vector3 current)
        {
            current = current.sqrMagnitude > 1e-6f ? current.normalized : Vector3.up;
            matched = true;
            float sinElevation = Mathf.Clamp(current.y, -0.999f, 0.999f);
            Declination = 0;
            if (Mathf.Abs(sinElevation) > Mathf.Cos(Latitude * Mathf.Deg2Rad) - 0.02f)
                Latitude = Mathf.Acos(Mathf.Min(1, Mathf.Abs(sinElevation) + 0.02f)) * Mathf.Rad2Deg;
            float cosHour = Mathf.Clamp(sinElevation / Mathf.Cos(Latitude * Mathf.Deg2Rad), -1, 1);
            TimeOfDay = 12 + Mathf.Acos(cosHour) * Mathf.Rad2Deg / 15;
            NorthYaw = 0;
            Vector3 model = SunDirection(TimeOfDay, Latitude, Declination, 0);
            NorthYaw = (Mathf.Atan2(current.x, current.z) - Mathf.Atan2(model.x, model.z)) * Mathf.Rad2Deg;
            Apply();
        }
    }
}
