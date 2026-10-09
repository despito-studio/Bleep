using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DeadAir
{
    [Serializable]
    public sealed class DeadAirNavigationBakeData
    {
        public int version;
        public string worldSha256;
        public int authoredNodeCount, authoredLinkCount;
        public int[] sourceNodes;
        public NavigationLinkData[] links;
    }

    // Only navigation is replaced. Missions, classes, furniture and derived
    // inventory placements remain the original WorldData records.
    public static class DeadAirNavigationOverlay
    {
        public const int Version = 1;

        public static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static bool TryApply(WorldData world, byte[] sourceBytes, DeadAirNavigationBakeData bake, out string reason)
        {
            reason = null;
            if (world == null || world.navigationNodes == null || world.navigationLinks == null || bake == null)
            { reason = "missing authored graph or bake"; return false; }
            if (bake.version != Version) { reason = "unsupported bake version"; return false; }
            if (sourceBytes == null || !string.Equals(bake.worldSha256, Hash(sourceBytes), StringComparison.Ordinal))
            { reason = "world.json SHA256 changed"; return false; }
            if (bake.authoredNodeCount != world.navigationNodes.Length || bake.authoredLinkCount != world.navigationLinks.Length)
            { reason = "authored graph size changed"; return false; }
            if (bake.sourceNodes == null || bake.sourceNodes.Length == 0 || bake.links == null || bake.links.Length == 0)
            { reason = "empty retained graph"; return false; }

            var nodes = new NavigationNodeData[bake.sourceNodes.Length];
            var seenNodes = new HashSet<int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                int source = bake.sourceNodes[i];
                if (source < 0 || source >= world.navigationNodes.Length || !seenNodes.Add(source))
                { reason = "invalid or duplicated source node"; return false; }
                nodes[i] = world.navigationNodes[source];
            }
            var authoredEdges = new HashSet<long>();
            foreach (var edge in world.navigationLinks) authoredEdges.Add(EdgeKey(edge.a, edge.b));
            var seenEdges = new HashSet<long>();
            foreach (var edge in bake.links)
            {
                if (edge == null || edge.a < 0 || edge.b < 0 || edge.a >= nodes.Length || edge.b >= nodes.Length || edge.a == edge.b ||
                    !seenEdges.Add(EdgeKey(edge.a, edge.b)) || !authoredEdges.Contains(EdgeKey(bake.sourceNodes[edge.a], bake.sourceNodes[edge.b])))
                { reason = "invalid retained edge or unauthored shortcut"; return false; }
            }
            world.navigationNodes = nodes;
            world.navigationLinks = bake.links;
            world.navigationBaked = true;
            return true;
        }

        static long EdgeKey(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
    }
}
