using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Reusable icon picker dialog.
//
// Shows all available icons (PlayerIcons.AvailableIcons) in a grid
// (GridLayoutGroup), invokes a callback with the chosen icon index, then closes.
//
// Inspector:
//   panel         — root GameObject of the dialog (enabled/disabled).
//   gridContainer — Transform with GridLayoutGroup where cells are spawned.
//   cellPrefab    — cell prefab: UI Button with a child Image (see setup notes).
//   closeButton   — (optional) close/cancel button.
//
// Usage from code:
//   iconPicker.Open(chosenIndex => { PlayerIcons.SetPlayer1Icon(chosenIndex); ... });
public class IconPickerDialog : MonoBehaviour
{
    public GameObject panel;
    public Transform gridContainer;
    public Button cellPrefab;
    public Button closeButton;

    Action<int> onPicked;
    readonly List<GameObject> spawnedCells = new List<GameObject>();

    void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    // Opens the dialog. callback receives the index in PlayerIcons.AvailableIcons.
    public void Open(Action<int> callback)
    {
        onPicked = callback;
        PlayerIcons.EnsureLoaded();
        BuildGrid();

        if (panel != null)
        {
            panel.SetActive(true);
            panel.transform.SetAsLastSibling(); // on top of other UI
        }
    }

    public void Close()
    {
        onPicked = null;
        if (panel != null) panel.SetActive(false);
    }

    void BuildGrid()
    {
        // Clear cells from the previous open.
        foreach (var c in spawnedCells)
            if (c != null) Destroy(c);
        spawnedCells.Clear();

        if (cellPrefab == null || gridContainer == null)
        {
            Debug.LogError("IconPickerDialog: cellPrefab or gridContainer is not assigned in the inspector.");
            return;
        }

        Sprite[] icons = PlayerIcons.AvailableIcons;

        // If there are no icons at all — show one cell with the fallback circle.
        if (icons == null || icons.Length == 0)
        {
            SpawnCell(PlayerIcons.GetFallbackSprite(), 0);
            return;
        }

        for (int i = 0; i < icons.Length; i++)
            SpawnCell(icons[i], i);
    }

    void SpawnCell(Sprite sprite, int index)
    {
        Button cell = Instantiate(cellPrefab, gridContainer);
        cell.gameObject.SetActive(true);

        // Draw on a child Image if present; otherwise on the button itself.
        Image target = null;
        foreach (var img in cell.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject != cell.gameObject) { target = img; break; }
        }
        if (target == null) target = cell.GetComponent<Image>();
        if (target != null)
        {
            target.sprite = sprite;
            target.color = Color.white;
            target.preserveAspect = true;
        }

        int captured = index; // closure by value
        cell.onClick.AddListener(() =>
        {
            var cb = onPicked;
            Close();
            cb?.Invoke(captured);
        });

        spawnedCells.Add(cell.gameObject);
    }
}
