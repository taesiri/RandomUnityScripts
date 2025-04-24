using UnityEngine;
using System.Collections.Generic; // Required for Lists and Dictionaries

public class HierarchicalContinuousGlitch : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The top-level Transform whose children (and itself) with Renderers will be glitched.")]
    [SerializeField] private Transform targetRoot;

    [Header("Position Glitch (Stutter)")]
    [Tooltip("Enable continuous position stuttering for all found Renderers.")]
    [SerializeField] private bool enablePositionGlitch = true;
    [Tooltip("Maximum distance each glitched object can randomly move from its ORIGINAL local position EACH FRAME.")]
    [SerializeField] private float positionIntensity = 0.05f; // Often needs to be lower for multiple objects

    [Header("Rotation Glitch")]
    [Tooltip("Enable continuous random rotation for all found Renderers.")]
    [SerializeField] private bool enableRotationGlitch = true;
    [Tooltip("Maximum angle (degrees) each glitched object can randomly rotate from its ORIGINAL local rotation EACH FRAME.")]
    [SerializeField] private float rotationIntensity = 2.0f; // Often needs to be lower for multiple objects

    [Header("Blink Glitch")]
    [Tooltip("Enable continuous random blinking for all found Renderers.")]
    [SerializeField] private bool enableBlinkGlitch = true;
    [Tooltip("Chance (0 to 1) for each Renderer of being VISIBLE during a frame when blinking is active.")]
    [Range(0f, 1f)]
    [SerializeField] private float blinkVisibilityChance = 0.75f; // Higher default, looks less jarring with many objects

    // --- Private Variables ---

    // Store initial states for EACH transform/renderer
    private Dictionary<Transform, Vector3> initialLocalPositions = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Quaternion> initialLocalRotations = new Dictionary<Transform, Quaternion>();
    private Dictionary<Renderer, bool> initialRendererStates = new Dictionary<Renderer, bool>();

    // Lists to iterate over quickly in Update
    private List<Transform> glitchableTransforms = new List<Transform>();
    private List<Renderer> targetRenderers = new List<Renderer>();

    private bool isSetupComplete = false;

    void Awake()
    {
        InitializeGlitchTargets();
    }

    void OnEnable()
    {
        // If re-enabled, re-capture initial state in case things moved while disabled
        // and ensure renderers are reset correctly if blink is off
         if (isSetupComplete)
         {
            // Re-run setup to capture potentially changed states if objects moved while disabled
            // This assumes the hierarchy structure *didn't* change drastically
            // A more robust solution might diff the found renderers, but this is simpler
            InitializeGlitchTargets();

            // Ensure correct visibility if blinking is OFF
             if (!enableBlinkGlitch)
             {
                 ResetRendererVisibility();
             }
         }
         else
         {
             // Initial setup if Awake hasn't run or failed
              InitializeGlitchTargets();
         }
    }

    void InitializeGlitchTargets()
    {
        if (targetRoot == null)
        {
            Debug.LogError($"[{gameObject.name}] HierarchicalContinuousGlitch ERROR: 'Target Root' MUST be assigned!", this);
            enabled = false;
            return;
        }

        // Clear previous data before re-populating
        initialLocalPositions.Clear();
        initialLocalRotations.Clear();
        initialRendererStates.Clear();
        glitchableTransforms.Clear();
        targetRenderers.Clear();

        // Find ALL Renderers in the hierarchy, including inactive ones initially
        // so we capture their intended initial 'enabled' state correctly.
        Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(true); // Include inactive

        if (renderers.Length == 0)
        {
             Debug.LogWarning($"[{gameObject.name}] HierarchicalContinuousGlitch: No Renderers found within the hierarchy of '{targetRoot.name}'. Glitch effect will do nothing.", this);
             isSetupComplete = false; // Mark as not set up properly
             return; // No need to proceed
        }

        // Populate dictionaries and lists
        foreach (Renderer rend in renderers)
        {
            Transform t = rend.transform;

            // Store transform states only once per transform, even if multiple renderers exist
            if (!initialLocalPositions.ContainsKey(t))
            {
                initialLocalPositions.Add(t, t.localPosition);
                initialLocalRotations.Add(t, t.localRotation);
                glitchableTransforms.Add(t); // Add to list for position/rotation glitching
            }

            // Store renderer state and add to renderer list for blinking
             if (!initialRendererStates.ContainsKey(rend)) // Should always be true, but good practice
             {
                 initialRendererStates.Add(rend, rend.enabled);
                 targetRenderers.Add(rend); // Add to list for blinking
             }
        }

        isSetupComplete = true;
        Debug.Log($"[{gameObject.name}] HierarchicalContinuousGlitch Initialized. Found {targetRenderers.Count} renderers and {glitchableTransforms.Count} unique transforms to glitch in '{targetRoot.name}' hierarchy.");
    }


    void Update()
    {
        if (!isSetupComplete) return; // Don't run if setup failed or wasn't needed

        // --- Apply Position and Rotation Glitches ---
        foreach (Transform t in glitchableTransforms)
        {
            if (t == null) continue; // Skip if object was destroyed

            Vector3 initialPos = initialLocalPositions[t];
            Quaternion initialRot = initialLocalRotations[t];

            // Position
            if (enablePositionGlitch)
            {
                Vector3 randomPositionOffset = Random.insideUnitSphere * positionIntensity;
                t.localPosition = initialPos + randomPositionOffset;
            }
            else
            {
                if (t.localPosition != initialPos) t.localPosition = initialPos; // Reset if needed
            }

            // Rotation
            if (enableRotationGlitch)
            {
                Quaternion randomRotationOffset = Quaternion.Euler(
                    Random.Range(-rotationIntensity, rotationIntensity),
                    Random.Range(-rotationIntensity, rotationIntensity),
                    Random.Range(-rotationIntensity, rotationIntensity)
                );
                t.localRotation = initialRot * randomRotationOffset;
            }
            else
            {
                 if (t.localRotation != initialRot) t.localRotation = initialRot; // Reset if needed
            }
        }

        // --- Apply Blink Glitch ---
        foreach (Renderer rend in targetRenderers)
        {
            if (rend == null) continue; // Skip if object was destroyed

            if (enableBlinkGlitch)
            {
                // Decide visibility based on chance
                rend.enabled = (Random.value <= blinkVisibilityChance);
            }
            else
            {
                // Ensure renderer is set back to its initial state if blinking is off
                bool initialState = initialRendererStates[rend];
                if(rend.enabled != initialState) rend.enabled = initialState;
            }
        }
    }

    void OnDisable()
    {
        if (isSetupComplete)
        {
            // Reset Transforms
            foreach (KeyValuePair<Transform, Vector3> pair in initialLocalPositions)
            {
                if (pair.Key != null) // Check if transform still exists
                {
                    pair.Key.localPosition = pair.Value;
                }
            }
            foreach (KeyValuePair<Transform, Quaternion> pair in initialLocalRotations)
            {
                 if (pair.Key != null) // Check if transform still exists
                 {
                    pair.Key.localRotation = pair.Value;
                 }
            }

            // Reset Renderers
            ResetRendererVisibility();

            Debug.Log($"[{gameObject.name}] HierarchicalContinuousGlitch DISABLED. Resetting {glitchableTransforms.Count} transforms and {targetRenderers.Count} renderers in '{targetRoot?.name ?? "Unknown Root"}' hierarchy.");
        }
    }

    // Helper to reset renderer visibility based on stored initial states
    void ResetRendererVisibility()
    {
        foreach (KeyValuePair<Renderer, bool> pair in initialRendererStates)
        {
            if (pair.Key != null) // Check if renderer still exists
            {
                pair.Key.enabled = pair.Value;
            }
        }
    }
}