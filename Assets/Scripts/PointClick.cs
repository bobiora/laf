using UnityEngine;
using UnityEngine.InputSystem;

public class PointClick : MonoBehaviour
{
    public int gridX;
    public int gridY;
    public bool isSelected = false;

    private SpriteRenderer sr;
    private Color defaultColor = Color.white;
    private Color selectedColor = Color.yellow;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        sr.color = defaultColor;
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            CheckClick(Mouse.current.position.ReadValue());
        }

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            CheckClick(Touchscreen.current.primaryTouch.position.ReadValue());
        }
    }

    void CheckClick(Vector2 screenPos)
    {
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(screenPos);
        Collider2D hit = Physics2D.OverlapPoint(worldPos);

        if (hit != null && hit.gameObject == this.gameObject)
        {
            // Сообщаем менеджеру о клике — он решит, что делать
            GameManager.Instance.OnPointClicked(this);
        }
    }

    public void SetSelected(bool selected, Color color)
    {
        isSelected = selected;
        sr.color = isSelected ? color : Color.white;
    }
}