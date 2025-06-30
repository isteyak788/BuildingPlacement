using UnityEngine;
using UnityEngine.EventSystems; // Required for IsPointerOverUIObject

public class SinglePlacementMode : BasePlacementMode
{
    // Single placement mode specific variables (if any)
    // For now, most logic relies on base class properties

    // This method is called when SinglePlacementMode becomes the active placement mode.
    public override void EnterMode(BuildingPlacementManager manager, BuildingData buildingData)
    {
        // Call the base class's EnterMode to initialize _placementManager and _buildingData
        base.EnterMode(manager, buildingData);

        // Instantiate the preview instance for this specific mode
        // _currentPreviewInstance is a protected member from BasePlacementMode
        if (_currentPreviewInstance == null && _buildingData != null && _buildingData.initialConstructionPrefab != null)
        {
            // InstantiatePreview is a helper method provided by BasePlacementMode
            _currentPreviewInstance = InstantiatePreview(Vector3.zero, Quaternion.identity);
            // Default to invalid material until position is checked
            SetPreviewMaterial(_currentPreviewInstance, _placementManager.invalidPlacementMaterial);
        }
        else if (_currentPreviewInstance == null) // Handle cases where base.EnterMode might not set it
        {
             Debug.LogError("SinglePlacementMode: Could not create preview instance. BuildingData or prefab missing.");
             _placementManager.CancelPlacement(); // Exit mode if cannot create preview
             return;
        }

        Debug.Log("Single Placement Mode Entered.");
    }

    // This method is called when SinglePlacementMode is deactivated.
    public override void ExitMode()
    {
        // Call the base class's ExitMode to clean up _currentPreviewInstance and references
        base.ExitMode();
        Debug.Log("Single Placement Mode Exited.");
    }

    // This method is called every frame while SinglePlacementMode is active.
    public override void UpdateMode()
    {
        // Update the mouse's world position using the base class helper
        base.UpdateMouseWorldPosition();

        if (_currentPreviewInstance == null) return; // Safety check

        // If mouse position on terrain is found
        if (_mouseWorldPositionFound)
        {
            // Position the preview instance at the mouse's world position
            _currentPreviewInstance.transform.position = _mouseWorldPosition;

            // Handle rotation input (Q/E keys)
            HandleRotationInput();

            // Check if placement is valid at the current position and rotation
            // _placementManager is a protected member from BasePlacementMode
            // _buildingData is a protected member from BasePlacementMode
            bool canPlace = _placementManager.CanPlaceBuilding(_buildingData, _currentPreviewInstance.transform.position, _currentPreviewInstance.transform.rotation);
            
            // Set the preview material based on validity
            SetPreviewMaterial(_currentPreviewInstance, canPlace ? _placementManager.validPlacementMaterial : _placementManager.invalidPlacementMaterial);

            // Handle mouse click to place the building
            HandlePlacementInput(canPlace);
        }
        else
        {
            // If no valid mouse world position (e.g., mouse off terrain), hide preview or show invalid
            SetPreviewMaterial(_currentPreviewInstance, _placementManager.invalidPlacementMaterial);
            // Optionally, you might want to move the preview far away or make it invisible
            // _currentPreviewInstance.transform.position = Vector3.down * 9999f;
        }
    }

    private void HandleRotationInput()
    {
        // Rotate Left (Q key)
        if (Input.GetKey(_placementManager.rotateLeftKey))
        {
            float rotationAmount = _placementManager.rotationSpeed * Time.deltaTime;
            if (Input.GetKey(_placementManager.snapRotationModifierKey))
            {
                // Snap rotation: only apply on key down for discrete steps
                if (Input.GetKeyDown(_placementManager.rotateLeftKey))
                {
                    _currentPreviewInstance.transform.Rotate(Vector3.up, -45f, Space.World);
                }
            }
            else
            {
                _currentPreviewInstance.transform.Rotate(Vector3.up, -rotationAmount, Space.World);
            }
        }
        // Rotate Right (E key)
        else if (Input.GetKey(_placementManager.rotateRightKey))
        {
            float rotationAmount = _placementManager.rotationSpeed * Time.deltaTime;
            if (Input.GetKey(_placementManager.snapRotationModifierKey))
            {
                // Snap rotation: only apply on key down for discrete steps
                if (Input.GetKeyDown(_placementManager.rotateRightKey))
                {
                    _currentPreviewInstance.transform.Rotate(Vector3.up, 45f, Space.World);
                }
            }
            else
            {
                _currentPreviewInstance.transform.Rotate(Vector3.up, rotationAmount, Space.World);
            }
        }
    }

    private void HandlePlacementInput(bool canPlace)
    {
        // Left mouse click to place the building
        if (Input.GetMouseButtonDown(0))
        {
            // Check if pointer is not over UI and if placement is valid
            // _placementManager is a protected member from BasePlacementMode
            if (!_placementManager.IsPointerOverUIObject() && canPlace)
            {
                // Place the building using the BuildingPlacementManager's method
                _placementManager.PlaceBuilding(_buildingData, _currentPreviewInstance.transform.position, _currentPreviewInstance.transform.rotation);
                
                // After placing one building, you might want to:
                // 1. Remain in placement mode to place another (default behavior here)
                // 2. Automatically cancel placement after one is placed:
                // _placementManager.CancelPlacement();
            }
            else if (_placementManager.IsPointerOverUIObject())
            {
                // Debug.Log("Clicked on UI, not placing.");
            }
            else if (!canPlace)
            {
                Debug.Log("Cannot place building here (invalid spot).");
            }
        }
    }
}
