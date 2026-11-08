using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Subtle vibration on every UI button tap, in every scene, with no per-button wiring.
/// Remembers the button under the finger on touch down and ticks on release, only if the finger
/// did not move (so scrolling the feed or swiping cards never buzzes).
/// Uses Android's own keyboard-tap haptic: very light, and it follows the phone's touch feedback setting.
/// </summary>
public class UIHaptics : MonoBehaviour
{
    const float MoveLimit = 20f;   // px: more than this counts as a drag, not a tap

    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private GameObject pressed;
    private Vector2 downPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        var go = new GameObject("UIHaptics");
        DontDestroyOnLoad(go);
        go.AddComponent<UIHaptics>();
    }

    void Update()
    {
        if (Input.touchCount == 0) return;
        Touch t = Input.GetTouch(0);
        switch (t.phase)
        {
            case TouchPhase.Began:
                pressed = ButtonAt(t.position);
                downPosition = t.position;
                break;
            case TouchPhase.Moved:
                if (Vector2.Distance(t.position, downPosition) > MoveLimit) pressed = null;
                break;
            case TouchPhase.Ended:
                if (pressed != null) Tick();
                pressed = null;
                break;
            case TouchPhase.Canceled:
                pressed = null;
                break;
        }
    }

    private GameObject ButtonAt(Vector2 screenPosition)
    {
        var es = EventSystem.current;
        if (es == null) return null;
        hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = screenPosition }, hits);
        if (hits.Count == 0) return null;
        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        if (handler == null) return null;
        var selectable = handler.GetComponent<Selectable>();
        return selectable != null && selectable.IsInteractable() ? handler : null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    const int KeyboardTap = 3; // android.view.HapticFeedbackConstants.KEYBOARD_TAP
    private static AndroidJavaObject activity;

    private static void Tick()
    {
        try
        {
            if (activity == null)
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    activity = player.GetStatic<AndroidJavaObject>("currentActivity");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                    decor.Call<bool>("performHapticFeedback", KeyboardTap);
            }));
        }
        catch (System.Exception e) { Debug.LogWarning("[UIHaptics] " + e.Message); }
    }
#else
    private static void Tick() { }
#endif
}
