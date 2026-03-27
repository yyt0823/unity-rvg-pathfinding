using UnityEngine;
using System.Collections.Generic;

public class RVG2 : MonoBehaviour
{
    // ============ Data Structures ============
    
    // Obstacle shape type
    public enum ObstacleShape
    {
        T,
        U
    }
    
    // Obstacle data structure
    public struct Obstacle
    {
        public List<Vector3> vertices;      // Vertex positions
        public List<(int start, int end)> boundaryEdges;  // Boundary edges as vertex index pairs
        public List<float> vertexNormal;   // Vertex normals encoded as Y rotation (in degrees)
        public ObstacleShape shape;         // Shape type: T or U
        
        public Obstacle(List<Vector3> vertices, List<(int, int)> boundaryEdges, List<float> vertexNormal, ObstacleShape shape)
        {
            this.vertices = vertices;
            this.boundaryEdges = boundaryEdges;
            this.vertexNormal = vertexNormal;
            this.shape = shape;
        }
    }
    
    // RVG weighted edge data structure
    public struct RVGWeightedEdge
    {
        public int start;   // Start vertex index in rvgVertices
        public int end;     // End vertex index in rvgVertices
        public float weight; // Edge weight (terrain-weighted cost)
        
        public RVGWeightedEdge(int start, int end, float weight)
        {
            this.start = start;
            this.end = end;
            this.weight = weight;
        }
    }
    
    // List to hold all obstacles
    private List<Obstacle> obstacles = new List<Obstacle>();
    private List<Obstacle> expandedObstacles = new List<Obstacle>();
    
    // Agent radius and obstacle layers for bitangent checks
    private float agentRadius = 0f;
    private LayerMask obstacleLayers;
    
    // Terrain cost data
    private obstacles_generation levelBounds;
    private float xMin, xMax, zMin, zMax;
    
    // Pathfinding mode
    public enum PathMode { Naive, Augmented }
    [SerializeField] private PathMode pathMode = PathMode.Naive;
    
    // Public method to change pathfinding mode
    public void SetPathMode(PathMode mode)
    {
        pathMode = mode;
        Debug.Log($"RVG2: Path mode changed to {pathMode}");
    }
    
    
    
    
    // Augmentation parameters
    [SerializeField] private int augmentSamples = 30;
    [SerializeField] private float augmentMaxConnectDist = 8f;
    [SerializeField] private bool augmentLowCostBias = true;

    
    // RVG graph: vertices and edges
    private List<Vector3> rvgVertices = new List<Vector3>();
    private List<RVGWeightedEdge> rvgEdges = new List<RVGWeightedEdge>();
    
    // Augmented RVG graph: vertices and edges (with random samples)
    private List<Vector3> augRVGVertices = new List<Vector3>();
    private List<RVGWeightedEdge> augRVGEdges = new List<RVGWeightedEdge>();
    private int augBaseVertexCount = 0; // Track base vertex count before random samples
    
    // Current path for gizmo drawing
    private Vector3[] currentPath = null;
    private Vector3[] otherPath = null; // Path from the other mode (for comparison)
    
    // ============ Init ============
    
    // Initialize (interface compatibility)
    public void Initialize(obstacles_generation bounds, LayerMask layers, float radius, float xMin, float xMax, float zMin, float zMax)
    {
        levelBounds = bounds;
        obstacleLayers = layers;
        agentRadius = radius;
        this.xMin = Mathf.Min(xMin, xMax);
        this.xMax = Mathf.Max(xMin, xMax);
        this.zMin = Mathf.Min(zMin, zMax);
        this.zMax = Mathf.Max(zMin, zMax);
    }
    
    // Read obstacles from scene GameObjects
    public void ReadObstaclesFromScene()
    {
        obstacles.Clear();
        
        var allTransforms = FindObjectsOfType<Transform>(includeInactive: true);
        
        foreach (var t in allTransforms)
        {
            var v1 = t.Find("v1");
            if (v1 == null) continue;
            
            var vertices = new List<Vector3>();
            var normals = new List<float>();
            
            // Collect vertices v1..v8 (T) or v1..v6 (U)
            for (int idx = 1; idx <= 8; idx++)
            {
                var child = t.Find("v" + idx);
                if (child == null) break;
                
                Vector3 w = child.position;
                Vector3 basePos = new Vector3(w.x, 0f, w.z);
                vertices.Add(basePos);
                
                // Get Y rotation as normal
                float yRot = child.eulerAngles.y;
                normals.Add(yRot);
            }
            
            // Determine shape: check if a7/a8 exist (U shape) or not (T shape)
            var a7 = t.Find("a7");
            var a8 = t.Find("a8");
            ObstacleShape shape = ObstacleShape.T; // Default to T shape
            
            if (a7 != null && a8 != null)
            {
                // U shape: add a7 and a8
                shape = ObstacleShape.U;
                Vector3 a7Pos = new Vector3(a7.position.x, 0f, a7.position.z);
                vertices.Add(a7Pos);
                normals.Add(a7.eulerAngles.y);
                
                Vector3 a8Pos = new Vector3(a8.position.x, 0f, a8.position.z);
                vertices.Add(a8Pos);
                normals.Add(a8.eulerAngles.y);
            }
            
            // Build boundary edges: connect vertices clockwise
            var boundaryEdges = new List<(int, int)>();
            if (vertices.Count >= 2)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    int next = (i + 1) % vertices.Count;
                    boundaryEdges.Add((i, next));
                }
            }
            
            Obstacle obstacle = new Obstacle(vertices, boundaryEdges, normals, shape);
            obstacles.Add(obstacle);
        }
        
        Debug.Log($"RVG2: Read {obstacles.Count} obstacles from scene");
    }

    // Expand vertices by agent radius (interface compatibility)
    public void buildRVG(float agentRadius)
    {
        this.agentRadius = agentRadius; // Store agent radius for bitangent checks
        ExpandBoundary(agentRadius);
        
        // Always build both graphs so we can compare costs
        // Build base RVG (Naive)
        rvgVertices.Clear();
        rvgEdges.Clear();
        AddRVGVertices(rvgVertices);
        AddRVGEdges(rvgVertices, rvgEdges);
        Debug.Log($"RVG2: Base graph has {rvgVertices.Count} vertices and {rvgEdges.Count} edges");
        
        // Build augmented RVG
        augRVGVertices.Clear();
        augRVGEdges.Clear();
        AddRVGVertices(augRVGVertices);
        augBaseVertexCount = augRVGVertices.Count;
        Debug.Log($"RVG2: Base vertices count: {augBaseVertexCount}");
        AddRandomSampleVertices(augRVGVertices);
        Debug.Log($"RVG2: After random samples: {augRVGVertices.Count} vertices (added {augRVGVertices.Count - augBaseVertexCount})");
        AddRVGEdges(augRVGVertices, augRVGEdges);
        Debug.Log($"RVG2: Augmented graph has {augRVGEdges.Count} edges");
    }
    
    
    // Add random sampling vertices to a vertices list
    private void AddRandomSampleVertices(List<Vector3> vertices)
    {
        System.Random rng = new System.Random();
        
        for (int s = 0; s < augmentSamples; s++)
        {
            // Sample a random point within bounds
            Vector3 p = new Vector3(
                (float)(xMin + (xMax - xMin) * rng.NextDouble()),
                0f,
                (float)(zMin + (zMax - zMin) * rng.NextDouble()));
            
            // Optional low-cost bias: resample if in high-cost area
            if (augmentLowCostBias)
            {
                for (int t = 0; t < 16; t++)
                {
                    float x = (float)(xMin + (xMax - xMin) * rng.NextDouble());
                    float z = (float)(zMin + (zMax - zMin) * rng.NextDouble());
                    p = new Vector3(x, 0f, z);
                    float c = GetTerrainCostAt(p);
                    float accept = Mathf.Clamp01(1f / (c + 1e-3f));
                    if ((float)rng.NextDouble() < accept) break;
                }
            }
            
            // Check if point is too close to existing vertices (avoid duplicates)
            bool tooClose = false;
            foreach (var existing in vertices)
            {
                if (Vector3.Distance(p, existing) < 1e-6f)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;
            
            // Add sample to vertices list
            vertices.Add(p);
        }
        
        Debug.Log($"RVG2: Added random sample vertices (total vertices: {vertices.Count})");
    }
    
    // ============ helper functions ============
    
    // Check if two points are visible (line segment doesn't intersect obstacle edges)
    private bool IsVisible(Vector3 a, Vector3 b, List<Obstacle> expandedObstacles)
    {
        Vector2 a2D = new Vector2(a.x, a.z);
        Vector2 b2D = new Vector2(b.x, b.z);
        
        foreach (var obstacle in expandedObstacles)
        {
            foreach (var (start, end) in obstacle.boundaryEdges)
            {
                if (start >= 0 && start < obstacle.vertices.Count && 
                    end >= 0 && end < obstacle.vertices.Count)
                {
                    Vector2 edgeStart = new Vector2(obstacle.vertices[start].x, obstacle.vertices[start].z);
                    Vector2 edgeEnd = new Vector2(obstacle.vertices[end].x, obstacle.vertices[end].z);
                    
                    // Skip if endpoints match (touching at vertex is allowed)
                    if ((a2D - edgeStart).sqrMagnitude < 1e-6f || (a2D - edgeEnd).sqrMagnitude < 1e-6f ||
                        (b2D - edgeStart).sqrMagnitude < 1e-6f || (b2D - edgeEnd).sqrMagnitude < 1e-6f)
                    {
                        continue;
                    }
                    
                    // Check if line segments intersect
                    if (LineSegmentsIntersect(a2D, b2D, edgeStart, edgeEnd))
                    {
                        return false;
                    }
                }
            }
        }
        
        return true;
    }
    
    // Check if two 2D line segments intersect
    private bool LineSegmentsIntersect(Vector2 A, Vector2 B, Vector2 C, Vector2 D)
    {
        const float EPS = 1e-7f;
        
        // 2D cross product: a.x * b.y - a.y * b.x
        float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        
        // Orientation test: returns 1 if c is to the left of line ab, -1 if right, 0 if collinear
        int Orient(Vector2 a, Vector2 b, Vector2 c)
        {
            // Compute signed area: (b - a) × (c - a)
            float v = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return v > EPS ? 1 : (v < -EPS ? -1 : 0);
        }
        
        int o1 = Orient(A, B, C);
        int o2 = Orient(A, B, D);
        int o3 = Orient(C, D, A);
        int o4 = Orient(C, D, B);
        
        // Proper intersection only
        return (o1 != o2 && o3 != o4);
    }
    // Helper function: add vi vertices from expanded obstacles to rvgVertices
    private void AddRVGVertices(List<Vector3> vertices)
    {
        foreach (var obstacle in expandedObstacles)
        {
            foreach (var vertex in obstacle.vertices)
            {
                // Check if vertex already exists (simple distance check)
                bool exists = false;
                foreach (var existing in vertices)
                {
                    if (Vector3.Distance(vertex, existing) < 1e-6f)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    vertices.Add(vertex);
                }
            }
        }
    }

    // Helper function: add edges between vertices if they are visible from both sides
    private void AddRVGEdges(List<Vector3> vertices, List<RVGWeightedEdge> edges)
    {
        // Clear edges list
        edges.Clear();
        
        // Test all pairs of vertices for visibility
        for (int i = 0; i < vertices.Count; i++)
        {
            for (int j = i + 1; j < vertices.Count; j++)
            {
                Vector3 a = vertices[i];
                Vector3 b = vertices[j];
                
                bool shouldAddEdge = false;
                
                // Check if this is a specific edge pair (T or U shape internal edges)
                if (IsSpecificEdgePair(a, b))
                {
                    shouldAddEdge = true;
                }
                // Otherwise check visibility both directions (a->b and b->a)
                else if (IsBitangent(a, b, expandedObstacles) && IsNotInsideSameBoundary(a, b, expandedObstacles))
                {
                    shouldAddEdge = true;
                }
                
                // If edge should be added, compute weight and add it
                if (shouldAddEdge)
                {
                    float weight = ComputeEdgeCostWithTerrain(a, b);
                    edges.Add(new RVGWeightedEdge(i, j, weight));
                }
            }
        }
    }

    // Check if two vertices are NOT from the same obstacle (inside boundary)
    private bool IsNotInsideSameBoundary(Vector3 a, Vector3 b, List<Obstacle> expandedObstacles)
    {
        const float EPS = 1e-6f;
        
        // Check if both vertices belong to the same obstacle
        foreach (var obstacle in expandedObstacles)
        {
            bool aInObstacle = false;
            bool bInObstacle = false;
            
            foreach (var vertex in obstacle.vertices)
            {
                if (Vector3.Distance(a, vertex) < EPS)
                    aInObstacle = true;
                if (Vector3.Distance(b, vertex) < EPS)
                    bInObstacle = true;
            }
            
            // If both vertices are in the same obstacle, they're inside the same boundary
            if (aInObstacle && bInObstacle)
                return false;
        }
        
        // Vertices are from different obstacles (or not found)
        return true;
    }
    
    // Check if two vertices form a specific edge pair for T or U shapes
    private bool IsSpecificEdgePair(Vector3 a, Vector3 b)
    {
        const float EPS = 1e-6f;
        
        // Iterate through obstacles to find which obstacle both vertices belong to
        for (int obsIdx = 0; obsIdx < expandedObstacles.Count; obsIdx++)
        {
            var obstacle = expandedObstacles[obsIdx];
            
            int aIdx = -1;
            int bIdx = -1;
            
            // Find local indices of a and b in this obstacle
            for (int i = 0; i < obstacle.vertices.Count; i++)
            {
                if (Vector3.Distance(a, obstacle.vertices[i]) < EPS)
                    aIdx = i;
                if (Vector3.Distance(b, obstacle.vertices[i]) < EPS)
                    bIdx = i;
            }
            
            // If both vertices are in the same obstacle
            if (aIdx >= 0 && bIdx >= 0)
            {
                // Check based on shape
                if (obstacle.shape == ObstacleShape.T)
                {
                    // T shape: check if it's a boundary edge
                    foreach (var (start, end) in obstacle.boundaryEdges)
                    {
                        if ((aIdx == start && bIdx == end) || (aIdx == end && bIdx == start))
                        {
                            return true;
                        }
                    }
                    
                    // T shape: also add v6-v8 (indices 5-7) and v3-v5 (indices 2-4)
                    if ((aIdx == 5 && bIdx == 7) || (aIdx == 7 && bIdx == 5) ||
                        (aIdx == 2 && bIdx == 4) || (aIdx == 4 && bIdx == 2))
                    {
                        return true;
                    }
                }
                else if (obstacle.shape == ObstacleShape.U)
                {
                    // U shape: vi are indices 0-5 (v1-v6)
                    // Add edges between consecutive vi: 0-1, 1-2, 2-3, 3-4, 4-5, and wrap-around 5-0
                    if (aIdx < 6 && bIdx < 6)
                    {
                        // Consecutive pairs
                        if (aIdx == (bIdx + 1) % 6 || bIdx == (aIdx + 1) % 6)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        
        return false;
    }
    
    // Check if two points form a bitangent (line tangent to obstacles, visible both directions)
    private bool IsBitangent(Vector3 a, Vector3 b, List<Obstacle> expandedObstacles)
    {
        // First check visibility in both directions
        if (!IsVisible(a, b, expandedObstacles) || !IsVisible(b, a, expandedObstacles))
            return false;
        
        // Extend line by agentRadius * 1.2 in both directions
        Vector2 a2D = new Vector2(a.x, a.z);
        Vector2 b2D = new Vector2(b.x, b.z);
        Vector2 dir = (b2D - a2D);
        float len = dir.magnitude;
        if (len < 1e-6f) return false;
        
        Vector2 normalizedDir = dir / len;
        float extend = agentRadius * 1.2f;
        Vector2 aExtended = a2D - normalizedDir * extend;
        Vector2 bExtended = b2D + normalizedDir * extend;
        
        // Check if extended endpoints collide with original obstacles using Physics
        Vector3 aExt3D = new Vector3(aExtended.x, 0f, aExtended.y);
        Vector3 bExt3D = new Vector3(bExtended.x, 0f, bExtended.y);
        
        // Use CheckSphere to see if extended endpoints overlap with obstacles
        if (Physics.CheckSphere(aExt3D, agentRadius, obstacleLayers, QueryTriggerInteraction.Ignore) ||
            Physics.CheckSphere(bExt3D, agentRadius, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        
        return true;
    }
    
    //---------------------------------------//
    // Find path (interface compatibility)
    public (Vector3[] path, float cost) FindPath(Vector3 start, Vector3 goal)
    {
        
        return ComputePath(start, goal);
    }
    
    // Expand boundary edges by agent radius
    private void ExpandBoundary(float agentRadius)
    {
        expandedObstacles.Clear();
        
        foreach (var obstacle in obstacles)
        {
            var expandedVertices = new List<Vector3>();
            
            for (int i = 0; i < obstacle.vertices.Count; i++)
            {
                // Convert Y rotation (degrees) to direction vector on XZ plane
                float angleRad = obstacle.vertexNormal[i] * Mathf.Deg2Rad;
                Vector3 normalDir = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
                
                // Expand vertex along normal by agent radius
                Vector3 expanded = obstacle.vertices[i] + normalDir * agentRadius;
                expandedVertices.Add(expanded);
            }
            
            // Add boundary edges connecting vertices clockwise (v0-v1, v1-v2, ..., vN-v0)
            var boundaryEdges = new List<(int, int)>();
            for (int i = 0; i < expandedVertices.Count; i++)
            {
                int next = (i + 1) % expandedVertices.Count;
                boundaryEdges.Add((i, next));
            }
            
            // Create expanded obstacle with boundary edges connecting vertices clockwise
            var expandedObstacle = new Obstacle(
                expandedVertices,
                boundaryEdges,
                obstacle.vertexNormal,
                obstacle.shape  // Preserve shape
            );
            expandedObstacles.Add(expandedObstacle);
        }
        
        // Build RVG graph after expanding
        
    }
    
    // Dijkstra's algorithm to find shortest path on graph G
    // Returns (path as list of vertex indices, total cost)
    private (List<int> path, float cost) Dijkstra(List<Vector3> vertices, List<RVGWeightedEdge> edges, int startIdx, int endIdx)
    {
        if (startIdx < 0 || startIdx >= vertices.Count || endIdx < 0 || endIdx >= vertices.Count)
        {
            Debug.LogWarning("RVG2: Invalid start or end index");
            return (null, float.MaxValue);
        }
        
        // Build adjacency list from weighted edges
        var graph = new List<(int to, float cost)>[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            graph[i] = new List<(int, float)>();
        
        foreach (var edge in edges)
        {
            if (edge.start >= 0 && edge.start < vertices.Count && edge.end >= 0 && edge.end < vertices.Count)
            {
                // Use the weight from the edge (already terrain-weighted)
                graph[edge.start].Add((edge.end, edge.weight));
                graph[edge.end].Add((edge.start, edge.weight)); // Undirected graph
            }
        }
        
        // Initialize distances and previous nodes
        float[] distances = new float[vertices.Count];
        int[] previous = new int[vertices.Count];
        bool[] visited = new bool[vertices.Count];
        
        for (int i = 0; i < vertices.Count; i++)
        {
            distances[i] = float.MaxValue;
            previous[i] = -1;
            visited[i] = false;
        }
        
        distances[startIdx] = 0f;
        
        // Dijkstra's algorithm
        for (int iter = 0; iter < vertices.Count; iter++)
        {
            // Find unvisited node with minimum distance
            int current = -1;
            float minDist = float.MaxValue;
            
            for (int i = 0; i < vertices.Count; i++)
            {
                if (!visited[i] && distances[i] < minDist)
                {
                    minDist = distances[i];
                    current = i;
                }
            }
            
            // No reachable nodes left
            if (current == -1) break;
            
            // Found goal, reconstruct path
            if (current == endIdx)
            {
                var path = new List<int>();
                int node = endIdx;
                while (node != -1)
                {
                    path.Add(node);
                    node = previous[node];
                }
                path.Reverse();
                return (path, distances[endIdx]);
            }
            
            visited[current] = true;
            
            // Update distances to neighbors
            foreach (var edge in graph[current])
            {
                int neighbor = edge.to;
                float edgeCost = edge.cost;
                
                if (!visited[neighbor])
                {
                    float newDist = distances[current] + edgeCost;
                    if (newDist < distances[neighbor])
                    {
                        distances[neighbor] = newDist;
                        previous[neighbor] = current;
                    }
                }
            }
        }
        
        // No path found
        return (null, float.MaxValue);
    }
    
    // ============ Terrain Cost Calculation ============
    
    // Get terrain cost for a given position based on 3x2 grid
    private float GetTerrainCostAt(Vector3 pos)
    {
        // Get terrain cost for a given position based on 3x2 grid
        if (levelBounds == null || levelBounds.terrainCosts == null || levelBounds.terrainCosts.Length < 6)
        {
            return 1.0f; // Default cost if terrain data not available
        }

        float width = xMax - xMin;
        float depth = zMax - zMin;
        
        if (width <= 0f || depth <= 0f)
        {
            return 1.0f;
        }

        // Calculate which cell the position is in (3 columns, 2 rows)
        float relX = (pos.x - xMin) / width;  // 0 to 1
        float relZ = (pos.z - zMin) / depth;  // 0 to 1
        
        // Clamp to valid range
        relX = Mathf.Clamp01(relX);
        relZ = Mathf.Clamp01(relZ);
        
        // Convert to grid indices (0-2 for col, 0-1 for row)
        int col = Mathf.Clamp(Mathf.FloorToInt(relX * 3f), 0, 2);
        int row = Mathf.Clamp(Mathf.FloorToInt(relZ * 2f), 0, 1);
        
        // Calculate index: row * 3 + col
        int idx = row * 3 + col;
        
        if (idx < 0 || idx >= levelBounds.terrainCosts.Length)
        {
            return 1.0f;
        }
        
        return Mathf.Max(0.001f, levelBounds.terrainCosts[idx]);
    }
    
    // Compute edge cost with terrain weighting
    public float ComputeEdgeCostWithTerrain(Vector3 a, Vector3 b)
    {
        float distance = Vector3.Distance(a, b);
        
        if (distance < 1e-4f)
        {
            return 0f;
        }

        // Sample terrain costs along the edge
        // Use multiple samples to account for edges crossing multiple terrain cells
        int numSamples = Mathf.Max(3, Mathf.CeilToInt(distance / 2f)); // Sample every ~2 units
        float totalCost = 0f;
        
        for (int i = 0; i <= numSamples; i++)
        {
            float t = (float)i / numSamples;
            Vector3 samplePos = Vector3.Lerp(a, b, t);
            totalCost += GetTerrainCostAt(samplePos);
        }
        
        // Average terrain cost along the edge
        float avgTerrainCost = totalCost / (numSamples + 1);
        
        // Weighted cost = distance × average terrain cost
        return distance * avgTerrainCost;
    }
 
    // Helper function to compute path on a given graph
    private (Vector3[] path, float cost) ComputePathOnGraph(Vector3 start, Vector3 goal, List<Vector3> vertices, List<RVGWeightedEdge> edges)
    {
        // Check if graph is built
        if (vertices == null || vertices.Count == 0 || edges == null || edges.Count == 0)
        {
            float fallbackCost = ComputeEdgeCostWithTerrain(start, goal);
            return (new Vector3[] { start, goal }, fallbackCost);
        }
        
        // Project start and goal to ground plane
        Vector3 startPos = new Vector3(start.x, 0f, start.z);
        Vector3 goalPos = new Vector3(goal.x, 0f, goal.z);
        
        // Build node list: start (0), goal (1), then all RVG vertices (2+)
        var nodes = new List<Vector3>();
        nodes.Add(startPos);
        nodes.Add(goalPos);
        nodes.AddRange(vertices);
        
        int numNodes = nodes.Count;
        
        // Build weighted edges list for Dijkstra
        var allWeightedEdges = new List<RVGWeightedEdge>();
        
        // Add existing RVG edges (offset indices by 2 since start=0, goal=1)
        foreach (var edge in edges)
        {
            int startIdx = edge.start + 2;  // Offset: RVG vertices start at index 2
            int endIdx = edge.end + 2;
            allWeightedEdges.Add(new RVGWeightedEdge(startIdx, endIdx, edge.weight));
        }
        
        // Connect start (0) to visible RVG vertices
        for (int i = 2; i < numNodes; i++)
        {
            if (IsVisible(startPos, nodes[i], expandedObstacles))
            {
                float weight = ComputeEdgeCostWithTerrain(startPos, nodes[i]);
                allWeightedEdges.Add(new RVGWeightedEdge(0, i, weight));
            }
        }
        
        // Connect goal (1) to visible RVG vertices
        for (int i = 2; i < numNodes; i++)
        {
            if (IsVisible(goalPos, nodes[i], expandedObstacles))
            {
                float weight = ComputeEdgeCostWithTerrain(goalPos, nodes[i]);
                allWeightedEdges.Add(new RVGWeightedEdge(1, i, weight));
            }
        }
        
        // Connect start to goal if visible
        if (IsVisible(startPos, goalPos, expandedObstacles))
        {
            float weight = ComputeEdgeCostWithTerrain(startPos, goalPos);
            allWeightedEdges.Add(new RVGWeightedEdge(0, 1, weight));
        }
        
        // Run Dijkstra from start (0) to goal (1)
        (List<int> pathIndices, float cost) = Dijkstra(nodes, allWeightedEdges, 0, 1);
        
        if (pathIndices == null || pathIndices.Count == 0)
        {
            // No path found, return straight line as fallback
            float fallbackCost = ComputeEdgeCostWithTerrain(startPos, goalPos);
            return (new Vector3[] { startPos, goalPos }, fallbackCost);
        }
        
        // Convert indices to actual positions
        var path = new Vector3[pathIndices.Count];
        for (int i = 0; i < pathIndices.Count; i++)
        {
            path[i] = nodes[pathIndices[i]];
        }
        
        return (path, cost);
    }
    
    // This build the RVG graph and then run Dijkstra
    public (Vector3[] path, float cost) ComputePath(Vector3 start, Vector3 goal)
    {
        // Always compute both Naive and Augmented paths and log costs
        float naiveCost = float.MaxValue;
        float augmentedCost = float.MaxValue;
        Vector3[] naivePath = null;
        Vector3[] augPath = null;
        
        // Compute Naive path
        if (rvgVertices != null && rvgVertices.Count > 0 && rvgEdges != null && rvgEdges.Count > 0)
        {
            (naivePath, naiveCost) = ComputePathOnGraph(start, goal, rvgVertices, rvgEdges);
        }
        else
        {
            naiveCost = ComputeEdgeCostWithTerrain(start, goal);
            naivePath = new Vector3[] { start, goal };
        }
        
        // Compute Augmented path
        if (augRVGVertices != null && augRVGVertices.Count > 0 && augRVGEdges != null && augRVGEdges.Count > 0)
        {
            (augPath, augmentedCost) = ComputePathOnGraph(start, goal, augRVGVertices, augRVGEdges);
        }
        else
        {
            augmentedCost = ComputeEdgeCostWithTerrain(start, goal);
            augPath = new Vector3[] { start, goal };
        }
        
        // Log both costs
        Debug.Log($"RVG2 Compare: Naive cost = {naiveCost:F2}, Augmented cost = {augmentedCost:F2}");
        
        // Store both paths for visualization
        if (pathMode == PathMode.Augmented)
        {
            currentPath = augPath;  // Current mode path (red)
            otherPath = naivePath;  // Other mode path (pink)
            return (augPath, augmentedCost);
        }
        else
        {
            currentPath = naivePath;  // Current mode path (red)
            otherPath = augPath;       // Other mode path (pink)
            return (naivePath, naiveCost);
        }
    }
    
    // ============ Getters ============
    
    public List<Obstacle> GetObstacles()
    {
        return obstacles;
    }
    
    public List<Vector3> GetRVGVertices()
    {
        return rvgVertices;
    }
    
    public List<RVGWeightedEdge> GetRVGEdges()
    {
        return rvgEdges;
    }
    
    // ============ Gizmo Drawing ============
    
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        DrawExpandedObstacles();
        DrawRVGEdges();
        DrawPath();
    }
    
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        DrawExpandedObstacles();
        DrawRVGEdges();
        DrawPath();
    }
    
    private void DrawExpandedObstacles()
    {
        if (expandedObstacles == null || expandedObstacles.Count == 0) return;
        
        Gizmos.color = Color.yellow;
        foreach (var obstacle in expandedObstacles)
        {
            foreach (var (start, end) in obstacle.boundaryEdges)
            {
                if (start >= 0 && start < obstacle.vertices.Count && 
                    end >= 0 && end < obstacle.vertices.Count)
                {
                    Vector3 startPos = obstacle.vertices[start];
                    Vector3 endPos = obstacle.vertices[end];
                    startPos.y = 0.02f;
                    endPos.y = 0.02f;
                    Gizmos.DrawLine(startPos, endPos);
                }
            }
        }
    }
    
    private void DrawRVGEdges()
    {
        // Select graph based on mode
        List<Vector3> vertices = (pathMode == PathMode.Augmented) ? augRVGVertices : rvgVertices;
        List<RVGWeightedEdge> edges = (pathMode == PathMode.Augmented) ? augRVGEdges : rvgEdges;
        
        if (edges == null || edges.Count == 0 || vertices == null || vertices.Count == 0) return;
        
        // Set color based on mode: yellow for Augmented, cyan for Naive
        Gizmos.color = (pathMode == PathMode.Augmented) ? Color.yellow : Color.cyan;
        
        foreach (var edge in edges)
        {
            if (edge.start >= 0 && edge.start < vertices.Count && 
                edge.end >= 0 && edge.end < vertices.Count)
            {
                Vector3 startPos = vertices[edge.start];
                Vector3 endPos = vertices[edge.end];
                startPos.y = 0.03f; // Slightly above expanded obstacles for visibility
                endPos.y = 0.03f;
                Gizmos.DrawLine(startPos, endPos);
            }
        }
        
        // Draw augmented vertices (random samples) in magenta when in Augmented mode
        if (pathMode == PathMode.Augmented && augRVGVertices.Count > augBaseVertexCount)
        {
            Gizmos.color = Color.magenta;
            float sphereRadius = 0.08f;
            for (int i = augBaseVertexCount; i < augRVGVertices.Count; i++)
            {
                Vector3 pos = augRVGVertices[i];
                pos.y = 0.04f;
                Gizmos.DrawSphere(pos, sphereRadius);
            }
        }
    }
    
    private void DrawPath()
    {
        // Draw current mode path in red
        if (currentPath != null && currentPath.Length >= 2)
        {
            Gizmos.color = Color.red;
            
            // Draw lines between consecutive waypoints
            for (int i = 0; i < currentPath.Length - 1; i++)
            {
                Vector3 start = currentPath[i];
                Vector3 end = currentPath[i + 1];
                start.y = 0.05f;
                end.y = 0.05f;
                Gizmos.DrawLine(start, end);
            }
            
            // Draw spheres at waypoints
            float sphereRadius = 0.1f;
            foreach (var waypoint in currentPath)
            {
                Vector3 pos = waypoint;
                pos.y = 0.05f;
                Gizmos.DrawSphere(pos, sphereRadius);
            }
        }
        
        // Draw other mode path in pink
        if (otherPath != null && otherPath.Length >= 2)
        {
            Gizmos.color = Color.magenta;
            
            // Draw lines between consecutive waypoints
            for (int i = 0; i < otherPath.Length - 1; i++)
            {
                Vector3 start = otherPath[i];
                Vector3 end = otherPath[i + 1];
                start.y = 0.06f; // Slightly higher than current path
                end.y = 0.06f;
                Gizmos.DrawLine(start, end);
            }
            
            // Draw spheres at waypoints (smaller)
            float sphereRadius = 0.08f;
            foreach (var waypoint in otherPath)
            {
                Vector3 pos = waypoint;
                pos.y = 0.06f;
                Gizmos.DrawSphere(pos, sphereRadius);
            }
        }
    }
}

