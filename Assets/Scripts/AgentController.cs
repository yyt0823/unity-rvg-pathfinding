using UnityEngine;
using System.Collections;

public class AgentController : MonoBehaviour
{
    private obstacles_generation levelBounds;
    private LayerMask obstacleLayers;
    private float agentRadius;
    private float xMin, xMax, zMin, zMax;
    private float levelLength;
    
    private GameObject currentDestination;
    private bool isMoving = false;
    private float agentSpeed;
    private RVG2 rvg2;
    
    public void Initialize(obstacles_generation bounds, LayerMask layers, float radius, float xMin, float xMax, float zMin, float zMax, RVG2.PathMode pathMode = RVG2.PathMode.Naive)
    {
        levelBounds = bounds;
        obstacleLayers = layers;
        agentRadius = radius;
        this.xMin = xMin;
        this.xMax = xMax;
        this.zMin = zMin;
        this.zMax = zMax;
        
        // Calculate level length (longest dimension)
        float width = xMax - xMin;
        float depth = zMax - zMin;
        levelLength = Mathf.Max(width, depth);
        
        // Calculate speed: 2 seconds to cross level length in straight line if cost = 1.0
        agentSpeed = levelLength / 2f;
        
        // Initialize RVG2
        rvg2 = gameObject.AddComponent<RVG2>();
        rvg2.Initialize(levelBounds, obstacleLayers, agentRadius, xMin, xMax, zMin, zMax);
        rvg2.SetPathMode(pathMode); // Set pathfinding mode
        rvg2.ReadObstaclesFromScene();
        rvg2.buildRVG(agentRadius);
        
        // Start moving to first destination
        StartCoroutine(MoveToRandomDestination());
    }
    
    IEnumerator MoveToRandomDestination()
    {
        // Pick a random valid destination
        Vector3 destination = PickRandomDestination();
        
        if (destination != Vector3.zero)
        {
            // Create visible destination marker
            CreateDestinationMarker(destination);
            
            // Use RVG2 pathfinding to get optimal path
            (Vector3[] path, float cost) = rvg2.FindPath(transform.position, destination);
            
            if (path == null || path.Length == 0)
            {
                Debug.LogWarning("AgentController: No path found, using straight line");
                path = new Vector3[] { transform.position, destination };
            }
            
            // Move along the path
            yield return StartCoroutine(MoveAlongPath(path));
            
            // Remove destination marker when reached
            if (currentDestination != null)
            {
                Destroy(currentDestination);
                currentDestination = null;
            }
        }
        // Agent stops after reaching destination
    }
    

    // s
    Vector3 PickRandomDestination()
    {
        float innerXMin = xMin + agentRadius;
        float innerXMax = xMax - agentRadius;
        float innerZMin = zMin + agentRadius;
        float innerZMax = zMax - agentRadius;
        float checkRadius = agentRadius + 0.5f;
        
        for (int attempt = 0; attempt < 100; attempt++)
        {
            float x = Random.Range(innerXMin, innerXMax);
            float z = Random.Range(innerZMin, innerZMax);
            Vector3 candidate = new Vector3(x, agentRadius, z);
            
            // Check if position is valid (no obstacles)
            if (!Physics.CheckSphere(candidate, checkRadius, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                return new Vector3(x, 0f, z);
            }
        }
        
        return Vector3.zero; // Failed to find valid destination
    }
    
    void CreateDestinationMarker(Vector3 position)
    {
        // Remove old marker if exists
        if (currentDestination != null)
        {
            Destroy(currentDestination);
        }
        
        // Create blue circular plane marker - same size as agent
        currentDestination = GameObject.CreatePrimitive(PrimitiveType.Quad);
        currentDestination.name = "DestinationMarker";
        currentDestination.transform.position = new Vector3(position.x, 0.02f, position.z); // Slightly above terrain to avoid z-fighting
        currentDestination.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Lay flat on ground
        float markerDiameter = agentRadius * 2f; // Same diameter as agent
        currentDestination.transform.localScale = new Vector3(markerDiameter, markerDiameter, 1f);
        
        // Make it blue
        Renderer renderer = currentDestination.GetComponent<Renderer>();
        renderer.material.color = Color.blue;
        
        // Remove collider 
        Destroy(currentDestination.GetComponent<Collider>());
    }
    
    public void DestroyDestinationMarker()
    {
        if (currentDestination != null)
        {
            Destroy(currentDestination);
            currentDestination = null;
        }
    }
    
    IEnumerator MoveAlongPath(Vector3[] path)
    {
        if (path == null || path.Length == 0)
        {
            yield break;
        }
        
        isMoving = true;
        
        foreach (Vector3 target in path)
        {
            Vector3 targetPos = new Vector3(target.x, agentRadius, target.z);
            
            while (Vector3.Distance(transform.position, targetPos) > 0.1f)
            {
                transform.position = Vector3.MoveTowards(transform.position, targetPos, agentSpeed * Time.deltaTime);
                yield return null;
            }
        }
        
        isMoving = false;
    }
}

