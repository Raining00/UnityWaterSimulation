using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Clip-space triangle used to shade the analytical water-plane intersection outside the FFT root.
    /// It avoids kilometre-scale world triangles crossing the camera and far planes.
    /// </summary>
    public static class OceanHorizonMesh
    {
        public static Mesh Create()
        {
            var mesh=new Mesh
            {
                name="Ocean analytical horizon",
                hideFlags=HideFlags.HideAndDontSave,
                vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)},
                uv2=new[]{new Vector2(0,1),new Vector2(0,1),new Vector2(0,1)},
                triangles=new[]{0,1,2}
            };
            // The renderer provides an explicit world bound. Keep a non-zero local bound for API validation.
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one);
            return mesh;
        }
        public static float Extent(Camera camera,Vector3 center,float rootSize,float minimumDistance)
        {
            float viewRadius=camera.orthographic?camera.orthographicSize*Mathf.Sqrt(1+camera.aspect*camera.aspect)
                :camera.farClipPlane*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)*Mathf.Sqrt(1+camera.aspect*camera.aspect);
            float offset=new Vector2(camera.transform.position.x-center.x,camera.transform.position.z-center.z).magnitude;
            return Mathf.Max(Mathf.Max(rootSize,minimumDistance),offset+camera.farClipPlane+viewRadius);
        }
        public static bool Visible(Bounds bounds,Plane[] planes,bool infinite)
        {
            // Unity's plane order is left, right, bottom, top, near, far. The shader preserves water past far.
            int count=infinite?5:6;
            for(int i=0;i<count;i++)
            {
                var n=planes[i].normal;
                float radius=Vector3.Dot(bounds.extents,new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z)));
                if(planes[i].GetDistanceToPoint(bounds.center)+radius<0)return false;
            }
            return true;
        }
    }
}
