using UnityEngine;
using System.Collections.Generic;

public class obstacles_generation : MonoBehaviour
{
    // fields for prefabs, l r t b and margin for obstacle generation
    public GameObject prefabT;
    public GameObject prefabU;
    public int left;
    public int right;
    public int top;
    public int bot;
    public int margin;
    public int margin_edge;
    
    // fields for color and cost for terrain generation
    public Color lowCostColor = Color.green;
    public Color highCostColor = Color.red;
    public float[] terrainCosts = new float[6];
    
    
    // init obstacles
    void Start()
    {
        // ignore y axis for now 
        List<Vector2> obj_list = new List<Vector2>();
        int count = Random.Range(8, 13); // Random number from 8 to 12 (inclusive)

        // pick random place and check for distance validity
        for (int i = 0; i < count; i++)
        {
            bool validPosition = false;
            int attempts = 0;
            int maxAttempts = 3000; // Prevent infinite loop
            
            while (!validPosition && attempts < maxAttempts)
            {
                float x = Random.Range(left + margin_edge, right - margin_edge);
                float y = Random.Range(top - margin_edge, bot + margin_edge);
                Vector2 newPosition = new Vector2(x, y);
                
                validPosition = true;
                foreach (Vector2 obj in obj_list)
                {
                    float distance = Vector2.Distance(obj, newPosition);
                    if (distance < margin)
                    {
                        validPosition = false;
                        break;
                    }
                }
                
                if (validPosition)
                {
                    obj_list.Add(newPosition);
                }
                
                attempts++;
            }
        }
        
        // Instantiate obstacles after all positions are generated
        foreach (Vector2 obj_position in obj_list)
        {
            int prefabChoice = Random.Range(0, 2);
            Quaternion randomRotation = Quaternion.Euler(0, Random.Range(0, 360),0);
            if (prefabChoice == 1)
            {
                Instantiate(prefabT, new Vector3(obj_position.x, -2f, obj_position.y), randomRotation);
            }
            else
            {
                Instantiate(prefabU, new Vector3(obj_position.x, -2f, obj_position.y), randomRotation);
            }
        }




        // Terrain subarea generation, partition into 6 area and assign color base on cost 
        // and uplift y axis for solving z fighting (here y fighting)
        float xMin = Mathf.Min(left, right);
        float xMax = Mathf.Max(left, right);
        float zMin = Mathf.Min(top, bot);
        float zMax = Mathf.Max(top, bot);
        float totalWidth = xMax - xMin;
        float totalDepth = zMax - zMin;
        if (totalWidth > 0f && totalDepth > 0f)
        {
            float cellWidth = totalWidth / 3f;
            float cellDepth = totalDepth / 2f;
            int idx = 0;
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    float cellXStart = xMin + col * cellWidth;
                    float cellZStart = zMin + row * cellDepth;
                    float centerX = cellXStart + cellWidth * 0.5f;
                    float centerZ = cellZStart + cellDepth * 0.5f;

                    GameObject area = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    area.name = $"TerrainArea_{row}_{col}";
                    area.transform.position = new Vector3(centerX, 0.01f, centerZ);
                    area.transform.localScale = new Vector3(totalWidth / 3f, totalDepth / 2f, 1f);
                    area.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                    var renderer = area.GetComponent<Renderer>();
                    float cost = Random.Range(0.5f, 5.0f);
                    if (idx < terrainCosts.Length) { terrainCosts[idx] = cost; }
                    float t = Mathf.InverseLerp(0.5f, 5.0f, cost);
                    Color areaColor = Color.Lerp(lowCostColor, highCostColor, t);
                    renderer.material.color = areaColor;

					// Create a centered label to mark terrain cost at the middle of the subterrain
					GameObject label = new GameObject($"CostLabel_{row}_{col}");
					label.transform.position = new Vector3(centerX, 0.06f, centerZ);
					label.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lay flat to face up
					var textMesh = label.AddComponent<TextMesh>();
					textMesh.text = cost.ToString("0.0");
					textMesh.anchor = TextAnchor.MiddleCenter;
					textMesh.alignment = TextAlignment.Center;
					textMesh.color = Color.black;
					textMesh.fontSize = 64;
					textMesh.characterSize = 10f;
					// Slight outline effect by adding a shadow duplicate
					GameObject shadow = new GameObject($"CostLabelShadow_{row}_{col}");
					shadow.transform.position = new Vector3(centerX + 0.02f, 0.055f, centerZ - 0.02f);
					shadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
					var shadowMesh = shadow.AddComponent<TextMesh>();
					shadowMesh.text = textMesh.text;
					shadowMesh.anchor = TextAnchor.MiddleCenter;
					shadowMesh.alignment = TextAlignment.Center;
					shadowMesh.color = new Color(0f, 0f, 0f, 0.5f);
					shadowMesh.fontSize = textMesh.fontSize;
					shadowMesh.characterSize = textMesh.characterSize;

                    idx++;
                }
            }

        }
    }

}
