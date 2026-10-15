namespace Tailoring.Domain.Production;

public static class DependencyGraph
{
    /// <summary>Edges are (Prerequisite, Dependent). Returns true when the graph contains a cycle (Kahn's algorithm).</summary>
    public static bool HasCycle(IEnumerable<string> nodes, IEnumerable<(string Prerequisite, string Dependent)> edges)
    {
        var inDegree = nodes.Distinct().ToDictionary(n => n, _ => 0);
        var next = inDegree.Keys.ToDictionary(n => n, _ => new List<string>());
        foreach (var (pre, dep) in edges)
        {
            if (!inDegree.ContainsKey(pre) || !inDegree.ContainsKey(dep)) continue;
            next[pre].Add(dep);
            inDegree[dep]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            visited++;
            foreach (var d in next[n])
                if (--inDegree[d] == 0) queue.Enqueue(d);
        }
        return visited != inDegree.Count;
    }
}
