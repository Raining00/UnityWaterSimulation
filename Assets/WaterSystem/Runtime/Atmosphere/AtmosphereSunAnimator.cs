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
            // Quaternion.Angle (and Quaternion ==) treats small rotations as equal. At a slow day
            // speed this holds the sun, and the moon following it, for many frames before a jump.
            // Compare components instead, allowing the q/-q equivalence and native rounding noise.
            Quaternion current = transform.rotation;
            var previous = new Vector4(current.x, current.y, current.z, current.w);
            var next = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            if (Mathf.Min((previous - next).sqrMagnitude, (previous + next).sqrMagnitude) > 1e-14f)
                transform.rotation = rotation;
        }
        /// <summary>World direction towards the sun.</summary>
        public static Vector3 SunDirection(float hours, float latitude, float declination, float northYaw)
        {
            float hourAngle = (hours - 12) * 15 * Mathf.Deg2Rad;
            float lat = latitude * Mathf.Deg2Rad, dec = declination * Mathf.Deg2Rad;
            // Direct east/up/north coordinates avoid the azimuth division and acos singularity
            // when the sun crosses the zenith; the orbit is continuous through noon and midnight.
            float cosDec = Mathf.Cos(dec), sinDec = Mathf.Sin(dec), cosHour = Mathf.Cos(hourAngle);
            var local = new Vector3(-cosDec * Mathf.Sin(hourAngle),
                Mathf.Sin(lat) * sinDec + Mathf.Cos(lat) * cosDec * cosHour,
                Mathf.Cos(lat) * sinDec - Mathf.Sin(lat) * cosDec * cosHour);
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
