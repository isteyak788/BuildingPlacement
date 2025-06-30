using UnityEngine;

public class SinglePlacementMode : BasePlacementMode
{
    private float currentRotationY = 0f; // Rotation specific to this mode

    // Override EnterMode to set up specific things for single placement
    public override void EnterMode(BuildingPlacementManager mgr, BuildingData data)
    {
        base.EnterMode(mgr, data); // Call base class EnterMode
        currentRotationY = 0f; // Reset rotation for new placement session

        // Initial preview instantiation, similar to what was in BuildingPlacementManager.StartPlacement
        Vector3 initialPlacementPos = Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, manager.maxPlacementDistance, manager.placementLayerMask))
        {
            initialPlacementPos = hit.point;
        }
        currentPreviewBuilding = InstantiatePreview(currentBuildingData.initialConstructionPrefab, initialPlacementPos, Quaternion.Euler(0, currentRotationY, 0));
        SetPreviewMaterial(manager.invalidPlacementMaterial);
    }

    public override void ExitMode()
    {
        base.ExitMode(); // Call base class ExitMode to destroy preview
    }

    public override void UpdateMode()
    {
        if (currentPreviewBuilding == null) return;

        UpdatePreviewPosition();
        HandleRotation();

        // Left mouse click to try placing the building
        if (Input.GetMouseButtonDown(0) && !IsMouseOverUI())
        {
            TryPlaceBuilding();
        }
    }

    private void UpdatePreviewPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, manager.maxPlacementDistance, manager.placementLayerMask))
        {
            currentPreviewBuilding.transform.position = hit.point;
            currentPreviewBuilding.transform.rotation = Quaternion.Euler(0, currentRotationY, 0);
            
            bool canPlace = CanPlaceBuilding(hit.point, currentPreviewBuilding.transform.rotation,
                                            currentBuildingData.placementFootprintSize,
                                            currentBuildingData.terrainCheckGridDensity,
                                            currentBuildingData.maxTerrainHeightDifference);

            SetPreviewMaterial(canPlace ? manager.validPlacementMaterial : manager.invalidPlacementMaterial);
        }
        else
        {
            currentPreviewBuilding.transform.position = new Vector3(0, -9999, 0); // Move off-screen
            SetPreviewMaterial(manager.invalidPlacementMaterial);
        }
    }

    private void HandleRotation()
    {
        float rotateInputAxis = 0f;

        if (Input.GetKey(manager.rotateLeftKey))
        {
            rotateInputAxis -= 1f;
        }
        if (Input.GetKey(manager.rotateRightKey))
        {
            rotateInputAxis += 1f;
        }
        
        if (Input.GetKey(manager.snapRotationModifierKey))
        {
            if (Input.GetKeyDown(manager.rotateLeftKey) || Input.GetKeyDown(manager.rotateRightKey))
            {
                float nearestSnap = Mathf.Round(currentRotationY / 45f) * 45f;
                if (Input.GetKeyDown(manager.rotateLeftKey))
                {
                    currentRotationY = nearestSnap - 45f;
                }
                else if (Input.GetKeyDown(manager.rotateRightKey))
                {
                    currentRotationY = nearestSnap + 45f;
                }
                currentRotationY = Mathf.Repeat(currentRotationY, 360f);
                return;
            }
        }
        
        if (rotateInputAxis != 0) 
        {
            currentRotationY += rotateInputAxis * manager.rotationSpeed * Time.deltaTime;
            currentRotationY = Mathf.Repeat(currentRotationY, 360f); 
        }
    }

    private void TryPlaceBuilding()
    {
        if (currentPreviewBuilding == null) return;

        if (CanPlaceBuilding(currentPreviewBuilding.transform.position, currentPreviewBuilding.transform.rotation,
                             currentBuildingData.placementFootprintSize,
                             currentBuildingData.terrainCheckGridDensity,
                             currentBuildingData.maxTerrainHeightDifference))
        {
            PlaceBuilding(currentPreviewBuilding.transform.position, currentPreviewBuilding.transform.rotation);
            
            // For single placement, you might want to automatically reset the mode or keep placing the same building.
            // For now, let's keep placing the same building, retaining rotation.
            // If you want to exit placement after one build, call manager.ExitPlacementMode(); here.
            
            // Re-instantiate a new preview for continuous placement
            Destroy(currentPreviewBuilding); // Destroy the old preview
            Vector3 initialPlacementPos = Vector3.zero;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, manager.maxPlacementDistance, manager.placementLayerMask))
            {
                initialPlacementPos = hit.point;
            }
            currentPreviewBuilding = InstantiatePreview(currentBuildingData.initialConstructionPrefab, initialPlacementPos, Quaternion.Euler(0, currentRotationY, 0));
            SetPreviewMaterial(manager.invalidPlacementMaterial); // New preview is initially invalid
        }
        else
        {
            Debug.Log("Cannot place building here! Invalid position, overlap with other buildings, or terrain is too sloped.");
        }
    }
}
