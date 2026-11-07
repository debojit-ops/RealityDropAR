using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Reports pointer down/up on a UI element (e.g. for press animations) without blocking its click.</summary>
public class UIPressRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public System.Action<bool> onChange;

    public void OnPointerDown(PointerEventData eventData) => onChange?.Invoke(true);
    public void OnPointerUp(PointerEventData eventData) => onChange?.Invoke(false);
    public void OnPointerExit(PointerEventData eventData) => onChange?.Invoke(false);
}
