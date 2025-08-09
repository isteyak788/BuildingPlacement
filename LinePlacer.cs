// Assets/Scripts/LinePlacer.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Required for .Last() extension method
using UnityEngine.UI; // Required for Button
using TMPro; // Required if you're using TextMeshPro for button text
using UnityEngine.EventSystems; // Required for IsPointerOverUIObject

public class LinePlacer : MonoBehaviour
{
    // Enum to manage the different states of line placement
    private enum LinePlacementState
    {
        Idle,         // Waiting for a button click to select an object, or waiting for first click to start drawing
        DrawingLine,  // Actively drawing the polyline (after first click, before Enter)
        DrawingBox,   // Actively drawing a box (after first click, during drag)
        DrawingCircle // Actively drawing a circle (after first click to set center, during drag to set radius)
    }

    private LinePlacementState currentState = LinePlacementState.Idle;

    // New Enum to distinguish which placement mode is active (set by buttons)
    public enum ActivePlacementMode // Made public to resolve accessibility accessibility
    {
        None,
        Line,
        Box,         // Original combined Box Line and Box Fill
        LineLoop,    // From previous modification
        BoxLine,     // Separate Box Line
        BoxFill,     // Separate Box Fill
        LineLoopFill, // For line loop with fill
        LineCircle,   // For circle placement
        LineCircleFill // NEW: For circle placement with fill
    }
    private ActivePlacementMode _currentActiveMode = ActivePlacementMode.None;

    [System.Serializable]
    public class PlaceableObjectEntry
    {
        [Tooltip("The BuildingInfo ScriptableObject for this entry.")]
        public BuildingInfo buildingInfo; // Changed from GameObjects to BuildingInfo

        [Header("Activation Buttons")]
        [Tooltip("Button to activate Line Placement with buildingInfo's buildingPrefab.")]
        public Button lineButton; // RENAMED
        [Tooltip("Button to activate both Box Line and Box Fill placement simultaneously.")]
        public Button boxAndFillButton; // RENAMED
        [Tooltip("Button to activate Line Loop Placement with buildingInfo's buildingPrefab.")]
        public Button lineLoopButton; // RENAMED
        [Tooltip("Button to activate Box Line placement.")]
        public Button boxLineButton; // RENAMED
        [Tooltip("Button to activate Box Fill placement.")]
        public Button boxFillButton; // RENAMED
        [Tooltip("Button to activate Line Loop Fill placement.")]
        public Button lineLoopFillButton; // RENAMED
        [Tooltip("Button to activate Line Circle placement.")]
        public Button lineCircleButton; // RENAMED
        [Tooltip("NEW: Button to activate Line Circle Fill placement.")]
        public Button lineCircleFillButton; // NEW


        [Header("Placement Behavior")]
        [Tooltip("If true, objects of this type can overlap existing objects.")]
        public bool canOverlap = false;
        [Tooltip("If true, objects of this type occupy grid cells and cannot be placed on occupied cells.")]
        public bool occupyGrid = false;
        [Tooltip("If true, show ghost/preview objects during placement.")]
        public bool showGhost = true;
        [Tooltip("If true, allow continuous placement without re-clicking the button.")]
        public bool isContinuous = false;
    }

    [Header("Placeable Objects & Controls")]
    [Tooltip("Define each object entry with its BuildingInfo and activation buttons.")]
    public PlaceableObjectEntry[] placeableObjectEntries;

    [Header("Global Placement Settings")]
    [Tooltip("The LayerMask for terrain or ground objects to raycast against for placement.")]
    public LayerMask placementLayerMask;
    [Tooltip("The material to use for valid placement previews.")]
    public Material validPlacementMaterial;
    [Tooltip("The material to use for invalid placement previews.")]
    public Material invalidPlacementMaterial;
    [Tooltip("The height from which to start the raycast downwards to find the terrain.")]
    public float terrainRaycastStartHeight = 100f;

    [Header("Line Placement Settings")]
    [Tooltip("The minimum length a line segment must have to place an object on it.")]
    public float minSegmentLengthForObject = 0.5f;
    [Tooltip("Spacing between objects along the line.")]
    public float objectSpacing = 1.0f;
    [Tooltip("Should objects be rotated 90 degrees to the right of the line direction?")]
    public bool objectsRotatedRightOfLine = false;
    [Tooltip("How many interpolated points to generate per segment for the curve. Higher value = smoother curve.")]
    [Range(2, 50)]
    public int curveResolution = 10;
    [Tooltip("Resolution for circle placement (number of segments for the circle).")]
    [Range(10, 100)]
    public int circleResolution = 30;

    [Header("Box Draw Settings")]
    [Tooltip("The LineRenderer used to draw the box selection preview.")]
    public LineRenderer boxPreviewLineRenderer;
    [Tooltip("The minimum size (width or length) a box must be to place objects.")]
    public float minBoxSize = 1.0f;
    [Tooltip("Spacing between filled objects inside the box.")]
    public float fillObjectSpacing = 1.0f;

    [Header("Grid Settings")]
    [Tooltip("Size of each grid cell. Objects will snap to and occupy these cells.")]
    public float gridSize = 1.0f;
    [Tooltip("LayerMask to check for existing colliders when checking for overlaps.")]
    public LayerMask overlapCheckLayerMask;

    [Header("Default Mode")]
    [Tooltip("The index of the PlaceableObjectEntry to use as default.")]
    public int defaultEntryIndex = 0;
    [Tooltip("The default placement mode to activate on start.")]
    public ActivePlacementMode defaultPlacementMode = ActivePlacementMode.None;

    // Internal References for drawing and previews
    private List<Vector3> linePoints = new List<Vector3>();
    private List<GameObject> currentObjectPreviews = new List<GameObject>();
    private Vector3 _mouseWorldPosition;
    private bool _mouseWorldPositionFound;

    // Prefabs for the currently active placement mode
    private GameObject _currentActivePlacementPrefab; // The main prefab from BuildingInfo
    private GameObject _currentActiveGhostPrefab;    // The ghost prefab from BuildingInfo

    // Current entry whose button was clicked to activate a mode
    private PlaceableObjectEntry _currentSelectedEntry;

    // Box Draw Specific Variables
    private Vector3 _boxDrawStartPoint;
    private bool _isDraggingBox = false;

    // Circle Draw Specific Variables
    private Vector3 _circleCenter;
    private float _circleRadius;
    private bool _isDrawingCircle = false;


    // Grid occupation tracking
    private HashSet<Vector2Int> _occupiedGridCells = new HashSet<Vector2Int>();


    void Awake()
    {
        LineRenderer internalLineRenderer = GetComponent<LineRenderer>();
        if (internalLineRenderer == null)
        {
            internalLineRenderer = gameObject.AddComponent<LineRenderer>();
            internalLineRenderer.startWidth = 0.1f;
            internalLineRenderer.endWidth = 0.1f;
            internalLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            internalLineRenderer.startColor = Color.clear;
            internalLineRenderer.endColor = Color.clear;
        }
        internalLineRenderer.enabled = false;

        if (boxPreviewLineRenderer != null)
        {
            boxPreviewLineRenderer.positionCount = 0;
            boxPreviewLineRenderer.enabled = false;
        }
        else
        {
            Debug.LogWarning("Box Preview Line Renderer is not assigned! Box draw preview will not work.");
        }
    }

    void OnEnable()
    {
        foreach (var entry in placeableObjectEntries)
        {
            // Ensure BuildingInfo and its prefabs are valid before enabling buttons
            bool hasValidBuildingPrefab = entry.buildingInfo != null && entry.buildingInfo.buildingPrefab != null;
            bool hasValidGhostPrefab = entry.buildingInfo != null && entry.buildingInfo.buildingGhostPrefab != null;

            if (entry.lineButton != null) // RENAMED
            {
                entry.lineButton.onClick.RemoveAllListeners();
                entry.lineButton.onClick.AddListener(() => ActivateLineMode(entry));
                entry.lineButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.boxAndFillButton != null) // RENAMED
            {
                entry.boxAndFillButton.onClick.RemoveAllListeners();
                entry.boxAndFillButton.onClick.AddListener(() => ActivateBoxMode(entry));
                entry.boxAndFillButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.lineLoopButton != null) // RENAMED
            {
                entry.lineLoopButton.onClick.RemoveAllListeners();
                entry.lineLoopButton.onClick.AddListener(() => ActivateLineLoopMode(entry));
                entry.lineLoopButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.boxLineButton != null) // RENAMED
            {
                entry.boxLineButton.onClick.RemoveAllListeners();
                entry.boxLineButton.onClick.AddListener(() => ActivateBoxLineMode(entry));
                entry.boxLineButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.boxFillButton != null) // RENAMED
            {
                entry.boxFillButton.onClick.RemoveAllListeners();
                entry.boxFillButton.onClick.AddListener(() => ActivateBoxFillMode(entry));
                entry.boxFillButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.lineLoopFillButton != null) // RENAMED
            {
                entry.lineLoopFillButton.onClick.RemoveAllListeners();
                entry.lineLoopFillButton.onClick.AddListener(() => ActivateLineLoopFillMode(entry));
                entry.lineLoopFillButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.lineCircleButton != null) // RENAMED
            {
                entry.lineCircleButton.onClick.RemoveAllListeners();
                entry.lineCircleButton.onClick.AddListener(() => ActivateLineCircleMode(entry));
                entry.lineCircleButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
            if (entry.lineCircleFillButton != null) // NEW
            {
                entry.lineCircleFillButton.onClick.RemoveAllListeners();
                entry.lineCircleFillButton.onClick.AddListener(() => ActivateLineCircleFillMode(entry));
                entry.lineCircleFillButton.interactable = hasValidBuildingPrefab || hasValidGhostPrefab;
            }
        }

        ExitPlacementMode();

        // Activate default mode if set
        if (defaultPlacementMode != ActivePlacementMode.None && defaultEntryIndex >= 0 && defaultEntryIndex < placeableObjectEntries.Length)
        {
            PlaceableObjectEntry defaultEntry = placeableObjectEntries[defaultEntryIndex];
            if (defaultEntry.buildingInfo != null && defaultEntry.buildingInfo.buildingPrefab != null)
            {
                if (defaultPlacementMode == ActivePlacementMode.Line)
                {
                    ActivateLineMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.Box)
                {
                    ActivateBoxMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.LineLoop)
                {
                    ActivateLineLoopMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.BoxLine)
                {
                    ActivateBoxLineMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.BoxFill)
                {
                    ActivateBoxFillMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.LineLoopFill)
                {
                    ActivateLineLoopFillMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.LineCircle)
                {
                    ActivateLineCircleMode(defaultEntry);
                }
                else if (defaultPlacementMode == ActivePlacementMode.LineCircleFill) // NEW
                {
                    ActivateLineCircleFillMode(defaultEntry);
                }
            }
        }
    }

    void OnDisable()
    {
        foreach (var entry in placeableObjectEntries)
        {
            if (entry.lineButton != null) entry.lineButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.boxAndFillButton != null) entry.boxAndFillButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.lineLoopButton != null) entry.lineLoopButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.boxLineButton != null) entry.boxLineButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.boxFillButton != null) entry.boxFillButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.lineLoopFillButton != null) entry.lineLoopFillButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.lineCircleButton != null) entry.lineCircleButton.onClick.RemoveAllListeners(); // RENAMED
            if (entry.lineCircleFillButton != null) entry.lineCircleFillButton.onClick.RemoveAllListeners(); // NEW
        }

        ExitPlacementMode();
    }

    // Common setup for activating any placement mode
    private void StartPlacementSetup(PlaceableObjectEntry entry, ActivePlacementMode mode)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogError($"Cannot start placement for {entry.buildingInfo?.buildingName ?? "null entry"}: BuildingInfo or its prefabs are missing!");
            return;
        }

        // Handle continuous mode deactivation if the same button is pressed
        if (_currentActiveMode == mode && _currentSelectedEntry == entry && entry.isContinuous)
        {
            Debug.Log($"Deactivating continuous {mode} placement for {entry.buildingInfo.buildingName}.");
            ExitPlacementMode();
            return;
        }

        // Exit any currently active mode before starting a new one
        if (_currentActiveMode != ActivePlacementMode.None)
        {
            ExitPlacementMode();
        }

        _currentSelectedEntry = entry;
        _currentActiveMode = mode;

        // Assign prefabs from BuildingInfo
        _currentActivePlacementPrefab = entry.buildingInfo.buildingPrefab;
        _currentActiveGhostPrefab = entry.buildingInfo.buildingGhostPrefab;

        currentState = LinePlacementState.Idle;
        linePoints.Clear();
        DisableVisualAids();
        _isDrawingCircle = false;
        _circleRadius = 0;

        SetAllPlacementButtonsInteractable(false); // Disable all buttons once a mode is active

        string debugMsg = $"Started {_currentActiveMode} placement for {entry.buildingInfo.buildingName}.";
        if (_currentActiveMode == ActivePlacementMode.Line || _currentActiveMode == ActivePlacementMode.LineLoop || _currentActiveMode == ActivePlacementMode.LineLoopFill)
        {
            debugMsg += " Click to add points. Press Enter to finalize. Right Click to cancel.";
            if (_currentActiveMode == ActivePlacementMode.LineLoop)
            {
                debugMsg += " (Will form a loop)";
            }
            if (_currentActiveMode == ActivePlacementMode.LineLoopFill)
            {
                debugMsg += " (Will form a loop and fill the interior)";
            }
        }
        else if (_currentActiveMode == ActivePlacementMode.Box || _currentActiveMode == ActivePlacementMode.BoxLine || _currentActiveMode == ActivePlacementMode.BoxFill)
        {
            debugMsg += " Click and Drag to define box, release to place. Right Click to cancel.";
        }
        else if (_currentActiveMode == ActivePlacementMode.LineCircle)
        {
            debugMsg += " Click to set center, drag to set radius, click again to finalize. Right Click to cancel.";
        }
        else if (_currentActiveMode == ActivePlacementMode.LineCircleFill) // NEW
        {
            debugMsg += " Click to set center, drag to set radius, click again to finalize. Will fill the interior.";
        }

        if (entry.isContinuous) debugMsg += " (Continuous Mode: click button again to deactivate.)";
        Debug.Log(debugMsg);
    }

    public void ActivateLineMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Line object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.Line);
    }

    public void ActivateBoxMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Box object prefab is null for this entry's BuildingInfo. Cannot activate Box mode.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.Box);
    }

    public void ActivateLineLoopMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Line Loop object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.LineLoop);
    }

    public void ActivateBoxLineMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Box Line object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.BoxLine);
    }

    public void ActivateBoxFillMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Box Fill object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.BoxFill);
    }

    public void ActivateLineLoopFillMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Line Loop Fill object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.LineLoopFill);
    }

    public void ActivateLineCircleMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Line Circle object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.LineCircle);
    }

    // NEW: Activate Line Circle Fill Placement Mode
    public void ActivateLineCircleFillMode(PlaceableObjectEntry entry)
    {
        if (entry.buildingInfo == null || (entry.buildingInfo.buildingPrefab == null && entry.buildingInfo.buildingGhostPrefab == null))
        {
            Debug.LogWarning("Line Circle Fill object prefab is null for this entry's BuildingInfo.");
            return;
        }
        StartPlacementSetup(entry, ActivePlacementMode.LineCircleFill);
    }

    private void DisableVisualAids()
    {
        if (boxPreviewLineRenderer != null) boxPreviewLineRenderer.enabled = false;
        if (boxPreviewLineRenderer != null) boxPreviewLineRenderer.positionCount = 0;
        ClearObjectPreviews();
    }

    public void ExitPlacementMode()
    {
        currentState = LinePlacementState.Idle;
        _currentActiveMode = ActivePlacementMode.None;
        _currentSelectedEntry = null;
        _currentActivePlacementPrefab = null;
        _currentActiveGhostPrefab = null;
        linePoints.Clear();
        DisableVisualAids();
        _isDraggingBox = false;
        _isDrawingCircle = false;
        _circleRadius = 0;

        SetAllPlacementButtonsInteractable(true);

        Debug.Log("Placement Mode Exited. Select a placement option via buttons.");
    }

    void Update()
    {
        UpdateMouseWorldPosition();

        switch (currentState)
        {
            case LinePlacementState.Idle:
                if (_currentActiveMode != ActivePlacementMode.None)
                {
                    HandleInitialInput();
                }
                break;
            case LinePlacementState.DrawingLine:
                HandleDrawingLineInput();
                UpdateLineObjectPreviews(false);
                break;
            case LinePlacementState.DrawingBox:
                HandleDrawingBoxInput();
                UpdateBoxPreview();
                UpdateBoxObjectPreviews();
                break;
            case LinePlacementState.DrawingCircle:
                HandleDrawingCircleInput();
                UpdateCircleObjectPreviews();
                break;
        }
    }

    private void UpdateMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, placementLayerMask))
        {
            _mouseWorldPosition = hit.point;
            _mouseWorldPositionFound = true;
        }
        else
        {
            _mouseWorldPositionFound = false;
        }
    }

    private bool IsPointerOverUIObject()
    {
        PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
        eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
        return results.Count > 0;
    }

    private void HandleInitialInput()
    {
        if (IsPointerOverUIObject()) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (_mouseWorldPositionFound)
            {
                if (_currentActiveMode == ActivePlacementMode.Box || _currentActiveMode == ActivePlacementMode.BoxLine || _currentActiveMode == ActivePlacementMode.BoxFill)
                {
                    _boxDrawStartPoint = _mouseWorldPosition;
                    _isDraggingBox = true;
                    currentState = LinePlacementState.DrawingBox;
                    Debug.Log("Started box drawing. Drag to define area, release to place.");
                    if (boxPreviewLineRenderer != null) boxPreviewLineRenderer.enabled = true;
                }
                else if (_currentActiveMode == ActivePlacementMode.Line || _currentActiveMode == ActivePlacementMode.LineLoop || _currentActiveMode == ActivePlacementMode.LineLoopFill)
                {
                    linePoints.Add(_mouseWorldPosition);
                    currentState = LinePlacementState.DrawingLine;
                    Debug.Log("First line point placed. Continue clicking to add more points.");
                }
                else if (_currentActiveMode == ActivePlacementMode.LineCircle || _currentActiveMode == ActivePlacementMode.LineCircleFill) // NEW
                {
                    _circleCenter = _mouseWorldPosition;
                    _isDrawingCircle = true;
                    currentState = LinePlacementState.DrawingCircle;
                    Debug.Log("Circle center set. Drag mouse to define radius, then click again to finalize.");
                }
                else
                {
                    Debug.LogWarning("No active placement mode. Please select an option first.");
                    ExitPlacementMode();
                }
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            ExitPlacementMode();
        }
    }

    private void HandleDrawingLineInput()
    {
        if (Input.GetMouseButtonDown(0) && !IsPointerOverUIObject())
        {
            if (_mouseWorldPositionFound)
            {
                if (linePoints.Count == 0 || Vector3.Distance(linePoints.Last(), _mouseWorldPosition) > minSegmentLengthForObject)
                {
                    linePoints.Add(_mouseWorldPosition);
                    Debug.Log($"Point {linePoints.Count} added. Current points: {linePoints.Count}. Press Enter to finalize line.");
                }
                else
                {
                    Debug.Log("Point too close to the previous one. Minimum segment length: " + minSegmentLengthForObject);
                }
            }
        }
        else if (Input.GetKeyDown(KeyCode.Return))
        {
            if (linePoints.Count >= 2)
            {
                if (_currentActiveMode == ActivePlacementMode.Line)
                {
                    UpdateLineObjectPreviews(true); // Recalculate previews without the mouse-dragged point
                    PlaceAllObjectsFromLine();
                }
                else if (_currentActiveMode == ActivePlacementMode.LineLoop)
                {
                    if (linePoints.Count >= 3) // Need at least 3 points for a meaningful loop
                    {
                        UpdateLineObjectPreviews(true); // Update previews with the closing segment
                        PlaceAllObjectsFromLineLoop();
                    }
                    else
                    {
                        Debug.Log("Need at least 3 points to finalize a line loop for placement.");
                    }
                }
                else if (_currentActiveMode == ActivePlacementMode.LineLoopFill)
                {
                    if (linePoints.Count >= 3)
                    {
                        // Update previews with the closing segment and fill
                        UpdateLineObjectPreviews(true); // This will show the perimeter, filling needs separate logic
                        PlaceAllObjectsFromLineLoopFill();
                    }
                    else
                    {
                        Debug.Log("Need at least 3 points to finalize a line loop fill for placement.");
                    }
                }
            }
            else
            {
                Debug.Log("Need at least 2 points to finalize a line for placement.");
            }
        }
        else if (Input.GetMouseButtonDown(1) && !IsPointerOverUIObject())
        {
            if (linePoints.Count > 1)
            {
                linePoints.RemoveAt(linePoints.Count - 1);
                Debug.Log("Last point removed. Current points: " + linePoints.Count);
            }
            else if (linePoints.Count == 1)
            {
                linePoints.Clear();
                currentState = LinePlacementState.Idle;
                Debug.Log("First point cleared. Back to waiting for first point.");
            }
            else
            {
                ExitPlacementMode();
            }
        }
    }

    private void HandleDrawingBoxInput()
    {
        if (IsPointerOverUIObject()) return;

        if (Input.GetMouseButtonUp(0))
        {
            _isDraggingBox = false;
            if (_mouseWorldPositionFound)
            {
                Vector3 currentMousePoint = _mouseWorldPosition;
                Vector3 minCorner = new Vector3(Mathf.Min(_boxDrawStartPoint.x, currentMousePoint.x), _boxDrawStartPoint.y, Mathf.Min(_boxDrawStartPoint.z, currentMousePoint.z));
                Vector3 maxCorner = new Vector3(Mathf.Max(_boxDrawStartPoint.x, currentMousePoint.x), _boxDrawStartPoint.y, Mathf.Max(_boxDrawStartPoint.z, currentMousePoint.z));

                float boxWidth = Mathf.Abs(maxCorner.x - minCorner.x);
                float boxLength = Mathf.Abs(maxCorner.z - minCorner.z);

                if (boxWidth >= minBoxSize && boxLength >= minBoxSize)
                {
                    if (_currentActiveMode == ActivePlacementMode.Box)
                    {
                        PlaceAllObjectsFromCombinedBox(minCorner, maxCorner);
                    }
                    else if (_currentActiveMode == ActivePlacementMode.BoxLine)
                    {
                        PlaceAllObjectsFromBoxLine(minCorner, maxCorner);
                    }
                    else if (_currentActiveMode == ActivePlacementMode.BoxFill)
                    {
                        PlaceAllObjectsFromBoxFill(minCorner, maxCorner);
                    }
                }
                else
                {
                    Debug.LogWarning($"Box too small. Minimum size: {minBoxSize}x{minBoxSize}. Current: {boxWidth:F2}x{boxLength:F2}.");
                    ExitPlacementMode();
                }
            }
            else
            {
                ExitPlacementMode();
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            ExitPlacementMode();
        }
    }

    // Handle drawing a circle
    private void HandleDrawingCircleInput()
    {
        if (IsPointerOverUIObject()) return;

        if (_mouseWorldPositionFound)
        {
            _circleRadius = Vector3.Distance(_circleCenter, _mouseWorldPosition);
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (_mouseWorldPositionFound)
            {
                if (_circleRadius > minBoxSize) // Reuse minBoxSize as minCircleRadius
                {
                    if (_currentActiveMode == ActivePlacementMode.LineCircle)
                    {
                        PlaceObjectsInCircle(); // Finalize and place circle objects
                    }
                    else if (_currentActiveMode == ActivePlacementMode.LineCircleFill) // NEW
                    {
                        PlaceAllObjectsFromLineCircleFill(); // Finalize and place circle fill objects
                    }
                }
                else
                {
                    Debug.LogWarning($"Circle radius too small. Minimum radius: {minBoxSize}. Current: {_circleRadius:F2}.");
                    ExitPlacementMode();
                }
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            ExitPlacementMode();
        }
    }


    private void UpdateBoxPreview()
    {
        if (boxPreviewLineRenderer == null || !_isDraggingBox || !_mouseWorldPositionFound)
        {
            if (boxPreviewLineRenderer != null) boxPreviewLineRenderer.enabled = false;
            return;
        }

        boxPreviewLineRenderer.enabled = true;

        Vector3 currentMousePoint = _mouseWorldPosition;
        float previewY = _boxDrawStartPoint.y;

        Vector3 p1 = new Vector3(_boxDrawStartPoint.x, previewY, _boxDrawStartPoint.z);
        Vector3 p2 = new Vector3(currentMousePoint.x, previewY, _boxDrawStartPoint.z);
        Vector3 p3 = new Vector3(currentMousePoint.x, previewY, currentMousePoint.z);
        Vector3 p4 = new Vector3(_boxDrawStartPoint.x, previewY, currentMousePoint.z);

        RaycastHit hit;
        if (RaycastToTerrain(p1 + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask)) p1.y = hit.point.y;
        if (RaycastToTerrain(p2 + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask)) p2.y = hit.point.y;
        if (RaycastToTerrain(p3 + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask)) p3.y = hit.point.y;
        if (RaycastToTerrain(p4 + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask)) p4.y = hit.point.y;

        Vector3[] corners = new Vector3[] { p1, p2, p3, p4, p1 };
        boxPreviewLineRenderer.positionCount = corners.Length;
        boxPreviewLineRenderer.SetPositions(corners);
    }

    // Updates live previews for objects within the box during drag
    private void UpdateBoxObjectPreviews()
    {
        ClearObjectPreviews();

        if (_currentSelectedEntry == null || !_currentSelectedEntry.showGhost || _currentActiveGhostPrefab == null || !_isDraggingBox || !_mouseWorldPositionFound) return;

        Vector3 currentMousePoint = _mouseWorldPosition;
        Vector3 minCorner = new Vector3(Mathf.Min(_boxDrawStartPoint.x, currentMousePoint.x), _boxDrawStartPoint.y, Mathf.Min(_boxDrawStartPoint.z, currentMousePoint.z));
        Vector3 maxCorner = new Vector3(Mathf.Max(_boxDrawStartPoint.x, currentMousePoint.x), _boxDrawStartPoint.y, Mathf.Max(_boxDrawStartPoint.z, currentMousePoint.z));

        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        // Generate and show previews based on which prefabs are assigned for the current entry
        // In this setup, _currentActiveGhostPrefab serves for both line and fill previews if it's assigned
        if (_currentActiveMode == ActivePlacementMode.Box || _currentActiveMode == ActivePlacementMode.BoxLine)
        {
            GenerateBoxLinePreviews(minCorner, maxCorner, _currentActiveGhostPrefab, boundsExtents, objectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
        }
        if (_currentActiveMode == ActivePlacementMode.Box || _currentActiveMode == ActivePlacementMode.BoxFill)
        {
            GenerateBoxFillPreviews(minCorner, maxCorner, _currentActiveGhostPrefab, boundsExtents, fillObjectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
        }
    }

    // Updates live previews for objects along the circumference of a circle
    private void UpdateCircleObjectPreviews()
    {
        ClearObjectPreviews();

        if (_currentSelectedEntry == null || !_currentSelectedEntry.showGhost || _currentActiveGhostPrefab == null || !_isDrawingCircle || !_mouseWorldPositionFound || _circleRadius <= 0) return;

        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + objectSpacing;

        // Calculate circumference
        float circumference = 2 * Mathf.PI * _circleRadius;
        int numberOfObjects = Mathf.FloorToInt(circumference / effectiveObjectSize);
        if (numberOfObjects == 0) return;

        float angleStep = 360f / numberOfObjects;

        for (int i = 0; i < numberOfObjects; i++)
        {
            float angle = i * angleStep;
            Vector3 pointOnCircle = _circleCenter + new Vector3(Mathf.Cos(Mathf.Deg2Rad * angle), 0, Mathf.Sin(Mathf.Deg2Rad * angle)) * _circleRadius;

            RaycastHit hit;
            if (RaycastToTerrain(pointOnCircle + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
            {
                pointOnCircle.y = hit.point.y;
            }
            else
            {
                continue;
            }

            Vector3 direction = (pointOnCircle - _circleCenter).normalized; // Direction from center to object
            Quaternion objectRotation = Quaternion.LookRotation(direction);

            if (objectsRotatedRightOfLine)
            {
                objectRotation *= Quaternion.Euler(0, 90, 0); // Rotate 90 degrees relative to the radial direction
                Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                pointOnCircle += perpendicularOffset;
            }

            bool isValid = IsValidPlacement(pointOnCircle, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);

            GameObject preview = Instantiate(_currentActiveGhostPrefab, pointOnCircle, objectRotation);
            currentObjectPreviews.Add(preview);
            SetPreviewMaterial(preview, isValid ? validPlacementMaterial : invalidPlacementMaterial);
        }

        // NEW: If LineCircleFill, also show a preview of the fill.
        if (_currentActiveMode == ActivePlacementMode.LineCircleFill)
        {
            GenerateCircleFillPreviews(_circleCenter, _circleRadius, _currentActiveGhostPrefab, boundsExtents, fillObjectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
        }
    }

    // Helper to generate previews along a box's perimeter
    private void GenerateBoxLinePreviews(Vector3 minCorner, Vector3 maxCorner, GameObject prefab, Vector3 boundsExtents, float spacing, bool canOverlap, bool occupyGrid)
    {
        if (prefab == null) return;

        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + spacing;

        List<Vector3> boxCorners = new List<Vector3>()
        {
            new Vector3(minCorner.x, 0, minCorner.z),
            new Vector3(maxCorner.x, 0, minCorner.z),
            new Vector3(maxCorner.x, 0, maxCorner.z),
            new Vector3(minCorner.x, 0, maxCorner.z),
            new Vector3(minCorner.x, 0, minCorner.z)
        };

        float currentPathDistance = 0f;
        float nextObjectDistanceTarget = effectiveObjectSize / 2f;

        for (int i = 0; i < boxCorners.Count - 1; i++)
        {
            Vector3 currentSegmentStart = boxCorners[i];
            Vector3 currentSegmentEnd = boxCorners[i + 1];
            float segmentLength = Vector3.Distance(currentSegmentStart, currentSegmentEnd);

            if (segmentLength < 0.001f) continue;

            RaycastHit hitStart, hitEnd;
            if (RaycastToTerrain(currentSegmentStart + Vector3.up * terrainRaycastStartHeight, out hitStart, placementLayerMask))
                currentSegmentStart.y = hitStart.point.y;
            if (RaycastToTerrain(currentSegmentEnd + Vector3.up * terrainRaycastStartHeight, out hitEnd, placementLayerMask))
                currentSegmentEnd.y = hitEnd.point.y;

            while (currentPathDistance + segmentLength >= nextObjectDistanceTarget)
            {
                float distanceIntoSegment = nextObjectDistanceTarget - currentPathDistance;
                Vector3 previewPosition = Vector3.Lerp(currentSegmentStart, currentSegmentEnd, distanceIntoSegment / segmentLength);

                RaycastHit hit;
                if (RaycastToTerrain(previewPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    previewPosition = hit.point;
                }
                else
                {
                    nextObjectDistanceTarget += effectiveObjectSize;
                    continue;
                }

                Vector3 direction = (currentSegmentEnd - currentSegmentStart).normalized;
                if (direction == Vector3.zero) direction = Vector3.forward;
                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    previewPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(previewPosition, boundsExtents, canOverlap, occupyGrid);

                GameObject preview = Instantiate(prefab, previewPosition, objectRotation);
                currentObjectPreviews.Add(preview);
                SetPreviewMaterial(preview, isValid ? validPlacementMaterial : invalidPlacementMaterial);

                nextObjectDistanceTarget += effectiveObjectSize;
            }
            currentPathDistance += segmentLength;
        }
    }

    // Helper to generate previews for filling a box area
    private void GenerateBoxFillPreviews(Vector3 minCorner, Vector3 maxCorner, GameObject prefab, Vector3 boundsExtents, float spacing, bool canOverlap, bool occupyGrid)
    {
        if (prefab == null) return;

        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSizeZ = objectLength + spacing;
        float effectiveObjectSizeX = objectWidth + spacing;

        for (float x = minCorner.x + effectiveObjectSizeX / 2f; x < maxCorner.x; x += effectiveObjectSizeX)
        {
            for (float z = minCorner.z + effectiveObjectSizeZ / 2f; z < maxCorner.z; z += effectiveObjectSizeZ)
            {
                Vector3 placementPosition = new Vector3(x, minCorner.y, z);

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition = hit.point;
                }
                else
                {
                    continue;
                }

                Quaternion objectRotation = prefab.transform.rotation;
                if (objectsRotatedRightOfLine)
                {
                    objectRotation = Quaternion.Euler(0, 90, 0);
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, canOverlap, occupyGrid);

                GameObject preview = Instantiate(prefab, placementPosition, objectRotation);
                currentObjectPreviews.Add(preview);
                SetPreviewMaterial(preview, isValid ? validPlacementMaterial : invalidPlacementMaterial);
            }
        }
    }

    // NEW: Helper to generate previews for filling a circle area
    private void GenerateCircleFillPreviews(Vector3 center, float radius, GameObject prefab, Vector3 boundsExtents, float spacing, bool canOverlap, bool occupyGrid)
    {
        if (prefab == null) return;

        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSizeZ = objectLength + spacing;
        float effectiveObjectSizeX = objectWidth + spacing;

        // Iterate through a square bounding box of the circle
        for (float x = center.x - radius; x <= center.x + radius; x += effectiveObjectSizeX)
        {
            for (float z = center.z - radius; z <= center.z + radius; z += effectiveObjectSizeZ)
            {
                Vector3 potentialPlacement = new Vector3(x + effectiveObjectSizeX / 2f, center.y, z + effectiveObjectSizeZ / 2f);

                // Check if the potential placement is within the circle's radius
                if (Vector3.Distance(center, potentialPlacement) <= radius - (effectiveObjectSizeX / 2f)) // Subtract half object size for better fit
                {
                    RaycastHit hit;
                    if (RaycastToTerrain(potentialPlacement + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                    {
                        potentialPlacement.y = hit.point.y;
                    }
                    else
                    {
                        continue;
                    }

                    Quaternion objectRotation = prefab.transform.rotation; // Keep default rotation for fill
                    // If objectsRotatedRightOfLine, maybe orient fill objects to radial direction?
                    // For now, keep it simple for fill:
                    // if (objectsRotatedRightOfLine)
                    // {
                    //     Vector3 direction = (potentialPlacement - center).normalized;
                    //     if (direction == Vector3.zero) direction = Vector3.forward;
                    //     objectRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 90, 0);
                    // }

                    bool isValid = IsValidPlacement(potentialPlacement, boundsExtents, canOverlap, occupyGrid);

                    GameObject preview = Instantiate(prefab, potentialPlacement, objectRotation);
                    currentObjectPreviews.Add(preview);
                    SetPreviewMaterial(preview, isValid ? validPlacementMaterial : invalidPlacementMaterial);
                }
            }
        }
    }

    // Catmull-Rom Spline Interpolation function
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

    // Generates a list of curved points from the original input points using Catmull-Rom
    private List<Vector3> GetCurvedPoints(List<Vector3> inputPoints, int resolutionPerSegment)
    {
        List<Vector3> curvedPoints = new List<Vector3>();

        if (inputPoints.Count < 2)
        {
            return new List<Vector3>(inputPoints);
        }

        List<Vector3> controlPoints = new List<Vector3>(inputPoints);
        controlPoints.Insert(0, inputPoints[0]);
        controlPoints.Add(inputPoints[inputPoints.Count - 1]);

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

                if (curvedPoints.Count == 0 || Vector3.Distance(curvedPoints.Last(), interpolatedPoint) > 0.0001f)
                {
                    curvedPoints.Add(interpolatedPoint);
                }
            }
        }

        if (inputPoints.Count > 0 && (curvedPoints.Count == 0 || Vector3.Distance(curvedPoints.Last(), inputPoints.Last()) > 0.0001f))
        {
            curvedPoints.Add(inputPoints.Last());
        }

        return curvedPoints;
    }

    // Updates the visual object previews along the drawn curved line
    private void UpdateLineObjectPreviews(bool isFinalPlacement)
    {
        ClearObjectPreviews();

        if (_currentSelectedEntry == null || !_currentSelectedEntry.showGhost || _currentActiveGhostPrefab == null || linePoints.Count < 1) return;

        List<Vector3> pointsForCurveGeneration = new List<Vector3>(linePoints);
        if (!isFinalPlacement && _mouseWorldPositionFound && linePoints.Count > 0)
        {
            pointsForCurveGeneration.Add(_mouseWorldPosition);
        }

        // Add the closing segment for LineLoop previews
        if ((_currentActiveMode == ActivePlacementMode.LineLoop || _currentActiveMode == ActivePlacementMode.LineLoopFill) && linePoints.Count >= 2)
        {
            pointsForCurveGeneration.Add(linePoints[0]); // Add the first point to close the loop for preview
        }


        List<Vector3> curvedPath = GetCurvedPoints(pointsForCurveGeneration, curveResolution);

        if (curvedPath.Count < 2) return;

        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + objectSpacing;

        float currentPathDistance = 0f;
        float nextObjectDistanceTarget = effectiveObjectSize / 2f;

        for (int i = 0; i < curvedPath.Count - 1; i++)
        {
            Vector3 currentPoint = curvedPath[i];
            Vector3 nextPoint = curvedPath[i + 1];
            float segmentLength = Vector3.Distance(currentPoint, nextPoint);

            while (currentPathDistance + segmentLength >= nextObjectDistanceTarget)
            {
                float distanceIntoSegment = nextObjectDistanceTarget - currentPathDistance;
                Vector3 previewPosition = Vector3.Lerp(currentPoint, nextPoint, distanceIntoSegment / segmentLength);

                Vector3 direction = (nextPoint - currentPoint).normalized;
                if (direction == Vector3.zero && i > 0)
                {
                    direction = (currentPoint - curvedPath[i - 1]).normalized;
                }
                if (direction == Vector3.zero) direction = Vector3.forward;

                RaycastHit hit;
                if (RaycastToTerrain(previewPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    previewPosition = hit.point;
                }
                else
                {
                    nextObjectDistanceTarget += effectiveObjectSize;
                    continue;
                }

                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    previewPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(previewPosition, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);

                GameObject preview = Instantiate(_currentActiveGhostPrefab, previewPosition, objectRotation);
                currentObjectPreviews.Add(preview);

                SetPreviewMaterial(preview, isValid ? validPlacementMaterial : invalidPlacementMaterial);

                nextObjectDistanceTarget += effectiveObjectSize;
            }
            currentPathDistance += segmentLength;
        }

        // If LineLoopFill, also show a preview of the fill. This is a simplified approach,
        // a true polygon fill preview is more complex and might involve triangulation.
        if (_currentActiveMode == ActivePlacementMode.LineLoopFill && linePoints.Count >= 3)
        {
            // For a basic visual cue, we can try to place one ghost object at the center of the loop.
            // A more robust solution would involve triangulating the polygon and filling it with ghosts.
            Vector3 centroid = Vector3.zero;
            foreach(Vector3 p in linePoints)
            {
                centroid += p;
            }
            centroid /= linePoints.Count;

            RaycastHit hit;
            if (RaycastToTerrain(centroid + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
            {
                centroid.y = hit.point.y;
            }

            // This single central ghost is just a very basic visual indicator for fill.
            if (_currentActiveGhostPrefab != null)
            {
                GameObject fillPreview = Instantiate(_currentActiveGhostPrefab, centroid, Quaternion.identity);
                currentObjectPreviews.Add(fillPreview);
                SetPreviewMaterial(fillPreview, validPlacementMaterial); // Assume valid for preview
            }
        }
    }

    private void ClearObjectPreviews()
    {
        foreach (GameObject preview in currentObjectPreviews)
        {
            if (preview != null)
            {
                Destroy(preview);
            }
        }
        currentObjectPreviews.Clear();
    }

    private bool RaycastToTerrain(Vector3 origin, out RaycastHit hit, LayerMask layerMask)
    {
        return Physics.Raycast(origin, Vector3.down, out hit, Mathf.Infinity, layerMask);
    }

    private void SetPreviewMaterial(GameObject obj, Material material)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.material = material;
        }
    }

    // Check if a placement position is valid based on overlap and grid rules
    private bool IsValidPlacement(Vector3 position, Vector3 boundsExtents, bool canOverlap, bool occupyGrid)
    {
        // Assuming 'position' is the desired placement point (e.g., bottom-center).
        // Adjust boundsCenter to be the actual center of the object's volume based on its extents.
        Vector3 boundsCenter = position + new Vector3(0, boundsExtents.y, 0);

        if (!canOverlap)
        {
            // Check for overlaps with existing colliders
            Collider[] colliders = Physics.OverlapBox(boundsCenter, boundsExtents, Quaternion.identity, overlapCheckLayerMask);
            // If any colliders are found, it's invalid unless it's just the terrain itself
            if (colliders.Any(c => !c.gameObject.Equals(gameObject) && ((1 << c.gameObject.layer) & placementLayerMask) == 0))
            {
                return false;
            }
        }

        if (occupyGrid)
        {
            // Check if any grid cells occupied by this object are already taken
            // This is a simplified approach, could be improved for large objects
            Vector2Int gridPos = WorldToGrid(position);
            if (_occupiedGridCells.Contains(gridPos))
            {
                return false;
            }
            // For objects larger than a single grid cell, you'd need to iterate over all
            // grid cells covered by the object's bounds.
            // int startX = Mathf.FloorToInt((boundsCenter.x - boundsExtents.x) / gridSize);
            // int endX = Mathf.FloorToInt((boundsCenter.x + boundsExtents.x) / gridSize);
            // int startZ = Mathf.FloorToInt((boundsCenter.z - boundsExtents.z) / gridSize);
            // int endZ = Mathf.FloorToInt((boundsCenter.z + boundsExtents.z) / gridSize);
            // for (int x = startX; x <= endX; x++) {
            //     for (int z = startZ; z <= endZ; z++) {
            //         if (_occupiedGridCells.Contains(new Vector2Int(x, z))) return false;
            //     }
            // }
        }

        return true;
    }

    // Helper to convert world position to grid coordinates (flat 2D grid)
    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / gridSize),
            Mathf.FloorToInt(worldPos.z / gridSize)
        );
    }

    // Helper to mark grid cells as occupied (simplified for single cell)
    private void MarkGridOccupied(Vector3 worldPos, bool occupied)
    {
        if (_currentSelectedEntry == null || !_currentSelectedEntry.occupyGrid) return;

        Vector2Int gridPos = WorldToGrid(worldPos);
        if (occupied)
        {
            _occupiedGridCells.Add(gridPos);
        }
        else
        {
            _occupiedGridCells.Remove(gridPos);
        }
        // More complex logic needed here for multi-cell objects
    }


    private void PlaceAllObjectsFromLine()
    {
        if (_currentActivePlacementPrefab == null || _currentActiveMode != ActivePlacementMode.Line || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No line object prefab selected or not in line placement mode.");
            ExitPlacementMode();
            return;
        }

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        foreach (GameObject preview in currentObjectPreviews)
        {
            bool isValid = IsValidPlacement(preview.transform.position, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
            if (isValid)
            {
                Instantiate(_currentActivePlacementPrefab, preview.transform.position, preview.transform.rotation);
                MarkGridOccupied(preview.transform.position, true); // Mark grid cell occupied
                placedCount++;
            }
            else
            {
                Debug.LogWarning($"Skipped placing an object at {preview.transform.position} due to invalid placement rules.");
            }
        }
        Debug.Log($"Line placement finished! Placed {placedCount} {_currentSelectedEntry.buildingInfo.buildingName}(s).");

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle; // Reset to idle for next drawing
            linePoints.Clear();
            DisableVisualAids();
            Debug.Log("Continuous line placement. Ready for next line.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // Place objects for Line Loop mode
    private void PlaceAllObjectsFromLineLoop()
    {
        if (_currentActivePlacementPrefab == null || _currentActiveMode != ActivePlacementMode.LineLoop || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No line loop object prefab selected or not in line loop placement mode.");
            ExitPlacementMode();
            return;
        }

        if (linePoints.Count < 3)
        {
            Debug.LogWarning("Not enough points to form a meaningful loop. Need at least 3 points.");
            ExitPlacementMode();
            return;
        }

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        // Generate the final list of points including the closing segment
        List<Vector3> pointsForFinalPlacement = new List<Vector3>(linePoints);
        pointsForFinalPlacement.Add(linePoints[0]); // Add the first point to close the loop

        List<Vector3> curvedPath = GetCurvedPoints(pointsForFinalPlacement, curveResolution);

        if (curvedPath.Count < 2)
        {
            Debug.LogWarning("Failed to generate a curved path for line loop placement.");
            ExitPlacementMode();
            return;
        }

        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + objectSpacing;

        float currentPathDistance = 0f;
        float nextObjectDistanceTarget = effectiveObjectSize / 2f;

        for (int i = 0; i < curvedPath.Count - 1; i++)
        {
            Vector3 currentPoint = curvedPath[i];
            Vector3 nextPoint = curvedPath[i + 1];
            float segmentLength = Vector3.Distance(currentPoint, nextPoint);

            while (currentPathDistance + segmentLength >= nextObjectDistanceTarget)
            {
                float distanceIntoSegment = nextObjectDistanceTarget - currentPathDistance;
                Vector3 placementPosition = Vector3.Lerp(currentPoint, nextPoint, distanceIntoSegment / segmentLength);

                Vector3 direction = (nextPoint - currentPoint).normalized;
                if (direction == Vector3.zero && i > 0)
                {
                    direction = (currentPoint - curvedPath[i - 1]).normalized;
                }
                if (direction == Vector3.zero) direction = Vector3.forward;

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition = hit.point;
                }
                else
                {
                    nextObjectDistanceTarget += effectiveObjectSize;
                    continue;
                }

                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    placementPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
                if (isValid)
                {
                    Instantiate(_currentActivePlacementPrefab, placementPosition, objectRotation);
                    MarkGridOccupied(placementPosition, true);
                    placedCount++;
                }
                else
                {
                    Debug.LogWarning($"Skipped placing Line Loop object at {placementPosition} due to invalid placement rules.");
                }
                nextObjectDistanceTarget += effectiveObjectSize;
            }
            currentPathDistance += segmentLength;
        }

        Debug.Log($"Line Loop placement finished! Placed {placedCount} {_currentSelectedEntry.buildingInfo.buildingName}(s).");

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle; // Reset to idle for next drawing
            linePoints.Clear();
            DisableVisualAids();
            Debug.Log("Continuous line loop placement. Ready for next loop.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // Place objects for Line Loop Fill mode
    private void PlaceAllObjectsFromLineLoopFill()
    {
        if (_currentActivePlacementPrefab == null || _currentActiveMode != ActivePlacementMode.LineLoopFill || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No line loop fill object prefab selected or not in line loop fill placement mode.");
            ExitPlacementMode();
            return;
        }

        if (linePoints.Count < 3)
        {
            Debug.LogWarning("Not enough points to form a meaningful loop for filling. Need at least 3 points.");
            ExitPlacementMode();
            return;
        }

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        // First, place objects along the perimeter (line loop behavior)
        List<Vector3> pointsForPerimeter = new List<Vector3>(linePoints);
        pointsForPerimeter.Add(linePoints[0]); // Close the loop for perimeter placement
        List<Vector3> curvedPerimeterPath = GetCurvedPoints(pointsForPerimeter, curveResolution);

        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + objectSpacing;

        float currentPathDistance = 0f;
        float nextObjectDistanceTarget = effectiveObjectSize / 2f;

        for (int i = 0; i < curvedPerimeterPath.Count - 1; i++)
        {
            Vector3 currentPoint = curvedPerimeterPath[i];
            Vector3 nextPoint = curvedPerimeterPath[i + 1];
            float segmentLength = Vector3.Distance(currentPoint, nextPoint);

            while (currentPathDistance + segmentLength >= nextObjectDistanceTarget)
            {
                float distanceIntoSegment = nextObjectDistanceTarget - currentPathDistance;
                Vector3 placementPosition = Vector3.Lerp(currentPoint, nextPoint, distanceIntoSegment / segmentLength);

                Vector3 direction = (nextPoint - currentPoint).normalized;
                if (direction == Vector3.zero && i > 0)
                {
                    direction = (currentPoint - curvedPerimeterPath[i - 1]).normalized;
                }
                if (direction == Vector3.zero) direction = Vector3.forward;

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition = hit.point;
                }
                else
                {
                    nextObjectDistanceTarget += effectiveObjectSize;
                    continue;
                }

                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    placementPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
                if (isValid)
                {
                    Instantiate(_currentActivePlacementPrefab, placementPosition, objectRotation);
                    MarkGridOccupied(placementPosition, true);
                    placedCount++;
                }
                else
                {
                    Debug.LogWarning($"Skipped placing Line Loop Fill (perimeter) object at {placementPosition} due to invalid placement rules.");
                }
                nextObjectDistanceTarget += effectiveObjectSize;
            }
            currentPathDistance += segmentLength;
        }

        // Second, fill the interior of the polygon
        // This is a simplified approach using a bounding box and checking points.
        // For complex concave polygons, a more robust point-in-polygon algorithm
        // or a triangulation-based filling would be necessary.
        Vector3 minX = linePoints.OrderBy(p => p.x).First();
        Vector3 maxX = linePoints.OrderByDescending(p => p.x).First();
        Vector3 minZ = linePoints.OrderBy(p => p.z).First();
        Vector3 maxZ = linePoints.OrderByDescending(p => p.z).First();

        Vector3 minBounds = new Vector3(minX.x, 0, minZ.z);
        Vector3 maxBounds = new Vector3(maxX.x, 0, maxZ.z);

        float fillObjectSizeZ = objectLength + fillObjectSpacing;
        float fillObjectSizeX = objectWidth + fillObjectSpacing;

        // Simplified: Iterate through a grid within the bounding box
        for (float x = minBounds.x; x < maxBounds.x; x += fillObjectSizeX)
        {
            for (float z = minBounds.z; z < maxBounds.z; z += fillObjectSizeZ)
            {
                Vector3 potentialPlacement = new Vector3(x + fillObjectSizeX / 2f, 0, z + fillObjectSizeZ / 2f);

                // Simple point-in-polygon check for convex shapes.
                // For a more general solution, a ray-casting algorithm or ear-clipping triangulation is needed.
                // This `IsPointInPolygon` is a placeholder. For actual use, implement a robust one.
                if (IsPointInConvexPolygon(potentialPlacement, linePoints))
                {
                    RaycastHit hit;
                    if (RaycastToTerrain(potentialPlacement + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                    {
                        potentialPlacement.y = hit.point.y;
                    }
                    else
                    {
                        continue;
                    }

                    bool isValid = IsValidPlacement(potentialPlacement, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
                    if (isValid)
                    {
                        Instantiate(_currentActivePlacementPrefab, potentialPlacement, Quaternion.identity); // No specific rotation for fill
                        MarkGridOccupied(potentialPlacement, true);
                        placedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Skipped placing Line Loop Fill (interior) object at {potentialPlacement} due to invalid placement rules.");
                    }
                }
            }
        }


        Debug.Log($"Line Loop Fill placement finished! Placed {placedCount} {_currentSelectedEntry.buildingInfo.buildingName}(s).");

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle; // Reset to idle for next drawing
            linePoints.Clear();
            DisableVisualAids();
            Debug.Log("Continuous line loop fill placement. Ready for next loop.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // Placeholder for a simple (and likely insufficient for complex polygons) point-in-polygon check
    // This typically works for convex polygons or very simple concave ones.
    // A robust solution would involve ray casting or winding number algorithms.
    private bool IsPointInConvexPolygon(Vector3 point, List<Vector3> polygonPoints)
    {
        if (polygonPoints.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = polygonPoints.Count - 1; i < polygonPoints.Count; j = i++)
        {
            Vector3 pI = polygonPoints[i];
            Vector3 pJ = polygonPoints[j];

            // Ignore Y for 2D polygon check
            if (((pI.z <= point.z && point.z < pJ.z) || (pJ.z <= point.z && point.z < pI.z)) &&
                (point.x < (pJ.x - pI.x) * (point.z - pI.z) / (pJ.z - pI.z) + pI.x))
            {
                inside = !inside;
            }
        }
        return inside;
    }


    // Place objects in a circle
    private void PlaceObjectsInCircle()
    {
        if (_currentActivePlacementPrefab == null || _currentActiveMode != ActivePlacementMode.LineCircle || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No circle object prefab selected or not in circle placement mode.");
            ExitPlacementMode();
            return;
        }

        if (_circleRadius <= 0)
        {
            Debug.LogWarning("Circle radius is zero or negative. Cannot place objects.");
            ExitPlacementMode();
            return;
        }

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + objectSpacing;

        float circumference = 2 * Mathf.PI * _circleRadius;
        int numberOfObjects = Mathf.FloorToInt(circumference / effectiveObjectSize);

        if (numberOfObjects == 0)
        {
            Debug.LogWarning("No objects can fit on the circle circumference with current spacing.");
            ExitPlacementMode();
            return;
        }

        float angleStep = 360f / numberOfObjects;

        for (int i = 0; i < numberOfObjects; i++)
        {
            float angle = i * angleStep;
            Vector3 placementPosition = _circleCenter + new Vector3(Mathf.Cos(Mathf.Deg2Rad * angle), 0, Mathf.Sin(Mathf.Deg2Rad * angle)) * _circleRadius;

            RaycastHit hit;
            if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
            {
                placementPosition.y = hit.point.y;
            }
            else
            {
                continue;
            }

            Vector3 direction = (placementPosition - _circleCenter).normalized; // Direction from center to object
            Quaternion objectRotation = Quaternion.LookRotation(direction);

            if (objectsRotatedRightOfLine)
            {
                objectRotation *= Quaternion.Euler(0, 90, 0); // Rotate 90 degrees relative to the radial direction
                Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                placementPosition += perpendicularOffset;
            }

            bool isValid = IsValidPlacement(placementPosition, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
            if (isValid)
            {
                Instantiate(_currentActivePlacementPrefab, placementPosition, objectRotation);
                MarkGridOccupied(placementPosition, true);
                placedCount++;
            }
            else
            {
                Debug.LogWarning($"Skipped placing Circle object at {placementPosition} due to invalid placement rules.");
            }
        }

        Debug.Log($"Circle placement finished! Placed {placedCount} {_currentSelectedEntry.buildingInfo.buildingName}(s).");

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle; // Reset to idle for next drawing
            _isDrawingCircle = false;
            _circleRadius = 0;
            DisableVisualAids();
            Debug.Log("Continuous circle placement. Ready for next circle.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // NEW: Place objects in a circle with fill
    private void PlaceAllObjectsFromLineCircleFill()
    {
        if (_currentActivePlacementPrefab == null || _currentActiveMode != ActivePlacementMode.LineCircleFill || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No circle fill object prefab selected or not in circle fill placement mode.");
            ExitPlacementMode();
            return;
        }

        if (_circleRadius <= 0)
        {
            Debug.LogWarning("Circle radius is zero or negative. Cannot place objects for fill.");
            ExitPlacementMode();
            return;
        }

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;

        // First, place objects along the perimeter (similar to PlaceObjectsInCircle)
        float effectiveObjectSizePerimeter = objectLength + objectSpacing;
        float circumference = 2 * Mathf.PI * _circleRadius;
        int numberOfObjectsPerimeter = Mathf.FloorToInt(circumference / effectiveObjectSizePerimeter);

        if (numberOfObjectsPerimeter > 0)
        {
            float angleStep = 360f / numberOfObjectsPerimeter;
            for (int i = 0; i < numberOfObjectsPerimeter; i++)
            {
                float angle = i * angleStep;
                Vector3 placementPosition = _circleCenter + new Vector3(Mathf.Cos(Mathf.Deg2Rad * angle), 0, Mathf.Sin(Mathf.Deg2Rad * angle)) * _circleRadius;

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition.y = hit.point.y;
                }
                else
                {
                    continue;
                }

                Vector3 direction = (placementPosition - _circleCenter).normalized;
                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    placementPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
                if (isValid)
                {
                    Instantiate(_currentActivePlacementPrefab, placementPosition, objectRotation);
                    MarkGridOccupied(placementPosition, true);
                    placedCount++;
                }
                else
                {
                    Debug.LogWarning($"Skipped placing Line Circle Fill (perimeter) object at {placementPosition} due to invalid placement rules.");
                }
            }
        }

        // Second, fill the interior of the circle
        float effectiveObjectSizeFillZ = objectLength + fillObjectSpacing;
        float effectiveObjectSizeFillX = objectWidth + fillObjectSpacing;

        for (float x = _circleCenter.x - _circleRadius; x <= _circleCenter.x + _circleRadius; x += effectiveObjectSizeFillX)
        {
            for (float z = _circleCenter.z - _circleRadius; z <= _circleCenter.z + _circleRadius; z += effectiveObjectSizeFillZ)
            {
                Vector3 potentialPlacement = new Vector3(x + effectiveObjectSizeFillX / 2f, _circleCenter.y, z + effectiveObjectSizeFillZ / 2f);

                // Check if the potential placement is within the circle's radius
                if (Vector3.Distance(_circleCenter, potentialPlacement) <= _circleRadius - (effectiveObjectSizeFillX / 2f)) // Subtract half object size for better fit
                {
                    RaycastHit hit;
                    if (RaycastToTerrain(potentialPlacement + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                    {
                        potentialPlacement.y = hit.point.y;
                    }
                    else
                    {
                        continue;
                    }

                    Quaternion objectRotation = _currentActivePlacementPrefab.transform.rotation; // Keep default rotation for fill
                    // If objectsRotatedRightOfLine, maybe orient fill objects to radial direction?
                    // For now, keep it simple for fill:
                    // if (objectsRotatedRightOfLine)
                    // {
                    //     Vector3 direction = (potentialPlacement - _circleCenter).normalized;
                    //     if (direction == Vector3.zero) direction = Vector3.forward;
                    //     objectRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 90, 0);
                    // }

                    bool isValid = IsValidPlacement(potentialPlacement, boundsExtents, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
                    if (isValid)
                    {
                        Instantiate(_currentActivePlacementPrefab, potentialPlacement, objectRotation);
                        MarkGridOccupied(potentialPlacement, true);
                        placedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Skipped placing Line Circle Fill (interior) object at {potentialPlacement} due to invalid placement rules.");
                    }
                }
            }
        }

        Debug.Log($"Line Circle Fill placement finished! Placed {placedCount} {_currentSelectedEntry.buildingInfo.buildingName}(s).");

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle;
            _isDrawingCircle = false;
            _circleRadius = 0;
            DisableVisualAids();
            Debug.Log("Continuous line circle fill placement. Ready for next circle.");
        }
        else
        {
            ExitPlacementMode();
        }
    }


    // Refactored from PlaceObjectsInBox to handle original combined Box mode
    private void PlaceAllObjectsFromCombinedBox(Vector3 minCorner, Vector3 maxCorner)
    {
        if (_currentActivePlacementPrefab == null || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No object prefab selected for combined box placement.");
            ExitPlacementMode();
            return;
        }

        ClearObjectPreviews(); // Clear any existing previews

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        placedCount += FinalizeBoxLinePlacement(minCorner, maxCorner, _currentActivePlacementPrefab, boundsExtents, objectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);
        placedCount += FinalizeBoxFillPlacement(minCorner, maxCorner, _currentActivePlacementPrefab, boundsExtents, fillObjectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);

        if (placedCount > 0)
        {
            Debug.Log($"Combined Box placement finished! Placed {placedCount} objects of type {_currentSelectedEntry.buildingInfo.buildingName}.");
        }
        else
        {
            Debug.LogWarning("Combined Box placement finished, but no objects were placed. Check object prefabs and placement settings.");
        }

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle; // Reset to idle for next drawing
            _isDraggingBox = false;
            DisableVisualAids();
            Debug.Log("Continuous combined box placement. Ready for next box.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // Place objects only for Box Line mode
    private void PlaceAllObjectsFromBoxLine(Vector3 minCorner, Vector3 maxCorner)
    {
        if (_currentActivePlacementPrefab == null || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No object prefab selected for box line placement.");
            ExitPlacementMode();
            return;
        }

        ClearObjectPreviews();

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        placedCount += FinalizeBoxLinePlacement(minCorner, maxCorner, _currentActivePlacementPrefab, boundsExtents, objectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);

        if (placedCount > 0)
        {
            Debug.Log($"Box Line placement finished! Placed {placedCount} objects of type {_currentSelectedEntry.buildingInfo.buildingName}.");
        }
        else
        {
            Debug.LogWarning("Box Line placement finished, but no objects were placed. Check object prefabs and placement settings.");
        }

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle;
            _isDraggingBox = false;
            DisableVisualAids();
            Debug.Log("Continuous box line placement. Ready for next box.");
        }
        else
        {
            ExitPlacementMode();
        }
    }

    // Place objects only for Box Fill mode
    private void PlaceAllObjectsFromBoxFill(Vector3 minCorner, Vector3 maxCorner)
    {
        if (_currentActivePlacementPrefab == null || _currentSelectedEntry == null)
        {
            Debug.LogWarning("No object prefab selected for box fill placement.");
            ExitPlacementMode();
            return;
        }

        ClearObjectPreviews();

        int placedCount = 0;
        Vector3 boundsExtents = _currentSelectedEntry.buildingInfo.buildingBoundsExtents;

        placedCount += FinalizeBoxFillPlacement(minCorner, maxCorner, _currentActivePlacementPrefab, boundsExtents, fillObjectSpacing, _currentSelectedEntry.canOverlap, _currentSelectedEntry.occupyGrid);

        if (placedCount > 0)
        {
            Debug.Log($"Box Fill placement finished! Placed {placedCount} objects of type {_currentSelectedEntry.buildingInfo.buildingName}.");
        }
        else
        {
            Debug.LogWarning("Box Fill placement finished, but no objects were placed. Check object prefabs and placement settings.");
        }

        if (_currentSelectedEntry.isContinuous)
        {
            currentState = LinePlacementState.Idle;
            _isDraggingBox = false;
            DisableVisualAids();
            Debug.Log("Continuous box fill placement. Ready for next box.");
        }
        else
        {
            ExitPlacementMode();
        }
    }


    // Helper to finalize placement along a box's perimeter
    private int FinalizeBoxLinePlacement(Vector3 minCorner, Vector3 maxCorner, GameObject prefab, Vector3 boundsExtents, float spacing, bool canOverlap, bool occupyGrid)
    {
        if (prefab == null) return 0;

        int count = 0;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSize = objectLength + spacing;

        List<Vector3> boxCorners = new List<Vector3>()
        {
            new Vector3(minCorner.x, 0, minCorner.z),
            new Vector3(maxCorner.x, 0, minCorner.z),
            new Vector3(maxCorner.x, 0, maxCorner.z),
            new Vector3(minCorner.x, 0, maxCorner.z),
            new Vector3(minCorner.x, 0, minCorner.z)
        };

        float currentPathDistance = 0f;
        float nextObjectDistanceTarget = effectiveObjectSize / 2f;

        for (int i = 0; i < boxCorners.Count - 1; i++)
        {
            Vector3 currentSegmentStart = boxCorners[i];
            Vector3 currentSegmentEnd = boxCorners[i + 1];
            float segmentLength = Vector3.Distance(currentSegmentStart, currentSegmentEnd);

            if (segmentLength < 0.001f) continue;

            RaycastHit hitStart, hitEnd;
            if (RaycastToTerrain(currentSegmentStart + Vector3.up * terrainRaycastStartHeight, out hitStart, placementLayerMask))
                currentSegmentStart.y = hitStart.point.y;
            if (RaycastToTerrain(currentSegmentEnd + Vector3.up * terrainRaycastStartHeight, out hitEnd, placementLayerMask))
                currentSegmentEnd.y = hitEnd.point.y;

            while (currentPathDistance + segmentLength >= nextObjectDistanceTarget)
            {
                float distanceIntoSegment = nextObjectDistanceTarget - currentPathDistance;
                Vector3 placementPosition = Vector3.Lerp(currentSegmentStart, currentSegmentEnd, distanceIntoSegment / segmentLength);

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition = hit.point;
                }
                else
                {
                    nextObjectDistanceTarget += effectiveObjectSize;
                    continue;
                }

                Vector3 direction = (currentSegmentEnd - currentSegmentStart).normalized;
                if (direction == Vector3.zero) direction = Vector3.forward;
                Quaternion objectRotation = Quaternion.LookRotation(direction);

                if (objectsRotatedRightOfLine)
                {
                    objectRotation *= Quaternion.Euler(0, 90, 0);
                    Vector3 perpendicularOffset = objectRotation * Vector3.forward * (objectWidth / 2f);
                    placementPosition += perpendicularOffset;
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, canOverlap, occupyGrid);
                if (isValid)
                {
                    Instantiate(prefab, placementPosition, objectRotation);
                    MarkGridOccupied(placementPosition, true);
                    count++;
                }
                else
                {
                    Debug.LogWarning($"Skipped placing Box Line object at {placementPosition} due to invalid placement rules.");
                }
                nextObjectDistanceTarget += effectiveObjectSize;
            }
            currentPathDistance += segmentLength;
        }
        return count;
    }

    // Helper to finalize placement for filling a box area
    private int FinalizeBoxFillPlacement(Vector3 minCorner, Vector3 maxCorner, GameObject prefab, Vector3 boundsExtents, float spacing, bool canOverlap, bool occupyGrid)
    {
        if (prefab == null) return 0;

        int count = 0;
        float objectLength = boundsExtents.z * 2;
        float objectWidth = boundsExtents.x * 2;
        float effectiveObjectSizeZ = objectLength + spacing;
        float effectiveObjectSizeX = objectWidth + spacing;

        for (float x = minCorner.x + effectiveObjectSizeX / 2f; x < maxCorner.x; x += effectiveObjectSizeX)
        {
            for (float z = minCorner.z + effectiveObjectSizeZ / 2f; z < maxCorner.z; z += effectiveObjectSizeZ)
            {
                Vector3 placementPosition = new Vector3(x, minCorner.y, z);

                RaycastHit hit;
                if (RaycastToTerrain(placementPosition + Vector3.up * terrainRaycastStartHeight, out hit, placementLayerMask))
                {
                    placementPosition = hit.point;
                }
                else
                {
                    continue;
                }

                Quaternion objectRotation = prefab.transform.rotation;
                if (objectsRotatedRightOfLine)
                {
                    objectRotation = Quaternion.Euler(0, 90, 0);
                }

                bool isValid = IsValidPlacement(placementPosition, boundsExtents, canOverlap, occupyGrid);
                if (isValid)
                {
                    Instantiate(prefab, placementPosition, objectRotation);
                    MarkGridOccupied(placementPosition, true);
                    count++;
                }
                else
                {
                    Debug.LogWarning($"Skipped placing Box Fill object at {placementPosition} due to invalid placement rules.");
                }
            }
        }
        return count;
    }

    // Helper to enable/disable all placement buttons
    private void SetAllPlacementButtonsInteractable(bool interactable)
    {
        foreach (var entry in placeableObjectEntries)
        {
            if (entry.lineButton != null) entry.lineButton.interactable = interactable; // RENAMED
            if (entry.boxAndFillButton != null) entry.boxAndFillButton.interactable = interactable; // RENAMED
            if (entry.lineLoopButton != null) entry.lineLoopButton.interactable = interactable; // RENAMED
            if (entry.boxLineButton != null) entry.boxLineButton.interactable = interactable; // RENAMED
            if (entry.boxFillButton != null) entry.boxFillButton.interactable = interactable; // RENAMED
            if (entry.lineLoopFillButton != null) entry.lineLoopFillButton.interactable = interactable; // RENAMED
            if (entry.lineCircleButton != null) entry.lineCircleButton.interactable = interactable; // RENAMED
            if (entry.lineCircleFillButton != null) entry.lineCircleFillButton.interactable = interactable; // NEW
        }
    }
}
