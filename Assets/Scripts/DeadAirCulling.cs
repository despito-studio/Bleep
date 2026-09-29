using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirCulling
    {
        sealed class Sector
        {
            public string name;
            public MeshRenderer[] renderers;
            public Light[] lights;
            public bool visible = true;

            public void SetVisible(bool isVisible)
            {
                if (visible == isVisible) return;
                visible = isVisible;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].enabled = isVisible;
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null) lights[i].enabled = isVisible;
            }
        }

        readonly Sector[] sectors;
        readonly Dictionary<string, Sector> sectorLookup = new Dictionary<string, Sector>();
        readonly HashSet<string> visibleSet = new HashSet<string>();
        string lastPlayerSector;

        public DeadAirCulling(Transform station, Transform exterior, HashSet<Transform> dynamicRoots)
        {
            var sectorRenderers = new Dictionary<string, List<MeshRenderer>>();
            var sectorLights = new Dictionary<string, List<Light>>();
            var sectorStaticObjects = new Dictionary<string, List<GameObject>>();

            string[] sectorNames = new string[] {
                "Exterior", "StationShell",
                "Corridor_Front", "Corridor_Mid1", "Corridor_Mid2", "Corridor_Deep",
                "Archive", "Apparatus", "Generator", "Studio04", "Vent", "Server", "TechWarehouse", "Dispatcher"
            };

            foreach (var s in sectorNames)
            {
                sectorRenderers[s] = new List<MeshRenderer>();
                sectorLights[s] = new List<Light>();
                sectorStaticObjects[s] = new List<GameObject>();
            }

            // 1. Exterior renderers and lights
            if (exterior != null)
            {
                foreach (var r in exterior.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.transform.position.y > 20f && r.gameObject.name == "Mesh")
                    {
                        r.gameObject.SetActive(false);
                        continue;
                    }
                    sectorRenderers["Exterior"].Add(r);
                    if (!IsDynamic(r.transform, exterior, dynamicRoots))
                        sectorStaticObjects["Exterior"].Add(r.gameObject);
                }
                foreach (var l in exterior.GetComponentsInChildren<Light>(true))
                {
                    sectorLights["Exterior"].Add(l);
                }
            }

            // 2. Station renderers and lights
            if (station != null)
            {
                foreach (var r in station.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var sz = r.bounds.size;
                    var c = r.bounds.center;
                    string sec;
                    if (sz.x > 15f || sz.z > 15f || c.z > 5.5f)
                    {
                        sec = "StationShell";
                    }
                    else if (c.x >= -2.5f && c.x <= 2.5f)
                    {
                        if (c.z >= -9f) sec = "Corridor_Front";
                        else if (c.z >= -24f) sec = "Corridor_Mid1";
                        else if (c.z >= -39f) sec = "Corridor_Mid2";
                        else sec = "Corridor_Deep";
                    }
                    else if (c.x > 2.5f)
                    {
                        if (c.z >= -9f) sec = "Archive";
                        else if (c.z >= -24f) sec = "Generator";
                        else if (c.z >= -39f) sec = "Vent";
                        else sec = "TechWarehouse";
                    }
                    else
                    {
                        if (c.z >= -9f) sec = "Apparatus";
                        else if (c.z >= -24f) sec = "Studio04";
                        else if (c.z >= -39f) sec = "Server";
                        else sec = "Dispatcher";
                    }

                    sectorRenderers[sec].Add(r);
                    if (!IsDynamic(r.transform, station, dynamicRoots))
                        sectorStaticObjects[sec].Add(r.gameObject);
                }

                foreach (var l in station.GetComponentsInChildren<Light>(true))
                {
                    var p = l.transform.position;
                    string sec;
                    if (p.x >= -2.5f && p.x <= 2.5f)
                    {
                        if (p.z >= -9f) sec = "Corridor_Front";
                        else if (p.z >= -24f) sec = "Corridor_Mid1";
                        else if (p.z >= -39f) sec = "Corridor_Mid2";
                        else sec = "Corridor_Deep";
                    }
                    else if (p.x > 2.5f)
                    {
                        if (p.z >= -9f) sec = "Archive";
                        else if (p.z >= -24f) sec = "Generator";
                        else if (p.z >= -39f) sec = "Vent";
                        else sec = "TechWarehouse";
                    }
                    else
                    {
                        if (p.z >= -9f) sec = "Apparatus";
                        else if (p.z >= -24f) sec = "Studio04";
                        else if (p.z >= -39f) sec = "Server";
                        else sec = "Dispatcher";
                    }
                    sectorLights[sec].Add(l);
                }
            }

            var sectorList = new List<Sector>();
            foreach (var s in sectorNames)
            {
                var sector = new Sector
                {
                    name = s,
                    renderers = sectorRenderers[s].ToArray(),
                    lights = sectorLights[s].ToArray(),
                    visible = true
                };
                sectorList.Add(sector);
                sectorLookup[s] = sector;
            }

            sectors = sectorList.ToArray();
        }

        static bool IsDynamic(Transform t, Transform root, HashSet<Transform> dynamicRoots)
        {
            if (dynamicRoots == null) return false;
            for (var curr = t; curr != null && curr != root; curr = curr.parent)
            {
                if (dynamicRoots.Contains(curr)) return true;
            }
            return false;
        }

        public void Update(RoomState state, Vector3 camPos)
        {
            string currentSector = ResolveSector(camPos);
            visibleSet.Clear();

            // Station shell is always visible when near the outside or in front corridor
            if (camPos.z >= -9f)
            {
                visibleSet.Add("StationShell");
                visibleSet.Add("Exterior");
            }

            bool doorArchive = state != null && state.Door("door-archive") != null && state.Door("door-archive").open;
            bool doorGenerator = state != null && state.Door("door-generator") != null && state.Door("door-generator").open;
            bool doorStudio = state != null && state.Door("door-studio") != null && state.Door("door-studio").open;
            bool doorVent = state != null && state.Door("door-vent") != null && state.Door("door-vent").open;
            bool doorServer = state != null && state.Door("door-server") != null && state.Door("door-server").open;
            bool doorTech = state != null && state.Door("door-tech") != null && state.Door("door-tech").open;
            bool doorDispatch = state != null && state.Door("door-dispatch") != null && state.Door("door-dispatch").open;

            switch (currentSector)
            {
                case "Exterior":
                    visibleSet.Add("Exterior");
                    visibleSet.Add("StationShell");
                    visibleSet.Add("Corridor_Front");
                    visibleSet.Add("Apparatus");
                    if (doorArchive) visibleSet.Add("Archive");
                    break;

                case "Corridor_Front":
                    visibleSet.Add("Exterior");
                    visibleSet.Add("StationShell");
                    visibleSet.Add("Corridor_Front");
                    visibleSet.Add("Corridor_Mid1");
                    visibleSet.Add("Apparatus");
                    if (doorArchive) visibleSet.Add("Archive");
                    break;

                case "Corridor_Mid1":
                    visibleSet.Add("Corridor_Front");
                    visibleSet.Add("Corridor_Mid1");
                    visibleSet.Add("Corridor_Mid2");
                    if (doorGenerator) visibleSet.Add("Generator");
                    if (doorStudio) visibleSet.Add("Studio04");
                    break;

                case "Corridor_Mid2":
                    visibleSet.Add("Corridor_Mid1");
                    visibleSet.Add("Corridor_Mid2");
                    visibleSet.Add("Corridor_Deep");
                    if (doorVent) visibleSet.Add("Vent");
                    if (doorServer) visibleSet.Add("Server");
                    break;

                case "Corridor_Deep":
                    visibleSet.Add("Corridor_Mid2");
                    visibleSet.Add("Corridor_Deep");
                    if (doorTech) visibleSet.Add("TechWarehouse");
                    if (doorDispatch) visibleSet.Add("Dispatcher");
                    break;

                case "Archive":
                    visibleSet.Add("Archive");
                    if (doorArchive)
                    {
                        visibleSet.Add("Corridor_Front");
                        visibleSet.Add("Apparatus");
                    }
                    break;

                case "Apparatus":
                    visibleSet.Add("Apparatus");
                    visibleSet.Add("Corridor_Front");
                    visibleSet.Add("StationShell");
                    if (doorArchive) visibleSet.Add("Archive");
                    break;

                case "Generator":
                    visibleSet.Add("Generator");
                    if (doorGenerator) visibleSet.Add("Corridor_Mid1");
                    break;

                case "Studio04":
                    visibleSet.Add("Studio04");
                    if (doorStudio) visibleSet.Add("Corridor_Mid1");
                    break;

                case "Vent":
                    visibleSet.Add("Vent");
                    if (doorVent) visibleSet.Add("Corridor_Mid2");
                    break;

                case "Server":
                    visibleSet.Add("Server");
                    if (doorServer) visibleSet.Add("Corridor_Mid2");
                    break;

                case "TechWarehouse":
                    visibleSet.Add("TechWarehouse");
                    if (doorTech) visibleSet.Add("Corridor_Deep");
                    break;

                case "Dispatcher":
                    visibleSet.Add("Dispatcher");
                    if (doorDispatch) visibleSet.Add("Corridor_Deep");
                    break;

                default:
                    // Fallback: exterior + front
                    visibleSet.Add("Exterior");
                    visibleSet.Add("StationShell");
                    visibleSet.Add("Corridor_Front");
                    visibleSet.Add("Apparatus");
                    break;
            }

            // Apply visibility
            for (int i = 0; i < sectors.Length; i++)
            {
                var s = sectors[i];
                s.SetVisible(visibleSet.Contains(s.name));
            }

            lastPlayerSector = currentSector;
        }

        static string ResolveSector(Vector3 pos)
        {
            if (pos.z > 5.5f) return "Exterior";

            if (pos.x >= -2.5f && pos.x <= 2.5f)
            {
                if (pos.z >= -9f) return "Corridor_Front";
                if (pos.z >= -24f) return "Corridor_Mid1";
                if (pos.z >= -39f) return "Corridor_Mid2";
                return "Corridor_Deep";
            }

            if (pos.x > 2.5f)
            {
                if (pos.z >= -9f) return "Archive";
                if (pos.z >= -24f) return "Generator";
                if (pos.z >= -39f) return "Vent";
                return "TechWarehouse";
            }
            else
            {
                if (pos.z >= -9f) return "Apparatus";
                if (pos.z >= -24f) return "Studio04";
                if (pos.z >= -39f) return "Server";
                return "Dispatcher";
            }
        }
    }
}
