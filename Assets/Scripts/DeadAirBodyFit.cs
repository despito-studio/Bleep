using UnityEngine;

namespace DeadAir
{
    /// <summary>Face landmarks measured on the supplied source models, in avatar metres.</summary>
    public static class DeadAirBodyFit
    {
        public static Vector3 SourceEye(string role) => role == "operator" ? new Vector3(0, 1.665f, .067f) :
            role == "journalist" ? new Vector3(.066f, 1.752f, .263f) :
            role == "engineer" ? new Vector3(.005f, 1.655f, .145f) : new Vector3(.005f, 1.884f, .258f);

        public static float TorsoFront(string role) => role == "operator" ? .134f :
            role == "journalist" ? .362f : role == "engineer" ? .345f : .295f;
    }
}
