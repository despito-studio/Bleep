using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // Room groups replace the fixed, one-storey coordinate sectors. Structural
    // surfaces stay enabled so they continue to occlude rooms and cast shadows.
    public sealed class DeadAirCulling
    {
        sealed class Sector
        {
            public Bounds bounds;
            public Renderer[] renderers;
            public Light[] lights;
            public bool visible = true;
        }
        readonly List<Sector> sectors = new List<Sector>();
        readonly Plane[] planes = new Plane[6];
        float nextUpdate;

        public DeadAirCulling(WorldData world, Transform station, Transform exterior,
            HashSet<Transform> dynamicRoots, Dictionary<string, DeadAirHingedDoor> doors)
        {
            if (station == null) return;
            foreach (var group in station.GetComponentsInChildren<Transform>(true))
            {
                if (!group.name.StartsWith("sector:")) continue;
                var renderers = group.GetComponentsInChildren<Renderer>(true);
                var lights = group.GetComponentsInChildren<Light>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                foreach (var light in lights) bounds.Encapsulate(light.transform.position);
                // Account for moving door leaves and illumination across doorways.
                bounds.Expand(new Vector3(4, 1, 4));
                sectors.Add(new Sector { bounds = bounds, renderers = renderers, lights = lights });
            }
        }

        public void Update(RoomState state, Vector3 cameraPosition)
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .1f;
            var camera = Camera.main;
            if (camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var sector in sectors)
            {
                float distanceSquared = sector.bounds.SqrDistance(cameraPosition);
                bool visible = distanceSquared < 9 || distanceSquared < 2500 && GeometryUtility.TestPlanesAABB(planes, sector.bounds);
                if (sector.visible != visible)
                {
                    sector.visible = visible;
                    foreach (var renderer in sector.renderers) if (renderer != null) renderer.enabled = visible;
                }
                // Nearby lamps can light the view even when their room is behind it.
                foreach (var light in sector.lights) if (light != null) light.enabled = distanceSquared < 625 || visible;
            }
        }
    }
}
