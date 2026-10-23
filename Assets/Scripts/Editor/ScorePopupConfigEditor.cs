using UnityEditor;
using UnityEngine;

// Inspector for ScorePopupConfig: the regular fields plus a "Play test popup" button that
// spawns a "+N" over the middle of the board, so position/animation tweaks can be previewed
// in Play Mode without closing a shape. Editor-only (lives in an Editor folder).
[CustomEditor(typeof(ScorePopupConfig))]
public class ScorePopupConfigEditor : Editor
{
    private int previewPoints = 2;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode in the Game scene to preview. Changes made in Play Mode are " +
                "kept — this is an asset, not a scene object.",
                MessageType.Info);
            return;
        }

        previewPoints = EditorGUILayout.IntSlider("Test points", previewPoints, 1, 99);

        ScorePopupUI ui = Object.FindFirstObjectByType<ScorePopupUI>();
        using (new EditorGUI.DisabledScope(ui == null))
        {
            if (GUILayout.Button("Play test popup", GUILayout.Height(28)))
                ui.ShowPreview(previewPoints);
        }

        if (ui == null)
            EditorGUILayout.HelpBox("No ScorePopupUI in the open scene — open the Game scene.",
                                    MessageType.Warning);
        else if (ui.Config != target)
            EditorGUILayout.HelpBox("The ScorePopupUI in this scene uses a different config " +
                                    $"('{ui.Config.name}'), so the preview won't reflect these values.",
                                    MessageType.Warning);
    }
}
