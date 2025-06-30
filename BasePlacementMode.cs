using UnityEngine;
using System.Collections.Generic; // This should already be there
using System.Linq; // Add this line

// All specific placement modes will inherit from this abstract class.
// It provides a common interface and shared properties for all placement behaviors.
public abstract class BasePlacementMode : MonoBehaviour
{
    protected BuildingPlacementManager manager;
    protected BuildingData currentBuildingData;
    protected GameObject currentPreviewBuilding; // The ghost object for the preview

    // Initialize the mode with necessary references and data
    public virtual void EnterMode(BuildingPlacementManager mgr, BuildingData data)
    {
        manager = mgr;
        currentBuildingData = data;
        // Optionally set IsPlacingBuilding in manager or handle it here if needed
        Debug.Log($"Entered placement mode: {GetType().Name} for {data.buildingName}");
    }

    // Called when this mode is exited
    public virtual void ExitMode()
    {
        if (currentPreviewBuilding != null)
        {
            Destroy(currentPreviewBuilding);
            currentPreviewBuilding = null;
        }
        currentBuildingData = null;
        manager = null;
        Debug.Log($"Exited placement mode: {GetType().Name}");
    }

    // Called every frame by the manager to update the mode's logic
    public abstract void UpdateMode();

    // Common method to create the preview building
    protected GameObject InstantiatePreview(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject preview = Instantiate(prefab, position, rotation);
        preview.name = prefab.name + " (Preview)";

        BuildingConstruction constructionScript = preview.GetComponent<BuildingConstruction>();
        if (constructionScript != null)
        {
            constructionScript.PrepareForPreview(); // Set up colliders as triggers, enable renderers
        }
        else
        {
            Debug.LogWarning($"Preview prefab '{prefab.name}' is missing a 'BuildingConstruction' script. Ensure it's attached and configured.", preview);
            foreach (Collider col in preview.GetComponentsInChildren<Collider>())
            {
                col.isTrigger = true;
            }
        }
        return preview;
    }

    // Common method to apply preview material
    protected void SetPreviewMaterial(Material mat)
    {
        if (currentPreviewBuilding == null) return;
        Renderer[] renderers = currentPreviewBuilding.GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            if (rend is MeshRenderer || rend is SkinnedMeshRenderer)
            {
                rend.material = mat;
            }
        }
    }

    // Helper to check if the mouse pointer is currently over a UI element
    protected bool IsMouseOverUI()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }
    
    // Common placement validation logic (moved from BuildingPlacementManager)
    protected bool CanPlaceBuilding(Vector3 position, Quaternion rotation, Vector3 footprintSize, int gridDensity, float maxSlopeHeight)
    {
        // --- 1. Check for Overlap with other Placed Buildings ---
        Collider previewCollider = currentPreviewBuilding.GetComponent<Collider>();
        if (previewCollider == null)
        {
            previewCollider = currentPreviewBuilding.GetComponentInChildren<Collider>();
            if (previewCollider == null)
            {
                Debug.LogWarning("Preview building has no collider (or no collider in children) for placement check! Ensure your building prefab has at least one Collider component.", currentPreviewBuilding);
                return false;
            }
        }

        Vector3 overlapBoxCenter = previewCollider.bounds.center;
        Vector3 overlapBoxHalfExtents = previewCollider.bounds.extents;

        // Using ~placementLayerMask to exclude the terrain layer from overlap checks with other buildings
        Collider[] hitColliders = Physics.OverlapBox(overlapBoxCenter, overlapBoxHalfExtents, rotation, ~manager.placementLayerMask);

        foreach (Collider col in hitColliders)
        {
            if (col.gameObject == currentPreviewBuilding || col.transform.IsChildOf(currentPreviewBuilding.transform)) continue;

            if (col.CompareTag("Building"))
            {
                return false;
            }
        }

        // --- 2. Check Terrain Validity under the Building's Full Footprint ---
        if (gridDensity <= 0) gridDensity = 1;

        System.Collections.Generic.List<float> hitYPositions = new System.Collections.Generic.List<float>();

        for (int x = 0; x < gridDensity; x++)
        {
            for (int z = 0; z < gridDensity; z++)
            {
                float normX = (gridDensity == 1) ? 0 : (x / (float)(gridDensity - 1f)) - 0.5f;
                float normZ = (gridDensity == 1) ? 0 : (z / (float)(gridDensity - 1f)) - 0.5f;

                Vector3 localOffset = new Vector3(normX * footprintSize.x, 0, normZ * footprintSize.z);
                Vector3 rotatedOffset = rotation * localOffset;

                Vector3 rayOrigin = new Vector3(position.x + rotatedOffset.x, position.y + manager.terrainRaycastStartHeight, position.z + rotatedOffset.z);

                RaycastHit terrainHit;
                if (Physics.Raycast(rayOrigin, Vector3.down, out terrainHit, manager.terrainRaycastStartHeight * 2, manager.placementLayerMask))
                {
                    hitYPositions.Add(terrainHit.point.y);
                }
                else
                {
                    return false;
                }
            }
        }

        if (hitYPositions.Count == 0)
        {
            Debug.LogWarning("No terrain hit points collected for placement check. Check gridDensity and placementLayerMask configuration.");
            return false;
        }

        float minY = hitYPositions.Min();
        float maxY = hitYPositions.Max();

        if (maxY - minY > maxSlopeHeight)
        {
            return false;
        }

        return true;
    }

    // Common method to finalize building placement
    protected void PlaceBuilding(Vector3 position, Quaternion rotation)
    {
        if (currentBuildingData == null || currentBuildingData.initialConstructionPrefab == null)
        {
            Debug.LogError("BuildingData or initialConstructionPrefab is null. Cannot place building.");
            return;
        }

        GameObject placedConstructionBuilding = Instantiate(currentBuildingData.initialConstructionPrefab, position, rotation);
        placedConstructionBuilding.name = currentBuildingData.buildingName;

        BuildingConstruction constructionScript = placedConstructionBuilding.GetComponent<BuildingConstruction>();
        if (constructionScript != null)
        {
            constructionScript.Initialize(currentBuildingData);
            constructionScript.StartConstruction();
        }
        else
        {
            Debug.LogWarning($"Placed prefab '{placedConstructionBuilding.name}' is missing a 'BuildingConstruction' script. It will appear fully built immediately and might not have proper tags/colliders.", placedConstructionBuilding);
            foreach(Transform child in placedConstructionBuilding.transform)
            {
                child.gameObject.SetActive(true);
            }
            foreach (Collider col in placedConstructionBuilding.GetComponentsInChildren<Collider>())
            {
                col.isTrigger = false;
            }
            placedConstructionBuilding.tag = "Building";
        }
        Debug.Log("Building placed successfully!");
    }
}
