using UnityEngine;
using System.Collections.Generic;
using System.Linq; // For Min and Max if needed, though mostly handled by Math functions here

public class LinePlacementMode : BasePlacementMode
{
    // Define states for the line placement process
    private enum LinePlacementState
    {
        Idle,           // Waiting for the first click to start drawing
        Drawing,        // Actively drawing the polyline (after first click, before Enter)
        Adjusting       // Polyline is finalized (Enter pressed), adjusting properties (Q/E, O/P)
    }

    private LinePlacementState currentState = LinePlacementState.Idle;

    [Header("Line Placement Settings")]
    [Tooltip("The minimum length a segment must have to place a building.")]
    public float minSegmentLengthForBuilding = 0.5f;
    [Tooltip("Initial spacing between buildings along the line.")]
    public float initialBuildingSpacing = 0.1f;
    [Tooltip("How much margin changes with O/P keys.")]
    public float marginChangeAmount = 0.1f;
    [Tooltip("Minimum allowed margin between buildings.")]
    public float minMargin = 0f;
    [Tooltip("Maximum allowed margin between buildings.")]
    public float maxMargin = 5f;

    // Internal References
    private LineRenderer lineRenderer;
    private List<Vector3> linePoints = new List<Vector3>();
    private List<GameObject> currentBuildingPreviews = new List<GameObject>();

    // Adjustment variables (for Stage 2)
    private float currentSpacing; // Adjusted by O/P
    private bool buildingsRotatedRightOfLine = false; // Adjusted by Q/E

    protected override void Awake()
    {
        base.Awake(); // Calls BasePlacementMode's Awake

        // Get the LineRenderer component attached to this GameObject
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("LinePlacementMode requires a LineRenderer component on the same GameObject!");
            enabled = false; // Disable script if essential component is missing
        }
    }

    // Called when this mode is activated
    public override void EnterMode(BuildingPlacementManager manager, BuildingData buildingData)
    {
        base.EnterMode(manager, buildingData); // Call BasePlacementMode's EnterMode

        currentState = LinePlacementState.Idle; // Start fresh
        linePoints.Clear(); // Clear any old points
        ClearBuildingPreviews(); // Remove any old previews
        
        // Initialize LineRenderer
        lineRenderer.positionCount = 0;
        lineRenderer.enabled = true; // Make sure it's visible

        // Set initial spacing
        currentSpacing = initialBuildingSpacing;

        Debug.Log("Line Placement Mode Entered. Click to draw points. Press Enter to finalize line. Right Click to cancel current line segment drawing.");
    }

    // Called when this mode is deactivated
    public override void ExitMode()
    {
        base.ExitMode(); // Call BasePlacementMode's ExitMode

        currentState = LinePlacementState.Idle;
        linePoints.Clear();
        ClearBuildingPreviews();
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false; // Hide the line renderer
        }
        Debug.Log("Line Placement Mode Exited.");
    }

    // Main update loop for this mode
    public override void UpdateMode()
    {
        // Handle common BasePlacementMode logic (like raycasting for mouse position)
        UpdateMouseWorldPosition(); 

        // State-specific input handling
        switch (currentState)
        {
            case LinePlacementState.Idle:
                HandleIdleInput();
                break;
            case LinePlacementState.Drawing:
                HandleDrawingInput();
                break;
            case LinePlacementState.Adjusting:
                HandleAdjustingInput(); // Will implement in Stage 2
                break;
        }

        // Update line renderer and building previews regardless of state (if active)
        UpdateLineRenderer();
        UpdateBuildingPreviews();
    }

    private void HandleIdleInput()
    {
        if (Input.GetMouseButtonDown(0) && _placementManager.IsPointerOverUIObject() == false) // Left click
        {
            // First click, start drawing
            if (_mouseWorldPositionFound)
            {
                linePoints.Add(_mouseWorldPosition);
                currentState = LinePlacementState.Drawing;
                Debug.Log("First point placed. Continue clicking to add more points.");
            }
        }
    }

    private void HandleDrawingInput()
    {
        // Add new points on left click
        if (Input.GetMouseButtonDown(0) && _placementManager.IsPointerOverUIObject() == false) // Left click
        {
            if (_mouseWorldPositionFound)
            {
                // Ensure new point is not too close to the last one
                if (Vector3.Distance(linePoints.Last(), _mouseWorldPosition) > minSegmentLengthForBuilding)
                {
                    linePoints.Add(_mouseWorldPosition);
                    Debug.Log($"Point {linePoints.Count} added. Press Enter to finalize line.");
                }
                else
                {
                    Debug.Log("Point too close to the previous one.");
                }
            }
        }
        // Finalize line on Enter
        else if (Input.GetKeyDown(KeyCode.Return) && linePoints.Count >= 2) // Enter key
        {
            currentState = LinePlacementState.Adjusting;
            Debug.Log("Line drawing finalized. Now in Adjustment Mode (Q/E to rotate, O/P to adjust margin). Press Enter again to place buildings.");
        }
        // Cancel current line drawing on Right click
        else if (Input.GetMouseButtonDown(1) && _placementManager.IsPointerOverUIObject() == false) // Right click
        {
            // If more than one point, remove last point
            if (linePoints.Count > 1)
            {
                linePoints.RemoveAt(linePoints.Count - 1);
                Debug.Log("Last point removed.");
                if (linePoints.Count < 1) // If no points left, go back to idle
                {
                    currentState = LinePlacementState.Idle;
                    Debug.Log("All points removed. Back to Idle.");
                }
            }
            else // If only one or no points, cancel entirely
            {
                _placementManager.CancelPlacement(); // Exit the entire mode
                return;
            }
        }
    }

    private void HandleAdjustingInput()
    {
        // Implement in Stage 2: Q/E for rotation, O/P for margin
        // For now, only handle final placement
        if (Input.GetKeyDown(KeyCode.Return) && currentBuildingPreviews.Any()) // Enter key to place
        {
            PlaceAllBuildings();
            // After placing, we typically want to start a new line or exit mode
            _placementManager.CancelPlacement(); // For now, just exit the mode
        }
        // Right click to cancel the adjustment and revert to drawing or exit
        else if (Input.GetMouseButtonDown(1) && _placementManager.IsPointerOverUIObject() == false)
        {
             // If we are in adjusting, and right-click, go back to drawing.
             // This lets them add more points if they decided not to commit
            currentState = LinePlacementState.Drawing;
            Debug.Log("Exited Adjustment Mode. Back to Drawing. You can add more points or press Enter to re-enter adjustment.");
        }
    }


    private void UpdateLineRenderer()
    {
        if (lineRenderer == null) return;

        List<Vector3> pointsToDraw = new List<Vector3>(linePoints);

        // In Drawing state, add the current mouse position as a temporary last point
        if (currentState == LinePlacementState.Drawing && _mouseWorldPositionFound && linePoints.Count > 0)
        {
            pointsToDraw.Add(_mouseWorldPosition);
        }

        if (pointsToDraw.Count > 0)
        {
            lineRenderer.positionCount = pointsToDraw.Count;
            lineRenderer.SetPositions(pointsToDraw.ToArray());
        }
        else
        {
            lineRenderer.positionCount = 0;
        }
    }

    private void UpdateBuildingPreviews()
    {
        ClearBuildingPreviews(); // Start fresh for each update

        if (linePoints.Count < 1) return;

        List<Vector3> segmentPoints = new List<Vector3>(linePoints);
        // If drawing, consider the segment from last point to mouse position
        if (currentState == LinePlacementState.Drawing && _mouseWorldPositionFound && linePoints.Count > 0)
        {
            segmentPoints.Add(_mouseWorldPosition);
        }

        if (segmentPoints.Count < 2) return;

        // Iterate through all line segments
        for (int i = 0; i < segmentPoints.Count - 1; i++)
        {
            Vector3 startPoint = segmentPoints[i];
            Vector3 endPoint = segmentPoints[i + 1];

            Vector3 segmentDirection = (endPoint - startPoint);
            float segmentLength = segmentDirection.magnitude;

            if (segmentLength < minSegmentLengthForBuilding) continue;

            segmentDirection.Normalize();

            float buildingLength = _buildingData.placementFootprintSize.z;
            float buildingWidth = _buildingData.placementFootprintSize.x; // Used for side offset in Stage 2

            float effectiveBuildingSize = buildingLength + currentSpacing; // Length of building plus its trailing space

            int numBuildingsOnSegment = Mathf.FloorToInt(segmentLength / effectiveBuildingSize);

            // Calculate rotation that aligns building forward with segment
            Quaternion segmentRotation = Quaternion.LookRotation(segmentDirection);

            for (int j = 0; j < numBuildingsOnSegment; j++)
            {
                // Calculate position along the segment
                Vector3 currentBuildingLocalOffset = segmentDirection * (effectiveBuildingSize * j + effectiveBuildingSize / 2f);
                Vector3 previewPosition = startPoint + currentBuildingLocalOffset;

                // Ensure the preview is at the correct terrain height
                if (_placementManager.RaycastToTerrain(previewPosition + Vector3.up * _placementManager.terrainRaycastStartHeight, out RaycastHit hit, _placementManager.placementLayerMask))
                {
                    previewPosition = hit.point;
                }
                else
                {
                    // If no terrain found, skip this building or place it at default height
                    continue; 
                }

                // Instantiate and set up the preview
                GameObject preview = InstantiatePreview(previewPosition, segmentRotation);
                currentBuildingPreviews.Add(preview);

                // Check placement validity for this individual preview
                bool canPlace = _placementManager.CanPlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation);
                SetPreviewMaterial(preview, canPlace ? _placementManager.validPlacementMaterial : _placementManager.invalidPlacementMaterial);
            }
        }
    }

    // Clears all active building previews
    private void ClearBuildingPreviews()
    {
        foreach (GameObject preview in currentBuildingPreviews)
        {
            Destroy(preview);
        }
        currentBuildingPreviews.Clear();
    }

    // Places all buildings from the previews that are valid
    private void PlaceAllBuildings()
    {
        int placedCount = 0;
        foreach (GameObject preview in currentBuildingPreviews)
        {
            // Re-check validity just before placing
            if (_placementManager.CanPlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation))
            {
                _placementManager.PlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation);
                placedCount++;
            }
        }
        Debug.Log($"Line placement finished! Placed {placedCount} buildings.");
        ClearBuildingPreviews(); // Clear previews after placement
    }
}
