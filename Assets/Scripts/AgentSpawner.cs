using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class AgentSpawner : MonoBehaviour
{
    // get the l r t b from the other file
    public obstacles_generation levelBounds; 
    // emun for agent size 
    public enum AgentSize
    {
        Small,
        Medium,
        Large
    }

    [Header("Agent Settings")]
    public AgentSize size = AgentSize.Medium;
    public float smallRadius;
    public float mediumRadius;
    public float largeRadius;
    // for checking collide using layer 
    public LayerMask obstacleLayers;
    
    [Header("Pathfinding Settings")]
    public RVG2.PathMode pathMode = RVG2.PathMode.Naive; 
    public int maxSpawnAttempts = 200;
    private GameObject currentAgent;
    private GameObject uiMenu;
    public Font uiFont; 


    private float GetAgentRadius()
    {
        switch (size)
        {
            case AgentSize.Small: return smallRadius;
            case AgentSize.Medium: return mediumRadius;
            case AgentSize.Large: return largeRadius;
            default: return mediumRadius;
        }
    }
    
    // Validate that radii 
    private bool ValidateRadii()
    {
        if (smallRadius <= 0 || mediumRadius <= 0 || largeRadius <= 0)
        {
            Debug.LogWarning("AgentSpawner: One or more agent radii are not set or invalid. Please set smallRadius, mediumRadius, and largeRadius in the Inspector.");
            return false;
        }
        return true;
    }

    void Start()
    {
        CreateUIMenu();
    }


    // a menu that have 3 button small medium and large when click spawn agent for that size at a valid location
    void CreateUIMenu()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("AgentMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Ensure EventSystem exists for UI interaction
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // Create 3 buttons - Small, Medium, Large at bottom-center
        CreateSizeButton("Small", AgentSize.Small, canvasObj.transform, -180, 80);
        CreateSizeButton("Medium", AgentSize.Medium, canvasObj.transform, 0, 80);
        CreateSizeButton("Large", AgentSize.Large, canvasObj.transform, 180, 80);

        uiMenu = canvasObj;
    }

    void CreateSizeButton(string label, AgentSize sizeType, Transform parent, float xPos, float yPos)
    {
        GameObject btnObj = CreateUIButton(
            parent,
            $"{label}Button",
            label,
            new Vector2(0.5f, 0f), // bottom-center
            new Vector2(0.5f, 0f),
            new Vector2(160, 60),
            new Vector2(xPos, yPos),
            new Color(0.15f, 0.15f, 0.15f)
        );
        btnObj.GetComponent<Button>().onClick.AddListener(() => {
            size = sizeType;
            SpawnAgent();
        });
        Debug.Log($"UI: Created {label} button");
    }

    GameObject CreateUIButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPos, Color background)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        Image img = btnObj.AddComponent<Image>();
        img.color = background;
        var imgOutline = btnObj.AddComponent<Outline>();
        imgOutline.effectColor = new Color(1f, 1f, 1f, 0.25f);
        imgOutline.effectDistance = new Vector2(2f, -2f);
        Button button = btnObj.AddComponent<Button>();

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;

        // Create child Text for label
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        Text txt = txtObj.AddComponent<Text>();
        txt.text = label;
        // Use assigned font if provided, otherwise fall back to built-in Arial
        txt.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.fontSize = 30;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.raycastTarget = false;

        var outline = txtObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(2f, -2f);
        RectTransform txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.anchorMin = new Vector2(0, 0);
        txtRect.anchorMax = new Vector2(1, 1);
        txtRect.sizeDelta = Vector2.zero;
        txtRect.anchoredPosition = Vector2.zero;

        return btnObj;
    }


    // main functionality for this project, will 'link' to pathfinding file
    public void SpawnAgent()
    {
        // validate the radii
        if (!ValidateRadii())
        {
            return;
        }
        //check if r t b l can be retrieve
        if (levelBounds == null)
        {
            Debug.LogWarning("AgentSpawner: levelBounds reference is missing.");
            return;
        }

        float xMin = Mathf.Min(levelBounds.left, levelBounds.right);
        float xMax = Mathf.Max(levelBounds.left, levelBounds.right);
        float zMin = Mathf.Min(levelBounds.top, levelBounds.bot);
        float zMax = Mathf.Max(levelBounds.top, levelBounds.bot);
        float agentRadius = GetAgentRadius();

        // Ensure we have interior space
        if ((xMax - xMin) <= agentRadius * 2f || (zMax - zMin) <= agentRadius * 2f)
        {
            Debug.LogWarning("AgentSpawner: Level bounds too small for selected agent size.");
            return;
        }

        Vector3 spawnPosition;
        bool found = TryFindValidPosition(xMin, xMax, zMin, zMax, agentRadius, out spawnPosition);
        if (!found)
        {
            Debug.LogWarning("AgentSpawner: Failed to find a valid spawn position.");
            return;
        }

        // Replace any existing agent and destroy its destination marker
        if (currentAgent != null)
        {
            AgentController oldController = currentAgent.GetComponent<AgentController>();
            if (oldController != null)
            {
                oldController.DestroyDestinationMarker();
            }
            DestroyImmediate(currentAgent);
        }

        // Create a sphere to represent the agent with size we choose
        currentAgent = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        currentAgent.name = $"Agent_{size}";
        float diameter = agentRadius * 2f;
        currentAgent.transform.localScale = new Vector3(diameter, diameter, diameter);
        currentAgent.transform.position = new Vector3(spawnPosition.x, agentRadius, spawnPosition.z);

        // Add agent controller for movement and pathfinding
        AgentController controller = currentAgent.AddComponent<AgentController>();
        controller.Initialize(levelBounds, obstacleLayers, agentRadius, xMin, xMax, zMin, zMax, pathMode);
    }


    // check if new position is overlap with obstacles and return a valid position
    private bool TryFindValidPosition(float xMin, float xMax, float zMin, float zMax, float radius, out Vector3 result)
    {
        float innerXMin = xMin + radius;
        float innerXMax = xMax - radius;
        float innerZMin = zMin + radius;
        float innerZMax = zMax - radius;

        // Add a small safety margin to prevent touching edges
        float checkRadius = radius + 0.5f;

        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            float x = Random.Range(innerXMin, innerXMax);
            float z = Random.Range(innerZMin, innerZMax);
            Vector3 candidate = new Vector3(x, radius, z);

            // Check overlap with obstacles using checkRadius (includes safety margin)
            bool hasOverlap = Physics.CheckSphere(candidate, checkRadius, obstacleLayers, QueryTriggerInteraction.Ignore);
            
            
            
            if (!hasOverlap)
            {
                result = new Vector3(x, 0f, z);
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }
}


