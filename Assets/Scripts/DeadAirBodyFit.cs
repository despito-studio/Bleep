using UnityEngine;

namespace DeadAir
{
    /// <summary>Authored face landmarks, with measurements for the older imported cast as a fallback.</summary>
    public static class DeadAirBodyFit
    {
        public static Transform FindLandmark(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        public static Vector3 SourceEye(string role, Transform root)
        {
            var eye = FindLandmark(root, "EyeSocket");
            return eye != null ? root.InverseTransformPoint(eye.position) : SourceEye(role);
        }

        public static float TorsoFront(string role, Transform root)
        {
            var front = FindLandmark(root, "TorsoFront");
            return front != null ? root.InverseTransformPoint(front.position).z : TorsoFront(role);
        }

        public static Vector3 SourceEye(string role) => role == "operator" ? new Vector3(0, 1.665f, .067f) :
            role == "journalist" ? new Vector3(.066f, 1.752f, .263f) :
            role == "engineer" ? new Vector3(.005f, 1.655f, .145f) : new Vector3(.005f, 1.884f, .258f);

        public static float TorsoFront(string role) => role == "operator" ? .134f :
            role == "journalist" ? .362f : role == "engineer" ? .345f : .295f;
    }
}
