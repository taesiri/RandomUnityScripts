using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Required for LINQ operations like Any()
using System.IO;   // Required for file operations
using System;      // Required for DateTime

// Define a structure to hold log entry data
[System.Serializable]
public struct GlitchLogEntry
{
    public string glitchType;
    public int startFrameNumber;
    public float startTimestamp;
    public float durationSeconds;
    public float endTimestamp;
    public int endFrameNumber;   // Added end frame
    public int durationFrames; // Added duration in frames

    public GlitchLogEntry(string type, int startFrame, float startTime, float durationSec, float endTime, int endFrame)
    {
        glitchType = type;
        startFrameNumber = startFrame;
        startTimestamp = startTime;
        durationSeconds = durationSec;
        endTimestamp = endTime;
        endFrameNumber = endFrame;
        durationFrames = endFrame - startFrame; // Calculate frame duration
    }

    // Constructor for permanent glitch (no duration)
    public GlitchLogEntry(string type, int frame, float time)
    {
        glitchType = type;
        startFrameNumber = frame;
        startTimestamp = time;
        durationSeconds = 0f;
        endTimestamp = time; // End time is same as start
        endFrameNumber = frame; // End frame is same as start
        durationFrames = 0;
    }
}

// Wrapper class for JsonUtility serialization of the list
[System.Serializable]
public class GlitchLogData
{
    // Add fields to log the time window configuration
    public bool isRestrictedToWindow;
    public float windowStartTime;
    public float windowEndTime;
    public float chosenFirstGlitchTime; // The randomly picked time to start glitching

    public List<GlitchLogEntry> entries = new List<GlitchLogEntry>();
}

public class AdvancedHierarchicalGlitch : MonoBehaviour
{
    // Enum to define how the texture glitch behaves
    public enum TextureGlitchMode
    {
        RemoveTextures,     // Set textures to null (often results in white/unlit depending on shader)
        SolidColor,         // Set textures to null and base color to a specific color
        ReplaceWithTexture  // Replace textures with a specific glitch texture
    }

    [Header("Target")]
    [Tooltip("The top-level Transform whose children (and itself) with Renderers will be glitched.")]
    [SerializeField] private Transform targetRoot;

    [Header("Glitch Timing")]
    [Tooltip("Minimum time (seconds) between any glitch events.")]
    [SerializeField] private float minTimeBetweenGlitches = 1.0f;
    [Tooltip("Maximum time (seconds) between any glitch events.")]
    [SerializeField] private float maxTimeBetweenGlitches = 5.0f;
    [Space]
    [Tooltip("Enforce a single, fixed duration for all momentary glitches, overriding the min/max settings below.")]
    [SerializeField] private bool enforceFixedDuration = false;
    [Tooltip("The fixed duration (seconds) to use for momentary glitches when 'Enforce Fixed Duration' is checked.")]
    [SerializeField] [Min(0.01f)] private float fixedGlitchDuration = 2.0f;
    [Space]
    [Tooltip("Restrict glitching activity to only occur within a specific time window?")]
    [SerializeField] private bool restrictGlitchingToWindow = false;
    [Tooltip("The earliest time (seconds since game start) that the first glitch can potentially occur.")]
    [SerializeField] [Min(0f)] private float glitchWindowStartTime = 0f;
    [Tooltip("The latest time (seconds since game start) by which the first glitch must have been scheduled if the window is active.")]
    [SerializeField] [Min(0f)] private float glitchWindowEndTime = 50f;

    [Header("Momentary: Target Selection")]
    [Tooltip("Chance (0 to 1) that ANY individual glitchable object will be affected during a momentary Blink or Individual Jump glitch event.")]
    [Range(0f, 1f)]
    [SerializeField] private float individualGlitchTargetChance = 0.5f;

    // --- Configuration for each Momentary Glitch Type ---

    [Header("Momentary: Blink Glitch")]
    [Tooltip("Enable momentary random blinking during glitch events.")]
    [SerializeField] private bool enableBlinkGlitch = true;
    [Tooltip("Minimum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float minBlinkDuration = 0.1f;
    [Tooltip("Maximum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float maxBlinkDuration = 0.5f;
    [Tooltip("During a blink glitch, the chance (0 to 1) for an affected Renderer to be VISIBLE each frame.")]
    [Range(0f, 1f)]
    [SerializeField] private float blinkVisibilityChanceDuringGlitch = 0.3f;

    [Header("Momentary: Individual Transform Jump Glitch")]
    [Tooltip("Enable momentary random position/rotation jumps for individual objects during glitch events.")]
    [SerializeField] private bool enableIndividualTransformJumpGlitch = true;
    [Tooltip("Minimum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float minIndividualJumpDuration = 0.1f;
    [Tooltip("Maximum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float maxIndividualJumpDuration = 0.3f;
    [Tooltip("Maximum distance an affected object can jump from its ORIGINAL local position during a glitch.")]
    [SerializeField] private float individualJumpPositionIntensity = 0.5f;
    [Tooltip("Maximum angle (degrees) an affected object can randomly rotate from its ORIGINAL local rotation during a glitch.")]
    [SerializeField] private float individualJumpRotationIntensity = 15.0f;

    [Header("Momentary: Whole Object Jump Glitch")]
    [Tooltip("Enable momentary random position/rotation jumps for the ENTIRE targetRoot during glitch events.")]
    [SerializeField] private bool enableWholeObjectJumpGlitch = true;
    [Tooltip("Minimum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float minWholeObjectJumpDuration = 0.1f;
    [Tooltip("Maximum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float maxWholeObjectJumpDuration = 0.4f;
    [Tooltip("Maximum distance the entire targetRoot can jump from its ORIGINAL local position during a glitch.")]
    [SerializeField] private float wholeObjectJumpPositionIntensity = 0.2f;
    [Tooltip("Maximum angle (degrees) the entire targetRoot can randomly rotate from its ORIGINAL local rotation during a glitch.")]
    [SerializeField] private float wholeObjectJumpRotationIntensity = 5.0f;

    [Header("Momentary: Texture Glitch")]
    [Tooltip("Enable momentarily altering textures on materials during glitch events.")]
    [SerializeField] private bool enableTextureGlitch = true;
    [Tooltip("Minimum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float minTextureGlitchDuration = 0.2f;
    [Tooltip("Maximum duration for this glitch type (used if 'Enforce Fixed Duration' is false).")]
    [Min(0.01f)] public float maxTextureGlitchDuration = 0.6f;
    [Space]
    [Tooltip("How the texture glitch affects materials:\n" +
             "- RemoveTextures: Sets texture properties to null.\n" +
             "- SolidColor: Sets textures to null and the main color property (_Color or _BaseColor) to Glitch Color.\n" +
             "- ReplaceWithTexture: Replaces texture properties with Glitch Texture.")]
    [SerializeField] private TextureGlitchMode textureGlitchMode = TextureGlitchMode.RemoveTextures;
    [Tooltip("The color to use when Texture Glitch Mode is SolidColor. Affects standard _Color or _BaseColor properties.")]
    [SerializeField] private Color textureGlitchColor = Color.magenta; // Default glitchy color
    [Tooltip("The texture to use when Texture Glitch Mode is ReplaceWithTexture.")]
    [SerializeField] private Texture textureGlitchReplacement = null;


    [Header("Permanent Disappearance Glitch")]
    [Tooltip("Enable the possibility of objects disappearing permanently.")]
    [SerializeField] private bool enablePermanentDisappearance = false;
    [Tooltip("Chance (0 to 1) that a glitch event triggers a permanent disappearance instead of a momentary one.")]
    [Range(0f, 1f)]
    [SerializeField] private float permanentDisappearanceChance = 0.05f; // Low chance by default
    [Tooltip("Chance (0 to 1) for each glitchable object to be permanently disappeared during such an event.")]
    [Range(0f, 1f)]
    [SerializeField] private float disappearanceTargetChance = 0.1f; // Affects only a few targets usually


    [Header("Glitch Control")]
    [Tooltip("Maximum number of glitch events to trigger. Set to 0 or less for infinite glitches.")]
    [SerializeField] private int maxGlitchEvents = 1;

    [Header("Logging")]
    [Tooltip("Enable logging of glitch events to a JSON file.")]
    [SerializeField] private bool enableLogging = false;
    [Tooltip("Directory relative to Application.persistentDataPath where logs will be saved.")]
    [SerializeField] private string logDirectory = "GlitchLogs";

    // --- Private Variables ---

    // --- State Storage ---
    private Dictionary<Transform, Vector3> initialLocalPositions = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Quaternion> initialLocalRotations = new Dictionary<Transform, Quaternion>();
    private Dictionary<Renderer, bool> initialRendererStates = new Dictionary<Renderer, bool>();
    private Vector3 initialRootLocalPosition;
    private Quaternion initialRootLocalRotation;
    // Original textures: Maps Material instance ID -> Property ID -> Original Texture
    private Dictionary<int, Dictionary<int, Texture>> originalMaterialTextures = new Dictionary<int, Dictionary<int, Texture>>();
    // Original colors: Maps Material instance ID -> Property ID -> Original Color
    private Dictionary<int, Dictionary<int, Color>> originalMaterialColors = new Dictionary<int, Dictionary<int, Color>>();
    private Dictionary<Renderer, List<int>> rendererMaterialInstanceIDs = new Dictionary<Renderer, List<int>>(); // Map Renderer -> List of its original material instance IDs
    // Common color property IDs (pre-calculated)
    private static readonly int colorPropertyID = Shader.PropertyToID("_Color");
    private static readonly int baseColorPropertyID = Shader.PropertyToID("_BaseColor"); // For URP/HDRP Lit


    // --- Active Lists (can shrink) ---
    private List<Transform> glitchableTransforms = new List<Transform>();
    private List<Renderer> glitchableRenderers = new List<Renderer>();

    // --- Glitch State ---
    private float timeUntilNextGlitch = 0f;
    private bool isMomentaryGlitchActive = false;
    private float currentGlitchEndTime = 0f;
    private int currentGlitchCount = 0; // Counter for triggered glitches
    private float firstGlitchTime = -1f;        // Time the first glitch is scheduled to start (if window is active)
    private bool waitingForFirstGlitch = false; // Flag indicating if we are waiting for the first glitch time

    // --- Current Momentary Glitch Tracking ---
    private System.Type activeMomentaryGlitchType = null; // Track which type is active
    // Store start info for logging at the end of the glitch
    private string currentGlitchLogType = "None";
    private int currentGlitchStartFrame = 0;
    private float currentGlitchStartTime = 0f;
    private float currentGlitchDurationSec = 0f;
    private float currentGlitchScheduledEndTime = 0f; // Use this for consistency

    // Store targets affected by the CURRENT specific momentary glitch for proper reset
    private HashSet<Renderer> renderersInCurrentBlink = new HashSet<Renderer>();
    private HashSet<Transform> transformsInCurrentIndividualJump = new HashSet<Transform>();
    // No specific tracking needed for WholeJump (always root) or Texture (all glitchable)

    private bool isSetupComplete = false;
    private List<System.Type> availableMomentaryGlitches = new List<System.Type>(); // Stores enabled types

    // Helper dummy classes for glitch type identification
    private class BlinkGlitchState { }
    private class IndividualJumpGlitchState { }
    private class WholeObjectJumpGlitchState { }
    private class TextureGlitchState { }

    // --- Logging Data ---
    private GlitchLogData glitchLog = new GlitchLogData();
    private bool logWritten = false; // Prevent writing multiple times unnecessarily

    void Awake()
    {
        InitializeGlitchTargets();
        if (isSetupComplete)
        {
            ScheduleNextGlitch();
        }
    }

    void OnEnable()
    {
        // Reset counter and log state if re-enabled
        currentGlitchCount = 0;
        glitchLog = new GlitchLogData(); // Reset log data
        logWritten = false;
        firstGlitchTime = -1f; // Reset calculated first glitch time
        waitingForFirstGlitch = false; // Reset waiting state

        if (!isSetupComplete)
        {
            InitializeGlitchTargets();
        }

        if (isSetupComplete)
        {
            ResetMomentaryGlitches(); // Reset any lingering state from before disable

            // --- Schedule based on window restriction ---
            if (restrictGlitchingToWindow && glitchWindowEndTime > glitchWindowStartTime)
            {
                firstGlitchTime = UnityEngine.Random.Range(glitchWindowStartTime, glitchWindowEndTime);
                waitingForFirstGlitch = true;
                timeUntilNextGlitch = float.MaxValue; // Don't schedule normally yet

                // Log window info
                glitchLog.isRestrictedToWindow = true;
                glitchLog.windowStartTime = glitchWindowStartTime;
                glitchLog.windowEndTime = glitchWindowEndTime;
                glitchLog.chosenFirstGlitchTime = firstGlitchTime;

                Debug.Log($"[{gameObject.name}] Glitching restricted to window [{glitchWindowStartTime:F2}s - {glitchWindowEndTime:F2}s]. First glitch scheduled for ~{firstGlitchTime:F2}s.", this);
            }
            else
            {
                // Not restricting, or window invalid - schedule normally if allowed
                glitchLog.isRestrictedToWindow = false; // Log that we are not restricted
                glitchLog.windowStartTime = -1f;      // Set to -1 because window is not used
                glitchLog.windowEndTime = -1f;        // Set to -1 because window is not used
                glitchLog.chosenFirstGlitchTime = -1f; // Set to -1 because no specific first time was chosen

                if (maxGlitchEvents <= 0 || currentGlitchCount < maxGlitchEvents)
                {
                    ScheduleNextGlitch();
                }
                else
                {
                    timeUntilNextGlitch = float.MaxValue; // Max events reached at start
                }
            }
        }
    }

    void InitializeGlitchTargets()
    {
        if (targetRoot == null)
        {
            Debug.LogError($"[{gameObject.name}] AdvancedHierarchicalGlitch ERROR: 'Target Root' MUST be assigned!", this);
            enabled = false;
            isSetupComplete = false;
            return;
        }

        // --- Clear Previous Data ---
        initialLocalPositions.Clear();
        initialLocalRotations.Clear();
        initialRendererStates.Clear();
        originalMaterialTextures.Clear();
        originalMaterialColors.Clear(); // Clear color storage
        rendererMaterialInstanceIDs.Clear();
        glitchableTransforms.Clear();
        glitchableRenderers.Clear();
        renderersInCurrentBlink.Clear();
        transformsInCurrentIndividualJump.Clear();
        activeMomentaryGlitchType = null;

        // --- Store Root Transform State ---
        initialRootLocalPosition = targetRoot.localPosition;
        initialRootLocalRotation = targetRoot.localRotation;

        // --- Find Renderers and Store Initial States ---
        Renderer[] allRenderers = targetRoot.GetComponentsInChildren<Renderer>(true);

        if (allRenderers.Length == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] AdvancedHierarchicalGlitch: No Renderers found within the hierarchy of '{targetRoot.name}'.", this);
            isSetupComplete = false;
            return;
        }

        foreach (Renderer rend in allRenderers)
        {
            if (!rend.gameObject.activeInHierarchy && enablePermanentDisappearance) continue;

            Transform t = rend.transform;

            if (!initialLocalPositions.ContainsKey(t))
            {
                initialLocalPositions.Add(t, t.localPosition);
                initialLocalRotations.Add(t, t.localRotation);
                glitchableTransforms.Add(t);
            }

            if (!initialRendererStates.ContainsKey(rend))
            {
                initialRendererStates.Add(rend, rend.enabled);
                glitchableRenderers.Add(rend);
            }

            // --- Store Original Material Properties ---
            Material[] sharedMaterials = rend.sharedMaterials;
            List<int> matInstanceIDsForRenderer = new List<int>();
            rendererMaterialInstanceIDs[rend] = matInstanceIDsForRenderer;

            foreach (Material mat in sharedMaterials)
            {
                if (mat == null) continue;

                int matInstanceID = mat.GetInstanceID();
                matInstanceIDsForRenderer.Add(matInstanceID);

                // Process this material's properties only once
                if (!originalMaterialTextures.ContainsKey(matInstanceID))
                {
                    var textureDict = new Dictionary<int, Texture>();
                    originalMaterialTextures[matInstanceID] = textureDict;
                    var colorDict = new Dictionary<int, Color>(); // Create color dict here
                    originalMaterialColors[matInstanceID] = colorDict;

                    // Store Textures
                    string[] texturePropertyNames = mat.GetTexturePropertyNames();
                    foreach(string propName in texturePropertyNames)
                    {
                        if (mat.HasProperty(propName))
                        {
                            int propID = Shader.PropertyToID(propName);
                            textureDict[propID] = mat.GetTexture(propID);
                        }
                    }

                    // Store Colors (specifically standard ones for solid color glitch)
                    if (mat.HasProperty(colorPropertyID))
                    {
                        colorDict[colorPropertyID] = mat.GetColor(colorPropertyID);
                    }
                    if (mat.HasProperty(baseColorPropertyID)) // Check for URP/HDRP base color too
                    {
                         // Avoid storing if same ID as _Color and already stored
                        if (!colorDict.ContainsKey(baseColorPropertyID))
                        {
                           colorDict[baseColorPropertyID] = mat.GetColor(baseColorPropertyID);
                        }
                    }
                }
            }
        }

        // --- Final Checks and Setup ---
        if (glitchableRenderers.Count == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] AdvancedHierarchicalGlitch: No active Renderers found or remaining in '{targetRoot.name}'. Disabling.", this);
            isSetupComplete = false;
            enabled = false;
            return;
        }

        // --- Build list of available momentary glitch types ---
        availableMomentaryGlitches.Clear();
        if(enableBlinkGlitch) availableMomentaryGlitches.Add(typeof(BlinkGlitchState));
        if(enableIndividualTransformJumpGlitch) availableMomentaryGlitches.Add(typeof(IndividualJumpGlitchState));
        if(enableWholeObjectJumpGlitch) availableMomentaryGlitches.Add(typeof(WholeObjectJumpGlitchState));
        if(enableTextureGlitch) availableMomentaryGlitches.Add(typeof(TextureGlitchState));

        // Reset glitch counter and log on full initialization
        currentGlitchCount = 0;
        glitchLog = new GlitchLogData();
        logWritten = false;

        isSetupComplete = true;
        Debug.Log($"[{gameObject.name}] AdvancedHierarchicalGlitch Initialized. Found {glitchableRenderers.Count} active renderers in '{targetRoot.name}'. Ready to glitch {availableMomentaryGlitches.Count} momentary types. Max Events: {(maxGlitchEvents <= 0 ? "Infinite" : maxGlitchEvents.ToString())}");
    }

    void ScheduleNextGlitch()
    {
        // Check window restriction *before* scheduling
        if (restrictGlitchingToWindow && Time.time > glitchWindowEndTime)
        {
            timeUntilNextGlitch = float.MaxValue; // Window closed
            Debug.Log($"[{gameObject.name}] Glitch window ended at {glitchWindowEndTime:F2}s. No more glitches will be scheduled.", this);
            return;
        }

        // Only schedule if we haven't reached the limit (or limit is infinite)
        if (isSetupComplete && (maxGlitchEvents <= 0 || currentGlitchCount < maxGlitchEvents))
        {
            timeUntilNextGlitch = UnityEngine.Random.Range(minTimeBetweenGlitches, maxTimeBetweenGlitches);
            isMomentaryGlitchActive = false;
            activeMomentaryGlitchType = null; // Ensure no glitch type is marked as active
        }
        else
        {
             // Stop scheduling if limit reached
            timeUntilNextGlitch = float.MaxValue; // Effectively stop timer
             if(maxGlitchEvents > 0) Debug.Log($"[{gameObject.name}] Max glitch events ({maxGlitchEvents}) reached. No more glitches will be scheduled.");
        }
    }

    void Update()
    {
        if (!isSetupComplete || glitchableRenderers.Count == 0) return;

        // --- Handle Waiting for First Glitch ---
        if (waitingForFirstGlitch)
        {
            if (Time.time >= firstGlitchTime)
            {
                waitingForFirstGlitch = false;
                Debug.Log($"[{gameObject.name}] Reached scheduled first glitch time ({firstGlitchTime:F2}s). Starting glitch sequence.", this);
                // Check if still within window before triggering immediately
                if (!restrictGlitchingToWindow || Time.time <= glitchWindowEndTime)
                {
                     // Decide whether to trigger immediately or schedule the first one
                     // Let's trigger immediately for responsiveness after waiting
                     if (maxGlitchEvents <= 0 || currentGlitchCount < maxGlitchEvents)
                     {
                         TriggerGlitchEvent(); // Trigger the first glitch now
                     } else {
                         ScheduleNextGlitch(); // Max events already reached? Schedule to stop.
                     }

                } else {
                    // We reached the first glitch time but the window already closed
                    timeUntilNextGlitch = float.MaxValue;
                     Debug.Log($"[{gameObject.name}] Reached scheduled first glitch time ({firstGlitchTime:F2}s), but glitch window already ended at {glitchWindowEndTime:F2}s.", this);
                }
            }
            else
            {
                return; // Still waiting, do nothing else this frame
            }
        }

        // --- Check Window End ---
        // (Done inside ScheduleNextGlitch and before triggering)
        bool canScheduleNew = !restrictGlitchingToWindow || Time.time <= glitchWindowEndTime;


        // Check if we've already reached the max count before proceeding
        bool canGlitch = maxGlitchEvents <= 0 || currentGlitchCount < maxGlitchEvents;

        if (isMomentaryGlitchActive)
        {
            ApplyMomentaryGlitchesPerFrame();

            if (Time.time >= currentGlitchEndTime)
            {
                ResetMomentaryGlitches();
                // Schedule next only if allowed by count AND window
                if (canGlitch && canScheduleNew)
                {
                   ScheduleNextGlitch();
                } else {
                    timeUntilNextGlitch = float.MaxValue; // Ensure timer stays stopped
                }
            }
        }
        else if (canGlitch && canScheduleNew) // Only countdown and trigger if allowed by count AND window
        {
            timeUntilNextGlitch -= Time.deltaTime;
            if (timeUntilNextGlitch <= 0)
            {
                TriggerGlitchEvent();
            }
        }
        // If !canGlitch or !canScheduleNew and not currently active, do nothing.
    }

    void TriggerGlitchEvent()
    {
        // Double check limits before proceeding (safety)
        if (maxGlitchEvents > 0 && currentGlitchCount >= maxGlitchEvents)
        {
            ScheduleNextGlitch(); // This will set timeUntilNextGlitch to MaxValue
            return;
        }
        if (restrictGlitchingToWindow && Time.time > glitchWindowEndTime)
        {
            ScheduleNextGlitch(); // This will set timeUntilNextGlitch to MaxValue
            return;
        }

        string triggeredGlitchType = "None"; // Local var for decision making
        float triggeredGlitchDuration = 0f; // Local var for decision making
        bool glitchTriggered = false;
        int glitchStartFrame = 0; // Local var for start frame
        float glitchStartTime = 0f; // Local var for start time

        // --- Decide: Permanent or Momentary? ---
        if (enablePermanentDisappearance && UnityEngine.Random.value < permanentDisappearanceChance)
        {
            glitchStartFrame = Time.frameCount; // Capture start info
            glitchStartTime = Time.time;
            ApplyPermanentDisappearance(); // This internally checks isSetupComplete
            if (isSetupComplete) // Check if disabling occurred within ApplyPermanentDisappearance
            {
                 triggeredGlitchType = "PermanentDisappearance";
                 // Permanent glitches are logged immediately as they have no duration/end time different from start
                 LogGlitchEvent(triggeredGlitchType, glitchStartFrame, glitchStartTime);
                 glitchTriggered = true;
                 currentGlitchCount++; // Increment count here for permanent
            }
            else
            {
                 glitchTriggered = false; // Don't count or schedule if disabled
            }
            // No need to store start info for permanent, it's logged above.
        }
        // --- Decide: Momentary Glitch ---
        else if (availableMomentaryGlitches.Count > 0)
        {
            isMomentaryGlitchActive = true;
            glitchTriggered = true; // Mark that a momentary glitch is starting
            currentGlitchCount++; // Increment count for momentary

            // --- Reset momentary tracking ---
            renderersInCurrentBlink.Clear();
            transformsInCurrentIndividualJump.Clear();
            activeMomentaryGlitchType = null;

            // --- Capture Start Info for Logging Later ---
            currentGlitchStartFrame = Time.frameCount;
            currentGlitchStartTime = Time.time;

            // --- Choose ONE momentary glitch type for this event ---
            int randomIndex = UnityEngine.Random.Range(0, availableMomentaryGlitches.Count);
            activeMomentaryGlitchType = availableMomentaryGlitches[randomIndex];

            // --- Determine Glitch Duration ---
            float currentMomentaryDuration;
            if (enforceFixedDuration)
            {
                currentMomentaryDuration = fixedGlitchDuration;
            }
            else // Use type-specific min/max
            {
                if (activeMomentaryGlitchType == typeof(BlinkGlitchState))
                    currentMomentaryDuration = UnityEngine.Random.Range(minBlinkDuration, maxBlinkDuration);
                else if (activeMomentaryGlitchType == typeof(IndividualJumpGlitchState))
                    currentMomentaryDuration = UnityEngine.Random.Range(minIndividualJumpDuration, maxIndividualJumpDuration);
                else if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState))
                    currentMomentaryDuration = UnityEngine.Random.Range(minWholeObjectJumpDuration, maxWholeObjectJumpDuration);
                else if (activeMomentaryGlitchType == typeof(TextureGlitchState))
                    currentMomentaryDuration = UnityEngine.Random.Range(minTextureGlitchDuration, maxTextureGlitchDuration);
                else
                    currentMomentaryDuration = 0.1f; // Safety default if type not matched
            }


            // --- Activate the chosen glitch and select targets ---
            if (activeMomentaryGlitchType == typeof(BlinkGlitchState))
            {
                triggeredGlitchType = "Blink";
                // Duration already set above
                foreach (Renderer rend in glitchableRenderers)
                {
                    if (rend != null && UnityEngine.Random.value < individualGlitchTargetChance)
                    {
                        renderersInCurrentBlink.Add(rend);
                    }
                }
            }
            else if (activeMomentaryGlitchType == typeof(IndividualJumpGlitchState))
            {
                 triggeredGlitchType = "IndividualJump";
                 // Duration already set above
                HashSet<Transform> potentialJumpTransforms = new HashSet<Transform>(glitchableRenderers.Where(r => r != null).Select(r => r.transform));
                foreach (Transform t in potentialJumpTransforms)
                {
                    if (t != null && initialLocalPositions.ContainsKey(t) && UnityEngine.Random.value < individualGlitchTargetChance)
                    {
                        transformsInCurrentIndividualJump.Add(t);
                    }
                }
            }
            else if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState))
            {
                 triggeredGlitchType = "WholeObjectJump";
                 // Duration already set above
                // No specific targets needed, affects root
            }
            else if (activeMomentaryGlitchType == typeof(TextureGlitchState))
            {
                 triggeredGlitchType = $"Texture ({textureGlitchMode})";
                 // Duration already set above
                ApplyTextureGlitchStart(); // Apply texture change ONCE at the start
            }
             else
            {
                 // Safety net if somehow the chosen type wasn't handled above
                 triggeredGlitchType = "UnknownMomentary";
            }

             // --- Store Start Info For Logging ---
             currentGlitchLogType = triggeredGlitchType; // Store type for logging
             currentGlitchDurationSec = currentMomentaryDuration; // Store duration for logging
             currentGlitchScheduledEndTime = currentGlitchStartTime + currentMomentaryDuration; // Store calculated end time for logging

             // Set the actual end time for the Update loop check
             currentGlitchEndTime = currentGlitchScheduledEndTime;

             ApplyMomentaryGlitchesPerFrame(); // Apply the first frame effect immediately
        }
        else
        {
             glitchTriggered = false;
             // Don't schedule here, let the main loop handle scheduling if no glitch occurred
        }

        // --- Schedule the next glitch IF one was triggered and we are still active ---
        if (glitchTriggered && isSetupComplete)
        {
            ScheduleNextGlitch(); // Schedule the *next* one (if allowed by counter)
        }
        else if (!isMomentaryGlitchActive) // If no glitch triggered and not already in one
        {
             ScheduleNextGlitch(); // Schedule anyway to keep trying
        }
    }

    // Apply per-frame effects (like blinking) or initial state (jumps)
    void ApplyMomentaryGlitchesPerFrame()
    {
        // --- Apply Blink Effect (Per Frame) ---
        if (activeMomentaryGlitchType == typeof(BlinkGlitchState))
        {
            foreach (Renderer rend in renderersInCurrentBlink)
            {
                if (rend != null) rend.enabled = (UnityEngine.Random.value <= blinkVisibilityChanceDuringGlitch);
            }
        }

        // --- Apply Individual Transform Jump (Once at start) ---
        if (activeMomentaryGlitchType == typeof(IndividualJumpGlitchState))
        {
            // This is applied once when triggered, but check the type here
            foreach (Transform t in transformsInCurrentIndividualJump)
            {
                if (t != null && initialLocalPositions.ContainsKey(t))
                {
                    Vector3 initialPos = initialLocalPositions[t];
                    Quaternion initialRot = initialLocalRotations[t];
                    t.localPosition = initialPos + UnityEngine.Random.insideUnitSphere * individualJumpPositionIntensity;
                    t.localRotation = initialRot * Quaternion.Euler(
                        UnityEngine.Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity),
                        UnityEngine.Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity),
                        UnityEngine.Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity)
                    );
                }
            }
        }

        // --- Apply Whole Object Jump (Once at start) ---
        if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState) && targetRoot != null)
        {
            targetRoot.localPosition = initialRootLocalPosition + UnityEngine.Random.insideUnitSphere * wholeObjectJumpPositionIntensity;
            targetRoot.localRotation = initialRootLocalRotation * Quaternion.Euler(
                UnityEngine.Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity),
                UnityEngine.Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity),
                UnityEngine.Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity)
            );
        }

        // --- Texture Glitch (Applied ONCE at start in ApplyTextureGlitchStart) ---
        // No per-frame action needed for the texture change itself.
    }

    // Apply texture alterations based on the chosen mode ONCE per texture glitch
    void ApplyTextureGlitchStart()
    {
        if (activeMomentaryGlitchType != typeof(TextureGlitchState) || !enableTextureGlitch) return;

        foreach (Renderer rend in glitchableRenderers)
        {
            if (rend == null) continue;

            Material[] currentMaterials = rend.materials; // Get instances
            if (currentMaterials == null) continue;
            if (!rendererMaterialInstanceIDs.TryGetValue(rend, out List<int> originalMatIDs)) continue;

            for (int i = 0; i < currentMaterials.Length && i < originalMatIDs.Count; ++i)
            {
                Material matInstance = currentMaterials[i];
                int originalMatID = originalMatIDs[i];
                if (matInstance == null) continue;
                if (!originalMaterialTextures.TryGetValue(originalMatID, out var textureDict)) continue;
                // Color dict lookup needed only for SolidColor mode, done inside switch

                switch (textureGlitchMode)
                {
                    case TextureGlitchMode.RemoveTextures:
                        foreach (var kvp in textureDict) // kvp: Property ID -> Original Texture
                        {
                            if (matInstance.HasProperty(kvp.Key))
                            {
                                matInstance.SetTexture(kvp.Key, null);
                            }
                        }
                        break;

                    case TextureGlitchMode.SolidColor:
                         // First remove textures
                         foreach (var kvp in textureDict)
                         {
                             if (matInstance.HasProperty(kvp.Key))
                             {
                                 matInstance.SetTexture(kvp.Key, null);
                             }
                         }
                         // Then set color (try _Color first, then _BaseColor)
                         if (matInstance.HasProperty(colorPropertyID))
                         {
                            matInstance.SetColor(colorPropertyID, textureGlitchColor);
                         }
                         else if (matInstance.HasProperty(baseColorPropertyID)) // Fallback for URP/HDRP Lit
                         {
                            matInstance.SetColor(baseColorPropertyID, textureGlitchColor);
                         }
                         // else: Material doesn't have a standard color property we know.
                         break;

                    case TextureGlitchMode.ReplaceWithTexture:
                         if (textureGlitchReplacement != null) // Only if a replacement is provided
                         {
                             foreach (var kvp in textureDict)
                             {
                                 if (matInstance.HasProperty(kvp.Key))
                                 {
                                     // Only replace if the original wasn't null? Optional rule.
                                     // if (kvp.Value != null)
                                     matInstance.SetTexture(kvp.Key, textureGlitchReplacement);
                                 }
                             }
                         }
                         else
                         {
                             // Fallback to removing textures if no replacement is set
                             foreach (var kvp in textureDict) { if (matInstance.HasProperty(kvp.Key)) matInstance.SetTexture(kvp.Key, null); }
                             Debug.LogWarning($"Texture Glitch Mode is Replace, but no Glitch Texture assigned. Removing textures instead.", this);
                         }
                         break;
                }
            }
        }
    }

    // Restore objects after a momentary glitch ends
    void ResetMomentaryGlitches()
    {
        if (!isSetupComplete || activeMomentaryGlitchType == null) return; // Nothing to reset if no glitch was active

        // --- Log the completed momentary glitch ---
        int endFrame = Time.frameCount;
        LogGlitchEvent(
            currentGlitchLogType,
            currentGlitchStartFrame,
            currentGlitchStartTime,
            currentGlitchDurationSec,
            currentGlitchScheduledEndTime, // Use the originally calculated end time for consistency
            endFrame
        );

        // --- Reset Blinking Renderers ---
        if (activeMomentaryGlitchType == typeof(BlinkGlitchState))
        {
            foreach (Renderer rend in renderersInCurrentBlink)
            {
                if (rend != null && initialRendererStates.ContainsKey(rend))
                {
                    rend.enabled = initialRendererStates[rend];
                }
            }
        }

        // --- Reset Individual Jumped Transforms ---
        if (activeMomentaryGlitchType == typeof(IndividualJumpGlitchState))
        {
            foreach (Transform t in transformsInCurrentIndividualJump)
            {
                if (t != null && initialLocalPositions.ContainsKey(t))
                {
                    t.localPosition = initialLocalPositions[t];
                    t.localRotation = initialLocalRotations[t];
                }
            }
        }

        // --- Reset Whole Object Jump ---
        if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState) && targetRoot != null)
        {
            targetRoot.localPosition = initialRootLocalPosition;
            targetRoot.localRotation = initialRootLocalRotation;
        }

        // --- Reset Textures and Colors ---
        if (activeMomentaryGlitchType == typeof(TextureGlitchState))
        {
            foreach (Renderer rend in glitchableRenderers) // Iterate ALL, as all were potentially affected
            {
                if (rend == null) continue;
                Material[] currentMaterials = rend.materials;
                if (currentMaterials == null) continue;
                if (!rendererMaterialInstanceIDs.TryGetValue(rend, out List<int> originalMatIDs)) continue;

                for (int i = 0; i < currentMaterials.Length && i < originalMatIDs.Count; ++i)
                {
                    Material matInstance = currentMaterials[i];
                    int originalMatID = originalMatIDs[i];
                    if (matInstance == null) continue;

                    // Restore Textures
                    if (originalMaterialTextures.TryGetValue(originalMatID, out var textureDict))
                    {
                        foreach (var kvp in textureDict) // Property ID -> Original Texture
                        {
                            if (matInstance.HasProperty(kvp.Key))
                            {
                                matInstance.SetTexture(kvp.Key, kvp.Value); // Restore original tex
                            }
                        }
                    }

                    // Restore Colors (if SolidColor mode was used)
                    if (textureGlitchMode == TextureGlitchMode.SolidColor) // Only restore color if that mode was active
                    {
                        if (originalMaterialColors.TryGetValue(originalMatID, out var colorDict))
                        {
                             // Restore _Color if it was stored
                             if(colorDict.TryGetValue(colorPropertyID, out Color originalColor) && matInstance.HasProperty(colorPropertyID))
                             {
                                 matInstance.SetColor(colorPropertyID, originalColor);
                             }
                              // Restore _BaseColor if it was stored
                             if(colorDict.TryGetValue(baseColorPropertyID, out Color originalBaseColor) && matInstance.HasProperty(baseColorPropertyID))
                             {
                                 matInstance.SetColor(baseColorPropertyID, originalBaseColor);
                             }
                        }
                    }
                }
            }
        }

        // --- Clear momentary state ---
        isMomentaryGlitchActive = false;
        activeMomentaryGlitchType = null;
        renderersInCurrentBlink.Clear();
        transformsInCurrentIndividualJump.Clear();

        // Clear stored log info
        currentGlitchLogType = "None";
        currentGlitchStartFrame = 0;
        currentGlitchStartTime = 0f;
        currentGlitchDurationSec = 0f;
        currentGlitchScheduledEndTime = 0f;
    }


    void ApplyPermanentDisappearance()
    {
        if (!isSetupComplete) return;

        List<Renderer> renderersToRemove = new List<Renderer>();
        List<Transform> transformsToCheckForRemoval = new List<Transform>();

        foreach (Renderer rend in glitchableRenderers)
        {
            if (rend != null && UnityEngine.Random.value < disappearanceTargetChance)
            {
                renderersToRemove.Add(rend);
                if (!transformsToCheckForRemoval.Contains(rend.transform)) transformsToCheckForRemoval.Add(rend.transform);
                rend.gameObject.SetActive(false);
            }
        }

        if (renderersToRemove.Count > 0)
        {
            foreach (Renderer rendToRemove in renderersToRemove)
            {
                glitchableRenderers.Remove(rendToRemove);
                initialRendererStates.Remove(rendToRemove);
                rendererMaterialInstanceIDs.Remove(rendToRemove);
                // No need to remove from originalMaterialTextures/Colors - keep data just in case
            }

            List<Transform> transformsToRemoveCompletely = new List<Transform>();
            foreach(Transform transformToCheck in transformsToCheckForRemoval)
            {
                if (transformToCheck == null) continue;
                bool transformStillUsed = glitchableRenderers.Any(r => r != null && r.transform == transformToCheck);
                if (!transformStillUsed && transformToCheck != targetRoot) // Don't remove root tracking
                {
                    transformsToRemoveCompletely.Add(transformToCheck);
                }
            }

            foreach (Transform transformToRemove in transformsToRemoveCompletely)
            {
                glitchableTransforms.Remove(transformToRemove);
                initialLocalPositions.Remove(transformToRemove);
                initialLocalRotations.Remove(transformToRemove);
            }

            if (glitchableRenderers.Count == 0)
            {
                Debug.LogWarning($"[{gameObject.name}] All renderers permanently disappeared. Disabling further glitching.", this);
                isSetupComplete = false;
            }
        }
    }

    // --- Logging Methods ---

    // Overload for momentary glitches
    void LogGlitchEvent(string type, int startFrame, float startTime, float durationSec, float endTime, int endFrame)
    {
        if (!enableLogging || string.IsNullOrEmpty(type) || type == "None") return;

        GlitchLogEntry entry = new GlitchLogEntry(type, startFrame, startTime, durationSec, endTime, endFrame);
        glitchLog.entries.Add(entry);
        // Debug.Log($"Logged Glitch: {type}, Frame: {startFrame}-{endFrame} ({entry.durationFrames}f), Start: {startTime:F3}, Duration: {durationSec:F3}, End: {endTime:F3}s");
    }

    // Overload for permanent glitches (simpler)
    void LogGlitchEvent(string type, int frame, float time)
    {
        if (!enableLogging || string.IsNullOrEmpty(type) || type == "None") return;

        GlitchLogEntry entry = new GlitchLogEntry(type, frame, time);
        glitchLog.entries.Add(entry);
        // Debug.Log($"Logged Glitch: {type}, Frame: {frame}, Time: {time:F3}");
    }

    void WriteLogToFile()
    {
         if (!enableLogging || logWritten || glitchLog.entries.Count == 0) return;

        try
        {
            string dirPath = Path.Combine(Application.persistentDataPath, logDirectory);
            Directory.CreateDirectory(dirPath); // Ensure directory exists

            // Sanitize GameObject name for filename
            string safeGameObjectName = string.Join("_", gameObject.name.Split(Path.GetInvalidFileNameChars()));

            string fileName = $"glitch_log_{safeGameObjectName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string filePath = Path.Combine(dirPath, fileName);

            // GlitchLogData now contains the window info, so just serialize it
            string jsonLog = JsonUtility.ToJson(glitchLog, true); // Use the wrapper class
            File.WriteAllText(filePath, jsonLog);

            Debug.Log($"Glitch log saved to: {filePath}");
            logWritten = true; // Mark as written for this session/enable cycle
        }
        catch (Exception e)
        {
            Debug.LogError($"[{gameObject.name}] Failed to write glitch log: {e.Message}\nStackTrace: {e.StackTrace}", this);
        }
    }

    void OnDisable()
    {
        if (isSetupComplete)
        {
            ResetMomentaryGlitches(); // Ensure any active temporary effect is reset
        }
        // Write log when disabled, if enabled and not already written
        WriteLogToFile();
    }

    void OnDestroy()
    {
        // Attempt to write log on destroy as well, in case OnDisable didn't fire or wasn't enough
        WriteLogToFile();
    }
}