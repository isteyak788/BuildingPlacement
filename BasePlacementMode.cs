using UnityEngine;
using UnityEngine.EventSystems; // Add this if not already there, for IsPointerOverUIObject in PlacementManager
using System.Collections.Generic; // Add this if not already there

// This abstract class provides a common interface and shared properties for all placement behaviors.
public abstract class BasePlacementMode : MonoBehaviour
{
    // These will be set by the BuildingPlacementManager when a mode is entered
    protected BuildingPlacementManager _placementManager;
    protected BuildingData _buildingData;
    protected GameObject _currentPreviewInstance; // For SinglePlacementMode or current single-building preview
    protected bool _mouseWorldPositionFound;
    protected Vector3 _mouseWorldPosition;

    // Mark Awake as virtual so derived classes can override it
    protected virtual void Awake() // <--- CHANGE THIS LINE
    {
        // Base Awake logic (if any, like getting references)
    }

    public virtual void EnterMode(BuildingPlacementManager manager, BuildingData buildingData)
    {
        _placementManager = manager;
        _buildingData = buildingData;
        
        // Optional: Initialize preview for modes that use a single preview instance immediately
        // For LinePlacementMode, we manage multiple previews internally, so this might be null here
        if (_buildingData != null && _buildingData.initialConstructionPrefab != null)
        {
            // _currentPreviewInstance = Instantiate(_buildingData.initialConstructionPrefab);
            // _currentPreviewInstance.GetComponent<Collider>().enabled = false; // Disable collider for preview
            // SetPreviewMaterial(_currentPreviewInstance, _placementManager.invalidPlacementMaterial); // Default to invalid
        }
    }

    public virtual void ExitMode()
    {
        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }
        _placementManager = null;
        _buildingData = null;
    }

    public abstract void UpdateMode(); // Must be implemented by derived classes

    // --- Helper methods (potentially from BuildingPlacementManager, copied for convenience) ---
    // These might be better if they access _placementManager.
    // However, if they are meant to be direct helpers for the mode, they fit here.

    protected void UpdateMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _placementManager.maxPlacementDistance, _placementManager.placementLayerMask))
        {
            _mouseWorldPosition = hit.point;
            _mouseWorldPositionFound = true;
        }
        else
        {
            _mouseWorldPositionFound = false;
        }
    }

    protected GameObject InstantiatePreview(Vector3 position, Quaternion rotation)
    {
        GameObject preview = Instantiate(_buildingData.initialConstructionPrefab, position, rotation);
        // Ensure its collider is disabled so it doesn't interfere with raycasts
        Collider previewCollider = preview.GetComponent<Collider>();
        if (previewCollider != null)
        {
            previewCollider.enabled = false;
        }
        return preview;
    }

    protected void SetPreviewMaterial(GameObject preview, Material material)
    {
        Renderer[] renderers = preview.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.material = material;
        }
    }

    // You might want to move CanPlaceBuilding logic directly into BuildingPlacementManager
    // and access it via _placementManager.CanPlaceBuilding()
    // For now, keeping it here for compatibility as per previous structure.
    protected bool CanPlaceBuilding(Vector3 position, Quaternion rotation)
    {
        return _placementManager.CanPlaceBuilding(_buildingData, position, rotation);
    }
}
