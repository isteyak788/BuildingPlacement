// Assets/Scripts/LinePlacementMode.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Required for .Last() extension method
using UnityEngine.UI; // Required for UI elements like Slider, Toggle, Button
using TMPro; // Required if you're using TextMeshPro for UI text

public class LinePlacementMode : BasePlacementMode
{
    // Enum to manage the different states of line placement
    private enum LinePlacementState
    {
        Idle,           // Waiting for the first click to start drawing a line
        Drawing,        // Actively drawing the polyline (after first click, before Enter)
        Adjusting       // Polyline is finalized (Enter pressed), player is adjusting properties via UI
    }

    private LinePlacementState currentState = LinePlacementState.Idle;

    [Header("Line Placement Settings")]
    [Tooltip("The minimum length a line segment must have to place a building on it.")]
    public float minSegmentLengthForBuilding = 0.5f;
    [Tooltip("Initial spacing between buildings along the line when entering mode.")]
    public float initialBuildingSpacing = 0.1f;
    [Tooltip("Minimum allowed margin/spacing between buildings (for UI slider).")]
    public float minMargin = 0f;
    [Tooltip("Maximum allowed margin/spacing between buildings (for UI slider).")]
    public float maxMargin = 5f;
    [Tooltip("How many interpolated points to generate per segment for the curve. Higher value = smoother curve.")]
    [Range(2, 50)] // Provide a slider in inspector for easy adjustment
    public int curveResolution = 10; // Default resolution for the curve

    // Internal References for line drawing and previews
    private LineRenderer lineRenderer;
    private List<Vector3> linePoints = new List<Vector3>();
    private List<GameObject> currentBuildingPreviews = new List<GameObject>();

    // Adjustment variables - These values are controlled by the UI
    private float currentSpacing; // Controls the density/margin between buildings
    private bool buildingsRotatedRightOfLine = false; // Controls if buildings are aligned with or perpendicular to the line

    // UI References - ASSIGN THESE IN THE UNITY INSPECTOR
    [Header("UI References (Assign in Inspector)")]
    [Tooltip("The main panel GameObject for line adjustment UI.")]
    public GameObject adjustmentUIPanel;
    [Tooltip("Slider for adjusting building margin/spacing.")]
    public Slider marginSlider;
    [Tooltip("Toggle for rotating buildings to the right or left of the line.")]
    public Toggle rotationToggle;
    [Tooltip("Button to confirm adjustments and place buildings.")]
    public Button okButton;
    [Tooltip("Button to cancel adjustments and return to drawing or exit.")]
    public Button cancelButton;

    protected override void Awake()
    {
        base.Awake();

        // Get the LineRenderer component on this GameObject
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("LinePlacementMode requires a LineRenderer component on the same GameObject!");
            enabled = false; // Disable script if LineRenderer is missing
        }

        // Ensure the UI panel starts hidden when the game application begins
        if (adjustmentUIPanel != null)
        {
            adjustmentUIPanel.SetActive(false);
        }
    }

    // Subscribe to UI events when this script becomes active
    void OnEnable()
    {
        // Add listeners to UI elements for their respective events.
        // Null checks are included for robustness in case elements aren't assigned in inspector.
        if (marginSlider != null) marginSlider.onValueChanged.AddListener(OnMarginSliderChanged);
        if (rotationToggle != null) rotationToggle.onValueChanged.AddListener(OnRotationToggleChanged);
        if (okButton != null) okButton.onClick.AddListener(OnOkButtonClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelButtonClicked);
    }

    // Unsubscribe from UI events when this script becomes inactive
    // This is important to prevent memory leaks and unexpected behavior
    void OnDisable()
    {
        if (marginSlider != null) marginSlider.onValueChanged.RemoveListener(OnMarginSliderChanged);
        if (rotationToggle != null) rotationToggle.onValueChanged.RemoveListener(OnRotationToggleChanged);
        if (okButton != null) okButton.onClick.RemoveListener(OnOkButtonClicked);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(OnCancelButtonClicked);
    }

    // Called when this placement mode is activated
    public override void EnterMode(BuildingPlacementManager manager, BuildingData buildingData)
    {
        base.EnterMode(manager, buildingData); // Call base class method

        currentState = LinePlacementState.Idle; // Start in idle state
        linePoints.Clear(); // Clear any previous line points
        ClearBuildingPreviews(); // Clear any previous building previews
        
        // Enable and reset LineRenderer for new line drawing
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = true;
        }

        // Initialize adjustment variables to their default starting values
        currentSpacing = initialBuildingSpacing;
        buildingsRotatedRightOfLine = false; // Default: buildings are aligned with the line

        // Ensure the adjustment UI is hidden when first entering this mode
        if (adjustmentUIPanel != null)
        {
            adjustmentUIPanel.SetActive(false);
        }

        Debug.Log("Line Placement Mode Entered. Click to draw points. Press Enter to finalize line. Right Click to cancel current line segment drawing.");
    }

    // Called when this placement mode is deactivated
    public override void ExitMode()
    {
        base.ExitMode(); // Call base class method

        currentState = LinePlacementState.Idle; // Reset to idle state
        linePoints.Clear(); // Clear all line points
        ClearBuildingPreviews(); // Clear all building previews

        // Disable and reset LineRenderer
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }
        
        // Ensure the adjustment UI is hidden when exiting this mode
        if (adjustmentUIPanel != null)
        {
            adjustmentUIPanel.SetActive(false);
        }

        Debug.Log("Line Placement Mode Exited.");
    }

    // Main update loop for the placement mode, called by BuildingPlacementManager
    public override void UpdateMode()
    {
        UpdateMouseWorldPosition(); // Update mouse raycast hit position

        // State machine to handle different input behaviors
        switch (currentState)
        {
            case LinePlacementState.Idle:
                HandleIdleInput();
                break;
            case LinePlacementState.Drawing:
                HandleDrawingInput();
                break;
            case LinePlacementState.Adjusting:
                HandleAdjustingInput(); // Input here is primarily via UI, but also includes keyboard shortcuts
                break;
        }

        // Always update line renderer and building previews in Drawing and Adjusting states
        // In Idle state, these should be hidden/cleared
        if (currentState == LinePlacementState.Drawing || currentState == LinePlacementState.Adjusting)
        {
            UpdateLineRenderer();
            UpdateBuildingPreviews();
        }
        else // If currentState is Idle, ensure visual aids are off
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            ClearBuildingPreviews();
        }
    }

    // Handles input when in the Idle state (waiting for first point)
    private void HandleIdleInput()
    {
        if (Input.GetMouseButtonDown(0) && !_placementManager.IsPointerOverUIObject())
        {
            if (_mouseWorldPositionFound)
            {
                linePoints.Add(_mouseWorldPosition); // Add the first point
                currentState = LinePlacementState.Drawing; // Transition to Drawing state
                Debug.Log("First point placed. Continue clicking to add more points.");
            }
        }
    }

    // Handles input when in the Drawing state (adding points to the line)
    private void HandleDrawingInput()
    {
        // Left click to add subsequent points
        if (Input.GetMouseButtonDown(0) && !_placementManager.IsPointerOverUIObject())
        {
            if (_mouseWorldPositionFound)
            {
                // Only add a new point if it's sufficiently far from the last one
                if (linePoints.Count == 0 || Vector3.Distance(linePoints.Last(), _mouseWorldPosition) > minSegmentLengthForBuilding)
                {
                    linePoints.Add(_mouseWorldPosition);
                    Debug.Log($"Point {linePoints.Count} added. Current points: {linePoints.Count}. Press Enter to finalize line.");
                }
                else
                {
                    Debug.Log("Point too close to the previous one. Minimum segment length: " + minSegmentLengthForBuilding);
                }
            }
        }
        // Press Enter to finalize the line and transition to Adjustment mode
        else if (Input.GetKeyDown(KeyCode.Return))
        {
            if (linePoints.Count >= 2) // A line needs at least two points to be meaningful
            {
                currentState = LinePlacementState.Adjusting; // Transition to Adjustment state
                ShowAdjustmentUI(true); // Activate and initialize the adjustment UI
                Debug.Log("Line drawing finalized. Now in Adjustment Mode. Adjust settings using the UI and press OK or Cancel.");
            }
            else
            {
                Debug.Log("Need at least 2 points to finalize a line for placement.");
            }
        }
        // Right click to undo the last point or cancel drawing entirely
        else if (Input.GetMouseButtonDown(1) && !_placementManager.IsPointerOverUIObject())
        {
            if (linePoints.Count > 1)
            {
                linePoints.RemoveAt(linePoints.Count - 1); // Remove last point
                Debug.Log("Last point removed. Current points: " + linePoints.Count);
            }
            else if (linePoints.Count == 1) // If only one point left, clear it and go back to Idle
            {
                linePoints.Clear();
                currentState = LinePlacementState.Idle;
                Debug.Log("First point cleared. Back to Idle.");
            }
            else // No points left, cancel the entire placement process
            {
                _placementManager.CancelPlacement();
                return;
            }
        }
    }

    // Handles input when in the Adjusting state (UI is active)
    private void HandleAdjustingInput()
    {
        // Right-click to exit adjustment mode and return to drawing mode (e.g., to modify the line points)
        // We check IsPointerOverUIObject to ensure we're not clicking on UI elements if the UI is covering the mouse.
        if (Input.GetMouseButtonDown(1) && !_placementManager.IsPointerOverUIObject())
        {
            currentState = LinePlacementState.Drawing; // Go back to drawing
            ShowAdjustmentUI(false); // Hide the adjustment UI
            Debug.Log("Exited Adjustment Mode. Back to Drawing. You can add more points or press Enter to re-enter adjustment.");
        }
        // Allow pressing Enter key to confirm adjustments (same as clicking the OK button)
        // This input is specifically for UI confirmation, so no need to check IsPointerOverUIObject.
        else if (Input.GetKeyDown(KeyCode.Return))
        {
            OnOkButtonClicked(); // Call the same method that the OK button's OnClick event calls
        }
    }

    // NEW: Catmull-Rom Spline Interpolation function
    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    // NEW: Generates a list of curved points from the original input points using Catmull-Rom
    private List<Vector3> GetCurvedPoints(List<Vector3> inputPoints, int resolutionPerSegment)
    {
        List<Vector3> curvedPoints = new List<Vector3>();

        if (inputPoints.Count < 2) // Need at least two points to form a line
        {
            return new List<Vector3>(inputPoints); // Return copy of input, no curve possible
        }

        // Catmull-Rom requires 4 control points (p0, p1, p2, p3) for each segment (interpolates between p1 and p2).
        // To handle the start and end of the line smoothly, we duplicate the first and last points:
        List<Vector3> controlPoints = new List<Vector3>(inputPoints);
        controlPoints.Insert(0, inputPoints[0]); // p0 for first effective segment (input[0]-input[1])
        controlPoints.Add(inputPoints[inputPoints.Count - 1]); // p3 for last effective segment (input[n-2]-input[n-1])

        // Iterate through segments using the extended control points list
        for (int i = 0; i < controlPoints.Count - 3; i++)
        {
            Vector3 p0 = controlPoints[i];
            Vector3 p1 = controlPoints[i + 1];
            Vector3 p2 = controlPoints[i + 2];
            Vector3 p3 = controlPoints[i + 3];

            for (int j = 0; j <= resolutionPerSegment; j++)
            {
                float t = (float)j / resolutionPerSegment;
                Vector3 interpolatedPoint = CatmullRom(p0, p1, p2, p3, t);
                
                // Add points, ensuring no immediate duplicates from previous segment's end point
                if (curvedPoints.Count == 0 || Vector3.Distance(curvedPoints.Last(), interpolatedPoint) > 0.0001f)
                {
                    curvedPoints.Add(interpolatedPoint);
                }
            }
        }
        
        // Ensure the very last original point is accurately included,
        // as floating point arithmetic might cause slight deviations.
        if (inputPoints.Count > 0 && (curvedPoints.Count == 0 || Vector3.Distance(curvedPoints.Last(), inputPoints.Last()) > 0.0001f))
        {
            curvedPoints.Add(inputPoints.Last());
        }

        return curvedPoints;
    }

    // Updates the visual LineRenderer based on current line points
    private void UpdateLineRenderer()
    {
        if (lineRenderer == null) return;

        List<Vector3> pointsForCurveGeneration = new List<Vector3>(linePoints);
        // If in Drawing state, add the current mouse position to show a live segment to the mouse
        if (currentState == LinePlacementState.Drawing && _mouseWorldPositionFound && linePoints.Count > 0)
        {
            pointsForCurveGeneration.Add(_mouseWorldPosition);
        }

        // Generate the curved points to be drawn by the LineRenderer
        List<Vector3> curvedPoints = GetCurvedPoints(pointsForCurveGeneration, curveResolution);

        if (curvedPoints.Count > 0)
        {
            lineRenderer.positionCount = curvedPoints.Count;
            lineRenderer.SetPositions(curvedPoints.ToArray());
        }
        else
        {
            lineRenderer.positionCount = 0; // Hide the line if there are no points to draw
        }
    }

    // Updates the visual building previews along the drawn curved line
    private void UpdateBuildingPreviews()
    {
        ClearBuildingPreviews(); // Always clear existing previews before generating new ones

        if (linePoints.Count < 1) return; // No line points, no previews

        List<Vector3> pointsForCurveGeneration = new List<Vector3>(linePoints);
        // Include the current mouse position for live preview during the drawing phase
        if (currentState == LinePlacementState.Drawing && _mouseWorldPositionFound && linePoints.Count > 0)
        {
            pointsForCurveGeneration.Add(_mouseWorldPosition);
        }

        // Generate the curved path points
        List<Vector3> curvedPath = GetCurvedPoints(pointsForCurveGeneration, curveResolution);

        if (curvedPath.Count < 2) return; // Need at least two points to form a path for placement

        float buildingLength = _buildingData.placementFootprintSize.z;
        float buildingWidth = _buildingData.placementFootprintSize.x;
        float effectiveBuildingSize = buildingLength + currentSpacing; // Total space each building occupies

        float currentPathDistance = 0f;
        // Start placing the first building at half its effective size from the start of the path
        float nextBuildingDistanceTarget = effectiveBuildingSize / 2f; 

        // Iterate through the generated curved path segments
        for (int i = 0; i < curvedPath.Count - 1; i++)
        {
            Vector3 currentPoint = curvedPath[i];
            Vector3 nextPoint = curvedPath[i + 1];
            float segmentLength = Vector3.Distance(currentPoint, nextPoint);

            // While the accumulated distance plus the current segment's length is enough to reach the next building target
            while (currentPathDistance + segmentLength >= nextBuildingDistanceTarget)
            {
                // Calculate the exact position of the building on the current segment
                float distanceIntoSegment = nextBuildingDistanceTarget - currentPathDistance;
                Vector3 previewPosition = Vector3.Lerp(currentPoint, nextPoint, distanceIntoSegment / segmentLength);

                // Determine the forward direction for the building's rotation (tangent to the curve)
                Vector3 direction = (nextPoint - currentPoint).normalized;
                // Fallback if segment is zero length (should be rare with good resolution)
                if (direction == Vector3.zero && i > 0)
                {
                    direction = (currentPoint - curvedPath[i-1]).normalized;
                }
                if (direction == Vector3.zero) direction = Vector3.forward; // Last resort default

                // Raycast downwards to get the correct Y position on the terrain
                if (_placementManager.RaycastToTerrain(previewPosition + Vector3.up * _placementManager.terrainRaycastStartHeight, out RaycastHit hit, _placementManager.placementLayerMask))
                {
                    previewPosition = hit.point; // Set Y to terrain height
                }
                else
                {
                    // If no terrain is found below, we cannot place this building at this spot.
                    // Advance the target distance to try placing the *next* building.
                    nextBuildingDistanceTarget += effectiveBuildingSize;
                    continue; // Skip to the next iteration of the while loop
                }

                // Calculate building rotation: initially aligned with the curve tangent
                Quaternion buildingRotation = Quaternion.LookRotation(direction);

                // Apply rotation adjustment if 'buildingsRotatedRightOfLine' is true
                if (buildingsRotatedRightOfLine)
                {
                    // Rotate 90 degrees around Y (vertical axis) relative to the direction.
                    // This makes the building's "forward" perpendicular to the line.
                    buildingRotation *= Quaternion.Euler(0, 90, 0);

                    // Also apply an offset perpendicular to the line to place it to the side
                    // This moves the building half its width away from the center line.
                    Vector3 perpendicularOffset = buildingRotation * Vector3.forward * (buildingWidth / 2f);
                    previewPosition += perpendicularOffset;
                }

                // Instantiate the preview GameObject
                GameObject preview = InstantiatePreview(previewPosition, buildingRotation);
                currentBuildingPreviews.Add(preview);

                // Check if this individual preview can be placed at its current position and rotation
                bool canPlace = _placementManager.CanPlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation);
                // Set the material of the preview based on its placement validity
                SetPreviewMaterial(preview, canPlace ? _placementManager.validPlacementMaterial : _placementManager.invalidPlacementMaterial);

                // Advance the target distance for the next building placement
                nextBuildingDistanceTarget += effectiveBuildingSize;
            }
            currentPathDistance += segmentLength; // Accumulate distance for the next path segment
        }
    }

    // Destroys all current building preview GameObjects
    private void ClearBuildingPreviews()
    {
        foreach (GameObject preview in currentBuildingPreviews)
        {
            Destroy(preview);
        }
        currentBuildingPreviews.Clear();
    }

    // Places all valid buildings in the scene based on the current previews
    private void PlaceAllBuildings()
    {
        int placedCount = 0;
        foreach (GameObject preview in currentBuildingPreviews)
        {
            // Only place buildings that are currently in a valid placement position
            if (_placementManager.CanPlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation))
            {
                _placementManager.PlaceBuilding(_buildingData, preview.transform.position, preview.transform.rotation);
                placedCount++;
            }
            else
            {
                Debug.LogWarning($"Skipped placing a building at {preview.transform.position} because it's no longer valid.");
            }
        }
        Debug.Log($"Line placement finished! Placed {placedCount} buildings.");
        // After placing, we usually want to exit the entire placement mode
        _placementManager.CancelPlacement(); // This will also trigger ExitMode()
    }

    // --- UI Callbacks and Helper Methods ---

    // Controls the visibility of the adjustment UI panel and initializes its values
    private void ShowAdjustmentUI(bool show)
    {
        if (adjustmentUIPanel != null)
        {
            adjustmentUIPanel.SetActive(show); // Activate/Deactivate the UI panel GameObject

            if (show) // If showing the UI, initialize its controls
            {
                if (marginSlider != null)
                {
                    marginSlider.minValue = minMargin; // Set slider range
                    marginSlider.maxValue = maxMargin;
                    marginSlider.value = currentSpacing; // Set slider to current spacing
                }
                if (rotationToggle != null)
                {
                    rotationToggle.isOn = buildingsRotatedRightOfLine; // Set toggle to current rotation state
                }

                // Position the UI panel in the world, usually near the end of the drawn line
                if (linePoints.Count > 0)
                {
                    Vector3 uiPosition = linePoints.Last(); // Place near the end of the line

                    // Ensure the UI is placed a bit above the terrain, not inside it
                    if (_placementManager.RaycastToTerrain(uiPosition + Vector3.up * _placementManager.terrainRaycastStartHeight, out RaycastHit hit, _placementManager.placementLayerMask))
                    {
                        uiPosition.y = hit.point.y + 1.5f; // Arbitrary height above ground
                    }
                    adjustmentUIPanel.transform.position = uiPosition;
                }
            }
        }
    }

    // Public method called by the Margin Slider's On Value Changed event
    public void OnMarginSliderChanged(float value)
    {
        currentSpacing = value; // Update the spacing variable
        Debug.Log($"Margin/Density changed to: {currentSpacing:F2}");
        // UpdateBuildingPreviews() will automatically run in UpdateMode() and reflect this change
    }

    // Public method called by the Rotation Toggle's On Value Changed (Boolean) event
    public void OnRotationToggleChanged(bool isOn)
    {
        buildingsRotatedRightOfLine = isOn; // Update the rotation state
        Debug.Log($"Buildings rotated to right of line: {isOn}");
        // UpdateBuildingPreviews() will automatically run in UpdateMode() and reflect this change
    }

    // Public method called by the OK Button's OnClick event (and by pressing Enter key)
    public void OnOkButtonClicked()
    {
        Debug.Log("OK Button/Enter Key Clicked. Attempting to place buildings.");
        PlaceAllBuildings(); // Trigger the placement of buildings
        ShowAdjustmentUI(false); // Hide the UI panel after confirmation
    }

    // Public method called by the Cancel Button's OnClick event
    public void OnCancelButtonClicked()
    {
        Debug.Log("Cancel Button Clicked. Cancelling line placement.");
        ShowAdjustmentUI(false); // Hide the UI panel
        _placementManager.CancelPlacement(); // Exit the entire placement mode
    }
}
