using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO; // Required for file operations

public class VisibilityTriggeredGlitchRecorder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera used for visibility checks.")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("The root object whose visibility triggers the glitch.")]
    [SerializeField] private GameObject targetObject;
    [Tooltip("Reference to the HierarchicalContinuousGlitch script instance.")]
    [SerializeField] private HierarchicalContinuousGlitch glitchControllerScript;

    [Header("Visibility Settings")]
    [Tooltip("How often (in seconds) to perform the visibility check.")]
    [SerializeField] private float visibilityCheckFrequency = 0.25f;
    [Tooltip("Requires the object's bounding box center to be unoccluded and within the viewport.")]
    [SerializeField] private bool checkCenterOcclusion = true;
    [Tooltip("Requires the object's bounding box corners to be within the camera viewport.")]
    [SerializeField] private bool checkBoundsInViewport = true;
    [Tooltip("Layer mask for occlusion checks. Should include geometry that can block the view.")]
    [SerializeField] private LayerMask occlusionLayers = ~0; // Default: Everything (~0 is bitwise NOT of 0, resulting in all layers enabled)

    [Header("Glitch Timing")]
    [Tooltip("Delay (in seconds) after the object becomes visible before the glitch effect starts.")]
    [SerializeField] private float glitchStartDelay = 4.0f; // Delay Parameter
    [Tooltip("Minimum duration (in seconds) the glitch effect stays active once started.")]
    [SerializeField] private float minGlitchDuration = 1.0f;
    [Tooltip("Maximum duration (in seconds) the glitch effect stays active once started.")]
    [SerializeField] private float maxGlitchDuration = 5.0f;
    [Tooltip("Minimum time (in seconds) after a glitch finishes before another can be triggered.")]
    [SerializeField] private float glitchCooldownDuration = 2.0f;

    [Header("Recording Settings")]
    [Tooltip("Filename for the JSON output (saved in persistentDataPath).")]
    [SerializeField] private string jsonFileName = "glitch_timestamps.json";

    // --- Private State ---
    private enum State { Idle, WaitingToGlitch, Glitching, Cooldown }
    private State currentState = State.Idle;

    private float timeSinceLastCheck = 0f;
    private float sessionStartTime = 0f;
    private Bounds targetBounds;
    private Renderer targetPrimaryRenderer; // Used for bounds checking

    private List<GlitchRecord> glitchRecords = new List<GlitchRecord>();
    private Coroutine activeVisibilityCoroutine = null;
    private Coroutine activeGlitchCoroutine = null;
    private Coroutine activeCooldownCoroutine = null;

    private bool wasTargetVisibleLastCheck = false; // Track visibility transitions

    // --- Data Structures for JSON ---
    [System.Serializable]
    private class GlitchRecord
    {
        public float startVisibleTime;  // Time object became visible
        public float startGlitchedTime; // Time glitch effect actually started (after delay)
        public float endGlitchedTime;   // Time glitch effect ended
        // Optional calculated properties for convenience
        public float visibleToGlitchDelay => (startGlitchedTime > 0 && startVisibleTime >= 0) ? (startGlitchedTime - startVisibleTime) : 0f;
        public float glitchDuration => (endGlitchedTime > 0 && startGlitchedTime > 0) ? (endGlitchedTime - startGlitchedTime) : 0f;
    }

    [System.Serializable]
    private class GlitchDataWrapper // Needed for JsonUtility list serialization
    {
        public List<GlitchRecord> glitchEvents;
    }

    // --- Unity Methods ---

    void Start()
    {
        // --- Validation ---
        if (mainCamera == null)
        {
            mainCamera = Camera.main; // Attempt to find default main camera
            if (mainCamera == null) Debug.LogError("VisibilityTriggeredGlitchRecorder: Main Camera not found or assigned!", this);
        }
        if (targetObject == null) Debug.LogError("VisibilityTriggeredGlitchRecorder: Target Object not assigned!", this);
        if (glitchControllerScript == null) Debug.LogError("VisibilityTriggeredGlitchRecorder: Glitch Controller Script not assigned!", this);

        // Disable script if essential references are missing
        if (mainCamera == null || targetObject == null || glitchControllerScript == null)
        {
            Debug.LogError("VisibilityTriggeredGlitchRecorder: Missing required references. Disabling script.", this);
            enabled = false;
            return;
        }

        // --- Initialization ---
        // Find a renderer within the target hierarchy for bounds checking
        targetPrimaryRenderer = targetObject.GetComponentInChildren<Renderer>();
        if (targetPrimaryRenderer == null)
        {
             Debug.LogError($"VisibilityTriggeredGlitchRecorder: No Renderer found in the hierarchy of Target Object '{targetObject.name}'. Cannot check visibility. Disabling script.", this);
             enabled = false;
             return;
        }

        // Initial setup
        glitchControllerScript.enabled = false; // Ensure glitch effect is off
        currentState = State.Idle;
        wasTargetVisibleLastCheck = false; // Assume target is not visible at the start

        sessionStartTime = Time.time; // Record the start time of the recording session
        Debug.Log($"VisibilityTriggeredGlitchRecorder Initialized. Session started at {sessionStartTime:F2}s. Recording to {GetSavePath()}");
    }

    void Update()
    {
        // --- Visibility Check Logic (throttled) ---
        timeSinceLastCheck += Time.deltaTime;
        if (timeSinceLastCheck >= visibilityCheckFrequency)
        {
            timeSinceLastCheck = 0f; // Reset timer

            bool isCurrentlyVisible = IsTargetVisible(); // Perform the visibility check

            // --- Triggering Logic ---
            // Only trigger the delay sequence if:
            // 1. Current state is Idle.
            // 2. Object is visible now.
            // 3. Object was NOT visible last check (detecting the rising edge).
            if (currentState == State.Idle && isCurrentlyVisible && !wasTargetVisibleLastCheck)
            {
                Debug.Log($"[{Time.time - sessionStartTime:F2}s] Target TRANSITIONED to visible! Starting delay sequence ({glitchStartDelay:F2}s).");
                // Start the delay coroutine if one isn't already running
                if (activeVisibilityCoroutine == null)
                {
                     activeVisibilityCoroutine = StartCoroutine(VisibilityDetectedSequence());
                }
                else
                {
                    // This case should ideally not happen if state logic is correct, but acts as a safeguard.
                    Debug.LogWarning("Trying to start VisibilityDetectedSequence while another coroutine is active or state is wrong. Resetting state.");
                     StopAllCoroutines(); // Force stop everything
                     ResetStateToIdle(); // Reset to a clean state
                }
            }

            // --- Update history for the next frame ---
            // This MUST happen *after* the trigger check to correctly detect the transition.
            wasTargetVisibleLastCheck = isCurrentlyVisible;
        }
    }

    void OnDisable()
    {
         StopAllCoroutines(); // Ensure all sequences (delay, glitch, cooldown) are stopped
         ResetStateToIdle(); // Reset state machine and ensure glitch controller is off
         wasTargetVisibleLastCheck = false; // Reset visibility tracking
         SaveGlitchData(); // Attempt to save any recorded data
    }

    void OnApplicationQuit()
    {
        // Similar cleanup as OnDisable, ensuring data save on quit
        StopAllCoroutines();
        if(glitchControllerScript != null) // Check if reference exists before accessing
        {
            glitchControllerScript.enabled = false;
        }
        SaveGlitchData();
    }


    // --- Core Logic Methods ---

    /// <summary>
    /// Checks if the target object is considered visible by the main camera based on configured settings.
    /// </summary>
    /// <returns>True if visible, False otherwise.</returns>
    bool IsTargetVisible()
    {
        // Basic checks: Renderer must exist, be enabled, and the target GameObject must be active.
        if (!targetPrimaryRenderer || !targetPrimaryRenderer.enabled || !targetObject.activeInHierarchy)
            return false;

        // Update the bounds based on the renderer's current state
        targetBounds = targetPrimaryRenderer.bounds;

        // 1. Check if bounds corners are within viewport (optional)
        if (checkBoundsInViewport)
        {
            Vector3[] corners = GetBoundsCorners(targetBounds);
            bool allCornersInView = true;
            foreach (Vector3 corner in corners)
            {
                // Convert world position to viewport coordinates (0-1 range)
                Vector3 viewportPoint = mainCamera.WorldToViewportPoint(corner);
                // Check if X, Y are within 0-1 range, and Z is positive (in front of near clip plane)
                if (viewportPoint.x < 0 || viewportPoint.x > 1 || viewportPoint.y < 0 || viewportPoint.y > 1 || viewportPoint.z <= mainCamera.nearClipPlane)
                {
                    allCornersInView = false;
                    break; // Exit loop early if any corner is out
                }
            }
            if (!allCornersInView) return false; // Not fully visible if a corner is outside the viewport
        }

        // 2. Check for Occlusion using Raycast (optional)
        if (checkCenterOcclusion)
        {
            // Check if the center point is within the viewport first (important!)
            Vector3 centerViewportPoint = mainCamera.WorldToViewportPoint(targetBounds.center);
            if (centerViewportPoint.x < 0 || centerViewportPoint.x > 1 || centerViewportPoint.y < 0 || centerViewportPoint.y > 1 || centerViewportPoint.z <= mainCamera.nearClipPlane)
            {
                 return false; // Center is outside the viewport or behind the camera
            }

            // Perform raycast from camera towards the center of the bounds
            Vector3 direction = targetBounds.center - mainCamera.transform.position;
            float distance = direction.magnitude;

            // Raycast slightly short of the center to avoid hitting the target itself from the inside if the origin is inside
            if (Physics.Raycast(mainCamera.transform.position, direction.normalized, out RaycastHit hit, distance - 0.01f, occlusionLayers))
            {
                // Check if the object hit is NOT the target object itself or one of its children
                if (!hit.transform.IsChildOf(targetObject.transform) && hit.transform != targetObject.transform)
                {
                     // Debug.Log($"Occluded by {hit.transform.name}"); // Optional: log what occluded it
                     return false; // Occluded by something else
                }
            }
            // If no hit or the hit was the target itself, it's not occluded from the center view.
        }

        // If all enabled checks passed, the object is considered visible
        return true;
    }

    /// <summary>
    /// Coroutine started when visibility is first detected. Handles the initial delay.
    /// </summary>
    IEnumerator VisibilityDetectedSequence()
    {
        currentState = State.WaitingToGlitch; // Enter waiting state
        float detectedTime = Time.time - sessionStartTime; // Record time relative to session start
        Debug.Log($"[State: WaitingToGlitch] Visibility detected at {detectedTime:F2}s. Waiting for {glitchStartDelay:F2}s delay.");

        // Create the record structure, populating only the initial detection time
        GlitchRecord currentRecord = new GlitchRecord { startVisibleTime = detectedTime };

        // Wait for the specified delay
        yield return new WaitForSeconds(glitchStartDelay);

        // --- Re-check visibility AFTER the delay ---
        // The object might have moved out of view during the delay
        if (!IsTargetVisible())
        {
            Debug.Log($"[State: WaitingToGlitch -> Idle] Target became invisible during delay. Cancelling glitch.");
            currentState = State.Idle; // Return to idle state
            activeVisibilityCoroutine = null; // Mark this coroutine as finished
            yield break; // Exit coroutine - do not proceed to glitching
        }

        // --- If still visible, proceed to the actual glitch sequence ---
        Debug.Log($"[State: WaitingToGlitch -> Glitching] Delay finished. Target still visible. Starting glitch effect.");
        activeVisibilityCoroutine = null; // Mark this coroutine as finished

        // Start the glitch sequence, passing the record to be populated further
        if (activeGlitchCoroutine == null)
        {
             activeGlitchCoroutine = StartCoroutine(GlitchSequence(currentRecord));
        }
        else
        {
             Debug.LogWarning("GlitchSequence was already running when delay finished. Resetting state.");
             ResetStateToIdle(); // Reset state if something went wrong
        }
    }

    /// <summary>
    /// Coroutine that handles the active glitching period.
    /// </summary>
    /// <param name="recordInProgress">The GlitchRecord object to populate with timing info.</param>
    IEnumerator GlitchSequence(GlitchRecord recordInProgress)
    {
        currentState = State.Glitching; // Enter Glitching state
        float glitchDuration = Random.Range(minGlitchDuration, maxGlitchDuration); // Calculate random duration
        float glitchStartTime = Time.time - sessionStartTime; // Record actual glitch start time

        // Update the record with the actual glitch start time
        recordInProgress.startGlitchedTime = glitchStartTime;

        Debug.Log($"[State: Glitching] Enabling glitch at {glitchStartTime:F2}s for {glitchDuration:F2}s.");

        // Enable the referenced glitch effect script
        if (glitchControllerScript != null) glitchControllerScript.enabled = true;

        // Wait for the duration of the glitch
        yield return new WaitForSeconds(glitchDuration);

        // --- Disable the glitch effect ---
        if (glitchControllerScript != null) glitchControllerScript.enabled = false;
        activeGlitchCoroutine = null; // Mark this coroutine as finished

        float glitchEndTime = Time.time - sessionStartTime; // Record glitch end time
        // Update the record with the glitch end time
        recordInProgress.endGlitchedTime = glitchEndTime;

        Debug.Log($"[State: Glitching -> Cooldown] Disabling glitch at {glitchEndTime:F2}s.");

        // --- Add the fully populated record to our list ---
        glitchRecords.Add(recordInProgress);

        // --- Start the cooldown sequence ---
        if (activeCooldownCoroutine == null)
        {
            activeCooldownCoroutine = StartCoroutine(CooldownSequence());
        }
        else
        {
            Debug.LogWarning("CooldownSequence was already running when glitch finished.");
            // Optionally stop the old one and start anew if required:
            // StopCoroutine(activeCooldownCoroutine);
            // activeCooldownCoroutine = StartCoroutine(CooldownSequence());
        }
    }

    /// <summary>
    /// Coroutine that handles the cooldown period after a glitch finishes.
    /// </summary>
    IEnumerator CooldownSequence()
    {
        currentState = State.Cooldown; // Enter Cooldown state
        Debug.Log($"[State: Cooldown] Entering cooldown for {glitchCooldownDuration:F2}s.");

        // Wait for the specified cooldown duration
        yield return new WaitForSeconds(glitchCooldownDuration);

        Debug.Log("[State: Cooldown -> Idle] Cooldown finished. Returning to Idle state.");
        currentState = State.Idle; // Return to Idle, allowing visibility checks to trigger again
        activeCooldownCoroutine = null; // Mark this coroutine as finished
    }


    // --- Helper and Recording Methods ---

    /// <summary>
    /// Calculates the 8 world-space corner positions of a Bounds object.
    /// </summary>
    Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3[] corners = new Vector3[8];
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        // Calculate positions based on center and extents
        corners[0] = center + new Vector3( extents.x,  extents.y,  extents.z);
        corners[1] = center + new Vector3( extents.x,  extents.y, -extents.z);
        corners[2] = center + new Vector3( extents.x, -extents.y,  extents.z);
        corners[3] = center + new Vector3( extents.x, -extents.y, -extents.z);
        corners[4] = center + new Vector3(-extents.x,  extents.y,  extents.z);
        corners[5] = center + new Vector3(-extents.x,  extents.y, -extents.z);
        corners[6] = center + new Vector3(-extents.x, -extents.y,  extents.z);
        corners[7] = center + new Vector3(-extents.x, -extents.y, -extents.z);
        return corners;
    }

    /// <summary>
    /// Gets the full path for saving the JSON file.
    /// </summary>
    string GetSavePath()
    {
        // Uses a persistent data path suitable for saving application data across sessions
        return Path.Combine(Application.persistentDataPath, jsonFileName);
    }

    /// <summary>
    /// Saves the recorded glitch event data to a JSON file.
    /// </summary>
    void SaveGlitchData()
    {
        // Don't save if no records exist or list is null
        if (glitchRecords == null || glitchRecords.Count == 0)
        {
             Debug.Log("No glitch events recorded, skipping JSON save.");
             return;
        }

        // Sort records by the time visibility started, for easier analysis
        glitchRecords.Sort((a, b) => a.startVisibleTime.CompareTo(b.startVisibleTime));

        // Wrap the list in a helper class for JsonUtility
        GlitchDataWrapper dataWrapper = new GlitchDataWrapper { glitchEvents = this.glitchRecords };
        // Convert the data to JSON format (pretty printed for readability)
        string json = JsonUtility.ToJson(dataWrapper, true);
        string path = GetSavePath(); // Get the target file path

        try
        {
            // Write the JSON string to the file
            File.WriteAllText(path, json);
            Debug.Log($"Glitch data ({glitchRecords.Count} events) saved successfully to: {path}");
        }
        catch (System.Exception e)
        {
            // Log errors if saving fails
            Debug.LogError($"Failed to save glitch data to {path}. Error: {e.Message}");
            Debug.LogException(e); // Log the full stack trace for debugging
        }
    }

    /// <summary>
    /// Utility method to reset the state machine and related variables to Idle.
    /// Also ensures the glitch controller script is disabled.
    /// </summary>
    void ResetStateToIdle()
    {
         if(glitchControllerScript != null) // Ensure reference exists
         {
            glitchControllerScript.enabled = false; // Turn off glitch effect
         }
         currentState = State.Idle; // Set state machine to Idle
         // Clear coroutine references
         activeVisibilityCoroutine = null;
         activeGlitchCoroutine = null;
         activeCooldownCoroutine = null;
         Debug.Log("State reset to Idle.");
         // Note: We do NOT reset wasTargetVisibleLastCheck here,
         // as Update needs the correct history for the next check.
         // It gets reset naturally if the object becomes invisible or in Start/OnDisable.
    }
}