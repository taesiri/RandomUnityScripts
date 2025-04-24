using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Required for LINQ operations like Any()

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
    // Removed global duration - now per-type

    [Header("Momentary: Target Selection")]
    [Tooltip("Chance (0 to 1) that ANY individual glitchable object will be affected during a momentary Blink or Individual Jump glitch event.")]
    [Range(0f, 1f)]
    [SerializeField] private float individualGlitchTargetChance = 0.5f;

    // --- Configuration for each Momentary Glitch Type ---

    [Header("Momentary: Blink Glitch")]
    [Tooltip("Enable momentary random blinking during glitch events.")]
    [SerializeField] private bool enableBlinkGlitch = true;
    [Min(0.01f)] public float minBlinkDuration = 0.1f;
    [Min(0.01f)] public float maxBlinkDuration = 0.5f;
    [Tooltip("During a blink glitch, the chance (0 to 1) for an affected Renderer to be VISIBLE each frame.")]
    [Range(0f, 1f)]
    [SerializeField] private float blinkVisibilityChanceDuringGlitch = 0.3f;

    [Header("Momentary: Individual Transform Jump Glitch")]
    [Tooltip("Enable momentary random position/rotation jumps for individual objects during glitch events.")]
    [SerializeField] private bool enableIndividualTransformJumpGlitch = true;
    [Min(0.01f)] public float minIndividualJumpDuration = 0.1f;
    [Min(0.01f)] public float maxIndividualJumpDuration = 0.3f;
    [Tooltip("Maximum distance an affected object can jump from its ORIGINAL local position during a glitch.")]
    [SerializeField] private float individualJumpPositionIntensity = 0.5f;
    [Tooltip("Maximum angle (degrees) an affected object can randomly rotate from its ORIGINAL local rotation during a glitch.")]
    [SerializeField] private float individualJumpRotationIntensity = 15.0f;

    [Header("Momentary: Whole Object Jump Glitch")]
    [Tooltip("Enable momentary random position/rotation jumps for the ENTIRE targetRoot during glitch events.")]
    [SerializeField] private bool enableWholeObjectJumpGlitch = true;
    [Min(0.01f)] public float minWholeObjectJumpDuration = 0.1f;
    [Min(0.01f)] public float maxWholeObjectJumpDuration = 0.4f;
    [Tooltip("Maximum distance the entire targetRoot can jump from its ORIGINAL local position during a glitch.")]
    [SerializeField] private float wholeObjectJumpPositionIntensity = 0.2f;
    [Tooltip("Maximum angle (degrees) the entire targetRoot can randomly rotate from its ORIGINAL local rotation during a glitch.")]
    [SerializeField] private float wholeObjectJumpRotationIntensity = 5.0f;

    [Header("Momentary: Texture Glitch")]
    [Tooltip("Enable momentarily altering textures on materials during glitch events.")]
    [SerializeField] private bool enableTextureGlitch = true;
    [Min(0.01f)] public float minTextureGlitchDuration = 0.2f;
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

    // --- Current Momentary Glitch Tracking ---
    private System.Type activeMomentaryGlitchType = null; // Track which type is active

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
        if (!isSetupComplete)
        {
            InitializeGlitchTargets();
        }

        if (isSetupComplete)
        {
            ResetMomentaryGlitches(); // Reset any lingering state from before disable
            ScheduleNextGlitch();
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

        isSetupComplete = true;
        Debug.Log($"[{gameObject.name}] AdvancedHierarchicalGlitch Initialized. Found {glitchableRenderers.Count} active renderers in '{targetRoot.name}'. Ready to glitch {availableMomentaryGlitches.Count} momentary types.");
    }

    void ScheduleNextGlitch()
    {
        timeUntilNextGlitch = Random.Range(minTimeBetweenGlitches, maxTimeBetweenGlitches);
        isMomentaryGlitchActive = false;
        activeMomentaryGlitchType = null; // Ensure no glitch type is marked as active
    }

    void Update()
    {
        if (!isSetupComplete || glitchableRenderers.Count == 0) return;

        if (isMomentaryGlitchActive)
        {
            ApplyMomentaryGlitchesPerFrame();

            if (Time.time >= currentGlitchEndTime)
            {
                ResetMomentaryGlitches();
                ScheduleNextGlitch();
            }
        }
        else
        {
            timeUntilNextGlitch -= Time.deltaTime;
            if (timeUntilNextGlitch <= 0)
            {
                TriggerGlitchEvent();
            }
        }
    }

    void TriggerGlitchEvent()
    {
        // --- Decide: Permanent or Momentary? ---
        if (enablePermanentDisappearance && Random.value < permanentDisappearanceChance)
        {
            ApplyPermanentDisappearance();
            if (isSetupComplete) ScheduleNextGlitch();
        }
        // --- Decide: Momentary Glitch ---
        else if (availableMomentaryGlitches.Count > 0)
        {
            isMomentaryGlitchActive = true;

            // --- Reset momentary tracking ---
            renderersInCurrentBlink.Clear();
            transformsInCurrentIndividualJump.Clear();
            activeMomentaryGlitchType = null;

            // --- Choose ONE momentary glitch type for this event ---
            int randomIndex = Random.Range(0, availableMomentaryGlitches.Count);
            activeMomentaryGlitchType = availableMomentaryGlitches[randomIndex];

            // --- Activate the chosen glitch, set its duration, and select targets ---
            float glitchDuration = 0.1f; // Default safety value

            if (activeMomentaryGlitchType == typeof(BlinkGlitchState))
            {
                glitchDuration = Random.Range(minBlinkDuration, maxBlinkDuration);
                foreach (Renderer rend in glitchableRenderers)
                {
                    if (rend != null && Random.value < individualGlitchTargetChance)
                    {
                        renderersInCurrentBlink.Add(rend);
                    }
                }
            }
            else if (activeMomentaryGlitchType == typeof(IndividualJumpGlitchState))
            {
                 glitchDuration = Random.Range(minIndividualJumpDuration, maxIndividualJumpDuration);
                HashSet<Transform> potentialJumpTransforms = new HashSet<Transform>(glitchableRenderers.Where(r => r != null).Select(r => r.transform));
                foreach (Transform t in potentialJumpTransforms)
                {
                    if (t != null && initialLocalPositions.ContainsKey(t) && Random.value < individualGlitchTargetChance)
                    {
                        transformsInCurrentIndividualJump.Add(t);
                    }
                }
            }
            else if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState))
            {
                 glitchDuration = Random.Range(minWholeObjectJumpDuration, maxWholeObjectJumpDuration);
                // No specific targets needed, affects root
            }
            else if (activeMomentaryGlitchType == typeof(TextureGlitchState))
            {
                 glitchDuration = Random.Range(minTextureGlitchDuration, maxTextureGlitchDuration);
                ApplyTextureGlitchStart(); // Apply texture change ONCE at the start
            }

             currentGlitchEndTime = Time.time + glitchDuration; // Set end time based on chosen type
             ApplyMomentaryGlitchesPerFrame(); // Apply the first frame effect immediately
        }
        else
        {
             ScheduleNextGlitch(); // No momentary glitches enabled
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
                if (rend != null) rend.enabled = (Random.value <= blinkVisibilityChanceDuringGlitch);
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
                    t.localPosition = initialPos + Random.insideUnitSphere * individualJumpPositionIntensity;
                    t.localRotation = initialRot * Quaternion.Euler(
                        Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity),
                        Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity),
                        Random.Range(-individualJumpRotationIntensity, individualJumpRotationIntensity)
                    );
                }
            }
        }

        // --- Apply Whole Object Jump (Once at start) ---
        if (activeMomentaryGlitchType == typeof(WholeObjectJumpGlitchState) && targetRoot != null)
        {
            targetRoot.localPosition = initialRootLocalPosition + Random.insideUnitSphere * wholeObjectJumpPositionIntensity;
            targetRoot.localRotation = initialRootLocalRotation * Quaternion.Euler(
                Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity),
                Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity),
                Random.Range(-wholeObjectJumpRotationIntensity, wholeObjectJumpRotationIntensity)
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
    }


    void ApplyPermanentDisappearance()
    {
        if (!isSetupComplete) return;

        List<Renderer> renderersToRemove = new List<Renderer>();
        List<Transform> transformsToCheckForRemoval = new List<Transform>();

        foreach (Renderer rend in glitchableRenderers)
        {
            if (rend != null && Random.value < disappearanceTargetChance)
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


    void OnDisable()
    {
        if (isSetupComplete)
        {
            ResetMomentaryGlitches(); // Ensure any active temporary effect is reset
        }
    }
}