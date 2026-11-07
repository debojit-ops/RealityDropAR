using UnityEngine;

/// <summary>Spring "press" scale for any UI element (shrinks to 95% while held).</summary>
public class UIPress : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler,
                       UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    private bool down;
    private float amount;

    public static void Add(RectTransform rt)
    {
        if (rt.GetComponent<UIPress>() == null) rt.gameObject.AddComponent<UIPress>();
    }

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => down = true;
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => down = false;
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => down = false;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        amount = Mathf.MoveTowards(amount, down ? 1f : 0f, dt * (down ? 12f : 7f));
        float s = amount * amount * (3f - 2f * amount);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.95f, s);
    }
}
