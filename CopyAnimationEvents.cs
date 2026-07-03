using System.Linq;
using UnityEditor;
using UnityEngine;

public class AnimationEventCopier : EditorWindow
{
    private AnimationClip sourceClip;
    private AnimationClip targetClip;
    private bool appendEvents = false;

    [MenuItem("Tools/Animation/Animation Event Copier")]
    public static void ShowWindow()
    {
        GetWindow<AnimationEventCopier>("Event Copier");
    }

    private void OnGUI()
    {
        GUILayout.Label("Copy Animation Events", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        sourceClip = (AnimationClip)EditorGUILayout.ObjectField("Source Clip", sourceClip, typeof(AnimationClip), false);
        targetClip = (AnimationClip)EditorGUILayout.ObjectField("Target Clip", targetClip, typeof(AnimationClip), false);
        appendEvents = EditorGUILayout.Toggle("Append (instead of overwrite)", appendEvents);

        EditorGUILayout.Space();

        if (GUILayout.Button("Copy Events") && sourceClip != null && targetClip != null)
        {
            CopyEvents();
        }
    }

    private void CopyEvents()
    {
        AnimationEvent[] sourceEvents = AnimationUtility.GetAnimationEvents(sourceClip);
        if (sourceEvents.Length == 0)
        {
            Debug.LogWarning($"'{sourceClip.name}' has no events to copy.");
            return;
        }

        float sourceLength = sourceClip.length;

        // Convert absolute seconds -> normalized [0,1] time, since
        // ModelImporterClipAnimation.events expects normalized time,
        // unlike AnimationUtility.GetAnimationEvents which returns seconds.
        AnimationEvent[] normalizedEvents = sourceEvents.Select(e =>
        {
            AnimationEvent copy = new AnimationEvent
            {
                time = Mathf.Clamp(e.time / sourceLength, 0f, 0.9999f), // avoid landing exactly on/past 1.0
                functionName = e.functionName,
                stringParameter = e.stringParameter,
                floatParameter = e.floatParameter,
                intParameter = e.intParameter,
                objectReferenceParameter = e.objectReferenceParameter,
                messageOptions = e.messageOptions
            };
            return copy;
        }).ToArray();

        AnimationEvent[] finalEvents = normalizedEvents;
        if (appendEvents)
        {
            AnimationEvent[] existing = AnimationUtility.GetAnimationEvents(targetClip); // already normalized, since it's read back the same way
            finalEvents = existing.Concat(normalizedEvents)
                                   .OrderBy(e => e.time)
                                   .ToArray();
        }

        string targetPath = AssetDatabase.GetAssetPath(targetClip);
        AssetImporter importer = AssetImporter.GetAtPath(targetPath);

        if (importer is ModelImporter modelImporter)
        {
            // Target is a clip living inside an FBX (e.g. your Animation Holder).
            // This is the persistent, "correct" way — same store the Inspector's
            // Animation tab Events UI reads/writes, and survives re-import.
            ModelImporterClipAnimation[] clips = modelImporter.clipAnimations.Length > 0
                ? modelImporter.clipAnimations
                : modelImporter.defaultClipAnimations; // fall back if no custom split exists yet

            int index = System.Array.FindIndex(clips, c => c.name == targetClip.name);
            if (index < 0)
            {
                Debug.LogError($"Could not find a matching clip entry named '{targetClip.name}' in {targetPath}'s importer settings.");
                return;
            }

            clips[index].events = finalEvents;
            modelImporter.clipAnimations = clips; // assigning promotes defaultClipAnimations to real overrides if needed
            EditorUtility.SetDirty(modelImporter);
            modelImporter.SaveAndReimport();

            Debug.Log($"Copied {sourceEvents.Length} events into '{targetClip.name}' via ModelImporter (FBX-safe).");
        }
        else
        {
            // Target is a loose .anim asset — direct AnimationUtility write is fine here.
            AnimationUtility.SetAnimationEvents(targetClip, finalEvents);
            EditorUtility.SetDirty(targetClip);
            AssetDatabase.SaveAssets();

            Debug.Log($"Copied {sourceEvents.Length} events into '{targetClip.name}' (loose .anim).");
        }
    }
}