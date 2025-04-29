using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO; // Required for file operations
using System;   // Required for DateTime

public class AdvancedVisibilityTriggeredGlitchRecorder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera used for visibility checks.")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("List of potential AdvancedHierarchicalGlitch targets. One will be chosen at random.")]
    [SerializeField] private List<AdvancedHierarchicalGlitch> glitchTargets = new List<AdvancedHierarchicalGlitch>();

    [Header("Visibility Settings")]
    [Tooltip("How often (in seconds) to perform the visibility check.")]
    [SerializeField] private float visibilityCheckFrequency = 0.25f;
    [Tooltip("Requires the selected target's bounding box center to be unoccluded and within the viewport.")]
    [SerializeField] private bool checkCenterOcclusion = true;
    [Tooltip("Requires the selected target's bounding box corners to be within the camera viewport.")]
    [SerializeField] private bool checkBoundsInViewport = true;
    [Tooltip("Layer mask for occlusion checks. Should include geometry that can block the view.")]
    [SerializeField] private LayerMask occlusionLayers = ~0; // Default: Everything

    [Header("Glitch Control Timing")]
    [Tooltip("Delay (in seconds) after the target becomes visible before enabling its glitch script.")]
    [SerializeField] private float glitchStartDelay = 4.0f;
    [Tooltip("Minimum duration (in seconds) the selected glitch script stays ENABLED once activated.")]
    [SerializeField] private float minGlitchActiveDuration = 3.0f;
    [Tooltip("Maximum duration (in seconds) the selected glitch script stays ENABLED once activated.")]
    [SerializeField] private float maxGlitchActiveDuration = 8.0f;
    [Tooltip("Minimum time (in seconds) after a glitch script is disabled before another visibility detection can trigger a new cycle.")]
    [SerializeField] private float glitchCooldownDuration = 5.0f;

    [Header("Recording Settings")]
    [Tooltip("Directory relative to Application.persistentDataPath where logs will be saved.")]
    [SerializeField] private string logDirectory = "VisibilityGlitchLogs";
    [Tooltip("Base filename for the JSON output.")]
    [SerializeField] private string jsonFileNameBase = "visibility_glitch_log";

    // --- Private State ---
    private enum State { Idle, WaitingToGlitch, Glitching, Cooldown, Disabled }
    private State currentState = State.Disabled; // Start disabled until setup completes

    // Selected Target Info
    private AdvancedHierarchicalGlitch selectedTargetGlitchScript = null;
    private GameObject selectedTargetRootObject = null;
    private Renderer selectedTargetPrimaryRenderer = null; // Used for bounds checking
    private Bounds selectedTargetBounds;

    // Timing & State
    private float timeSinceLastCheck = 0f;
    private float sessionStartTime = 0f;
    private bool wasTargetVisibleLastCheck = false; // Track visibility transitions

    // Coroutine References
    private Coroutine activeVisibilityCoroutine = null;
    private Coroutine activeGlitchCoroutine = null;
    private Coroutine activeCooldownCoroutine = null;

    // Logging
    private List<GlitchRecord> glitchRecords = new List<GlitchRecord>();
    private bool logWritten = false;
    private string fullLogFilePath = "";

    // --- Data Structures for JSON ---
    [System.Serializable]
    private class GlitchRecord
    {
        public string selectedTargetName;
        public float timeVisibleDetected;  // Time target became visible (relative to session start)
        public float timeGlitchEnabled; // Time glitch script was enabled (relative to session start)
        public float timeGlitchDisabled;   // Time glitch script was disabled (relative to session start)
        public string triggeredGlitchType; // <<< ADDED: To store the type of glitch from AdvancedHierarchicalGlitch

        // Calculated properties for convenience in JSON
        public float visibleToGlitchDelay => (timeGlitchEnabled > 0 && timeVisibleDetected >= 0) ? (timeGlitchEnabled - timeVisibleDetected) : 0f;
        public float actualGlitchActiveDuration => (timeGlitchDisabled > 0 && timeGlitchEnabled > 0) ? (timeGlitchDisabled - timeGlitchEnabled) : 0f;
    }

    [System.Serializable]
    private class GlitchLogDataWrapper // Needed for JsonUtility list serialization
    {
        public string selectedTargetObjectName;
        public float configuredStartDelay;
        public float configuredMinActiveDuration;
        public float configuredMaxActiveDuration;
        public float configuredCooldown;
        public float sessionStartTimeAbsolute; // Record actual system time if needed, or Time.time
        public List<GlitchRecord> glitchEvents;
    }

    // --- Unity Methods ---

    void Start()
    {
        sessionStartTime = Time.time;
        currentState = State.Disabled; // Assume disabled until validation passes

        // --- Validation ---
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) Debug.LogError($"[{gameObject.name}] Main Camera not found or assigned!", this);
        }

        if (glitchTargets == null || glitchTargets.Count == 0)
        {
            Debug.LogError($"[{gameObject.name}] Glitch Targets list is empty!", this);
        }

        // Filter out null entries and check for valid Target Roots
        glitchTargets.RemoveAll(item => item == null);
        if (glitchTargets.Count == 0)
        {
             Debug.LogError($"[{gameObject.name}] No valid AdvancedHierarchicalGlitch scripts remaining in the Glitch Targets list after removing nulls!", this);
        }

        List<AdvancedHierarchicalGlitch> validTargets = new List<AdvancedHierarchicalGlitch>();
        foreach (var target in glitchTargets)
        {
            // No need for null check here, already removed nulls
            Transform root = target.GetTargetRoot(); // Use the new getter
            if (root != null && root.GetComponentInChildren<Renderer>() != null)
            {
                validTargets.Add(target);
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] Excluding target '{target.gameObject.name}' because its Target Root is null or has no Renderers.", target);
            }
        }

        if (validTargets.Count == 0)
        {
             Debug.LogError($"[{gameObject.name}] No targets with valid Target Roots and Renderers found in the list!", this);
        }


        // Disable script if essential references are missing or no valid targets
        if (mainCamera == null || validTargets.Count == 0)
        {
            Debug.LogError($"[{gameObject.name}] Missing required references or no valid targets. Disabling script.", this);
            enabled = false; // Keep script component active, but logic won't run
            return;
        }

        // --- Initialization ---
        // Randomly select ONE target for this session
        int randomIndex = UnityEngine.Random.Range(0, validTargets.Count);
        selectedTargetGlitchScript = validTargets[randomIndex];
        selectedTargetRootObject = selectedTargetGlitchScript.GetTargetRoot().gameObject; // Use the new getter
        selectedTargetPrimaryRenderer = selectedTargetRootObject.GetComponentInChildren<Renderer>(); // We already validated this exists

        // Prepare file path for logging
        SetupLogFile();

        Debug.Log($"[{gameObject.name}] Initialized. Session started at {sessionStartTime:F2}s. SELECTED TARGET: '{selectedTargetRootObject.name}'. Recording to {fullLogFilePath}");

        // --- Disable Non-Selected Glitch Scripts ---
        // Ensure only the randomly chosen target's glitch script MIGHT run.
        // The selected script also starts disabled and is controlled by coroutines.
        foreach (var target in glitchTargets) // Iterate original list to catch all potential scripts
        {
            if (target != null)
            {
                if (target == selectedTargetGlitchScript)
                {
                    // Ensure the *selected* script starts disabled. It will be enabled by coroutines.
                    target.enabled = false;
                }
                else
                {
                    // Disable all *other* non-selected scripts permanently for this session.
                    target.enabled = false;
                    // Debug.Log($"[{gameObject.name}] Disabling non-selected target '{target.gameObject.name}' glitch script.");
                }
            }
        }

        // --- Start the state machine ---
        currentState = State.Idle;
        wasTargetVisibleLastCheck = false;
        timeSinceLastCheck = 0f; // Start checking visibility immediately
        logWritten = false;

        // Clear any previous coroutine references (paranoid check)
        activeVisibilityCoroutine = null;
        activeGlitchCoroutine = null;
        activeCooldownCoroutine = null;
    }

    // Need to add this public getter to AdvancedHierarchicalGlitch.cs
    /*
    public Transform GetTargetRoot()
    {
        return targetRoot;
    }
    */


    void Update()
    {
        if (currentState == State.Disabled || selectedTargetGlitchScript == null) return; // Do nothing if disabled or setup failed

        // --- Visibility Check Logic (throttled) ---
        timeSinceLastCheck += Time.deltaTime;
        if (timeSinceLastCheck >= visibilityCheckFrequency)
        {
            timeSinceLastCheck = 0f; // Reset timer

            bool isCurrentlyVisible = IsTargetVisible(); // Perform the visibility check

            // --- Triggering Logic (Only when Idle) ---
            if (currentState == State.Idle && isCurrentlyVisible && !wasTargetVisibleLastCheck)
            {
                Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState})] Target '{selectedTargetRootObject.name}' TRANSITIONED to visible! Starting delay ({glitchStartDelay:F2}s).");
                if (activeVisibilityCoroutine == null)
                {
                     activeVisibilityCoroutine = StartCoroutine(VisibilityDetectedSequence());
                }
                else
                {
                     Debug.LogWarning($"[{gameObject.name}] Trying to start VisibilityDetectedSequence while already active or state is wrong. Resetting state.");
                     ResetCoroutinesAndState();
                }
            }

            // --- Update history for the next frame ---
            wasTargetVisibleLastCheck = isCurrentlyVisible;
        }
    }

    void OnDisable()
    {
         // This is called when the component is disabled OR the object is destroyed.
         if (currentState != State.Disabled) // Avoid redundant calls if disabled during Start()
         {
            Debug.Log($"[{gameObject.name}] OnDisable called. Stopping coroutines and attempting to save log.");
            StopAllCoroutines(); // Ensure all sequences are stopped
            ResetCoroutinesAndState(disableGlitchScript: true); // Reset state machine and ensure glitch controller is off
            wasTargetVisibleLastCheck = false; // Reset visibility tracking
            SaveGlitchData(); // Attempt to save any recorded data
            currentState = State.Disabled; // Mark as fully disabled now
         }
    }

    void OnApplicationQuit()
    {
        // Explicitly try saving on quit, OnDisable might not always be sufficient or timely.
        if (currentState != State.Disabled && !logWritten)
        {
             Debug.Log($"[{gameObject.name}] OnApplicationQuit called. Attempting to save log.");
             // Don't need to stop coroutines here, application is quitting anyway
             if (selectedTargetGlitchScript != null) selectedTargetGlitchScript.enabled = false; // Ensure it's off just in case
             SaveGlitchData();
        }
    }


    // --- Core Logic Methods ---

    bool IsTargetVisible()
    {
        // Basic checks: Renderer must exist, be enabled, and the target GameObject must be active.
        if (!selectedTargetPrimaryRenderer || !selectedTargetPrimaryRenderer.enabled || !selectedTargetRootObject.activeInHierarchy)
            return false;

        selectedTargetBounds = selectedTargetPrimaryRenderer.bounds; // Update bounds

        // 1. Check Bounds Corners In Viewport (optional)
        if (checkBoundsInViewport)
        {
            if (!CheckBoundsCornersInViewport(selectedTargetBounds)) return false;
        }

        // 2. Check Center Occlusion (optional)
        if (checkCenterOcclusion)
        {
            if (!CheckCenterPointOcclusion(selectedTargetBounds)) return false;
        }

        // If all enabled checks passed
        return true;
    }

    bool CheckBoundsCornersInViewport(Bounds bounds)
    {
        Vector3[] corners = GetBoundsCorners(bounds);
        foreach (Vector3 corner in corners)
        {
            Vector3 viewportPoint = mainCamera.WorldToViewportPoint(corner);
            if (viewportPoint.x < 0 || viewportPoint.x > 1 || viewportPoint.y < 0 || viewportPoint.y > 1 || viewportPoint.z <= mainCamera.nearClipPlane)
            {
                return false; // At least one corner is out of view
            }
        }
        return true; // All corners are in view
    }

     bool CheckCenterPointOcclusion(Bounds bounds)
     {
        Vector3 centerViewportPoint = mainCamera.WorldToViewportPoint(bounds.center);
        if (centerViewportPoint.x < 0 || centerViewportPoint.x > 1 || centerViewportPoint.y < 0 || centerViewportPoint.y > 1 || centerViewportPoint.z <= mainCamera.nearClipPlane)
        {
             return false; // Center is outside the viewport or behind the camera
        }

        Vector3 direction = bounds.center - mainCamera.transform.position;
        float distance = direction.magnitude;

        // Raycast slightly short of the center
        if (Physics.Raycast(mainCamera.transform.position, direction.normalized, out RaycastHit hit, distance - 0.01f, occlusionLayers))
        {
            // Check if the hit object is part of the target hierarchy
            if (!hit.transform.IsChildOf(selectedTargetRootObject.transform) && hit.transform != selectedTargetRootObject.transform)
            {
                 // Debug.Log($"Occluded by {hit.transform.name}"); // Optional
                 return false; // Occluded by something else
            }
        }
        // Not occluded (or occluded by self/child, which is fine)
        return true;
     }


    IEnumerator VisibilityDetectedSequence()
    {
        currentState = State.WaitingToGlitch;
        float detectedTimeRelative = Time.time - sessionStartTime;
        Debug.Log($"[{detectedTimeRelative:F2}s ({currentState})] Waiting for {glitchStartDelay:F2}s delay...");

        GlitchRecord currentRecord = new GlitchRecord
        {
            selectedTargetName = selectedTargetRootObject.name,
            timeVisibleDetected = detectedTimeRelative
            // timeGlitchEnabled and timeGlitchDisabled will be filled later
        };

        yield return new WaitForSeconds(glitchStartDelay);

        // Re-check visibility AFTER the delay
        if (!IsTargetVisible())
        {
            Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState} -> Idle)] Target became invisible during delay. Cancelling glitch cycle.");
            currentState = State.Idle;
            activeVisibilityCoroutine = null;
            yield break; // Exit coroutine
        }

        // --- Proceed to Glitching ---
        Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState} -> Glitching)] Delay complete, target still visible. Starting glitch active period.");
        activeVisibilityCoroutine = null;

        if (activeGlitchCoroutine == null)
        {
             activeGlitchCoroutine = StartCoroutine(GlitchActiveSequence(currentRecord));
        }
        else
        {
             Debug.LogWarning($"[{gameObject.name}] GlitchActiveSequence was somehow already running. Resetting state.");
             ResetCoroutinesAndState();
        }
    }

    IEnumerator GlitchActiveSequence(GlitchRecord recordInProgress)
    {
        currentState = State.Glitching;
        float activeDuration = UnityEngine.Random.Range(minGlitchActiveDuration, maxGlitchActiveDuration);
        float enableTimeRelative = Time.time - sessionStartTime;
        float actualEndTime = Time.time + activeDuration; // Calculate exact time when the sequence should end

        recordInProgress.timeGlitchEnabled = enableTimeRelative;
        recordInProgress.triggeredGlitchType = "None"; // Default value if no glitch occurs/is detected

        Debug.Log($"[{enableTimeRelative:F2}s ({currentState})] Enabling '{selectedTargetRootObject.name}' glitch script for ~{activeDuration:F2}s.");

        if (selectedTargetGlitchScript != null)
        {
            selectedTargetGlitchScript.enabled = true; // << ENABLE THE GLITCH SCRIPT >>

            // --- Monitor for glitch type during active duration ---
            while (Time.time < actualEndTime)
            {
                // Check the type on each frame while the script is supposed to be active
                string currentType = selectedTargetGlitchScript.CurrentMomentaryGlitchType;
                if (currentType != "None" && recordInProgress.triggeredGlitchType == "None") // Capture the *first* non-"None" type
                {
                    recordInProgress.triggeredGlitchType = currentType;
                    Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState})] Captured First Glitch Type: {recordInProgress.triggeredGlitchType}");
                    // We captured the first type, no need to keep checking for *changes* in this log,
                    // AdvancedHierarchicalGlitch logs its own detailed events anyway.
                }

                yield return null; // Wait for the next frame
            }
            // The loop condition ensures we wait for approximately the correct total duration.

            // --- Disable the glitch script ---
            // Note: disableTime is recorded *after* the loop finishes
            float disableTimeRelative = Time.time - sessionStartTime;
            Debug.Log($"[{disableTimeRelative:F2}s ({currentState} -> Cooldown)] Disabling '{selectedTargetRootObject.name}' glitch script. Final Recorded Type: {recordInProgress.triggeredGlitchType}");

            // Check again in case it was destroyed during the wait
            if (selectedTargetGlitchScript != null)
            {
                 selectedTargetGlitchScript.enabled = false; // << DISABLE THE GLITCH SCRIPT >>
            }
            activeGlitchCoroutine = null;

            recordInProgress.timeGlitchDisabled = disableTimeRelative; // Record disable time
        }
        else // Handle case where script was null to begin with
        {
            recordInProgress.triggeredGlitchType = "Error: Target Script Null";
            recordInProgress.timeGlitchDisabled = enableTimeRelative; // Mark disable time same as enable time
            Debug.LogError($"[{gameObject.name}] Target Glitch Script was null when trying to activate glitch.", this);

            // Ensure state transitions correctly even on error
            currentState = State.Cooldown; // Go to cooldown to prevent immediate re-triggering
            activeGlitchCoroutine = null;
            if (activeCooldownCoroutine == null) {
                activeCooldownCoroutine = StartCoroutine(CooldownSequence());
            }
            yield break; // Exit this coroutine
        }


        // --- Log the completed event ---
        glitchRecords.Add(recordInProgress);

        // --- Start Cooldown ---
        if (activeCooldownCoroutine == null)
        {
            activeCooldownCoroutine = StartCoroutine(CooldownSequence());
        }
        else
        {
             Debug.LogWarning($"[{gameObject.name}] CooldownSequence was somehow already running. Resetting state.");
             ResetCoroutinesAndState(); // Reset if something is wrong
        }
    }

    IEnumerator CooldownSequence()
    {
        currentState = State.Cooldown;
        Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState})] Entering cooldown for {glitchCooldownDuration:F2}s.");

        yield return new WaitForSeconds(glitchCooldownDuration);

        Debug.Log($"[{Time.time - sessionStartTime:F2}s ({currentState} -> Idle)] Cooldown finished. Returning to Idle.");
        currentState = State.Idle;
        activeCooldownCoroutine = null;
    }


    // --- Helper and Recording Methods ---

    Vector3[] GetBoundsCorners(Bounds bounds)
    {
        // (Same implementation as before)
        Vector3[] corners = new Vector3[8];
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
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

     void SetupLogFile()
     {
        try
        {
             string dirPath = Path.Combine(Application.persistentDataPath, logDirectory);
             Directory.CreateDirectory(dirPath); // Ensure directory exists

             // Sanitize selected target name for filename
             string safeTargetName = selectedTargetRootObject != null ? string.Join("_", selectedTargetRootObject.name.Split(Path.GetInvalidFileNameChars())) : "UNKNOWN_TARGET";
             string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
             string fileName = $"{jsonFileNameBase}_{safeTargetName}_{timestamp}.json";
             fullLogFilePath = Path.Combine(dirPath, fileName);
        }
        catch (Exception e)
        {
             Debug.LogError($"[{gameObject.name}] Failed to set up log file path: {e.Message}", this);
             fullLogFilePath = ""; // Indicate failure
        }
     }

    void SaveGlitchData()
    {
        if (logWritten || glitchRecords == null || glitchRecords.Count == 0 || string.IsNullOrEmpty(fullLogFilePath))
        {
            if (!logWritten && (glitchRecords == null || glitchRecords.Count == 0))
                 Debug.Log($"[{gameObject.name}] No glitch events recorded, skipping JSON save.");
            else if (logWritten)
                 Debug.Log($"[{gameObject.name}] Log already written, skipping save.");
             else if (string.IsNullOrEmpty(fullLogFilePath))
                 Debug.LogError($"[{gameObject.name}] Cannot save log, file path is invalid.", this);
            return;
        }

        // Sort records by detection time
        glitchRecords.Sort((a, b) => a.timeVisibleDetected.CompareTo(b.timeVisibleDetected));

        GlitchLogDataWrapper dataWrapper = new GlitchLogDataWrapper
        {
            selectedTargetObjectName = selectedTargetRootObject != null ? selectedTargetRootObject.name : "ERROR: Target Lost",
            configuredStartDelay = this.glitchStartDelay,
            configuredMinActiveDuration = this.minGlitchActiveDuration,
            configuredMaxActiveDuration = this.maxGlitchActiveDuration,
            configuredCooldown = this.glitchCooldownDuration,
            sessionStartTimeAbsolute = this.sessionStartTime, // Using Time.time at start
            glitchEvents = this.glitchRecords
        };

        try
        {
            string json = JsonUtility.ToJson(dataWrapper, true); // Pretty print
            File.WriteAllText(fullLogFilePath, json);
            Debug.Log($"[{gameObject.name}] Glitch data ({glitchRecords.Count} events) saved successfully to: {fullLogFilePath}");
            logWritten = true; // Mark as written
        }
        catch (Exception e)
        {
            Debug.LogError($"[{gameObject.name}] Failed to save glitch data to {fullLogFilePath}. Error: {e.Message}\nStackTrace: {e.StackTrace}", this);
        }
    }

    void ResetCoroutinesAndState(bool disableGlitchScript = true)
    {
         if (disableGlitchScript && selectedTargetGlitchScript != null)
         {
            selectedTargetGlitchScript.enabled = false; // Ensure glitch is off
         }

         // Stop any active coroutines related to the state machine
         if(activeVisibilityCoroutine != null) StopCoroutine(activeVisibilityCoroutine);
         if(activeGlitchCoroutine != null) StopCoroutine(activeGlitchCoroutine);
         if(activeCooldownCoroutine != null) StopCoroutine(activeCooldownCoroutine);

         // Clear references
         activeVisibilityCoroutine = null;
         activeGlitchCoroutine = null;
         activeCooldownCoroutine = null;

         // Reset state only if not already disabled
         if(currentState != State.Disabled)
         {
             currentState = State.Idle;
             Debug.Log($"[{Time.time - sessionStartTime:F2}s] State reset to Idle.");
         }
         // Note: wasTargetVisibleLastCheck is managed by Update loop based on current visibility
    }
}