using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ARUIManager : MonoBehaviour
{
    public Button backButton;
    public Button spawnButton;

    private ModelPlacement modelPlacement;

    void Start()
    {
        modelPlacement = FindFirstObjectByType<ModelPlacement>();

        if (backButton) backButton.onClick.AddListener(() =>
        {
            SceneManager.LoadScene("PreviewScene_new");
        });

        if (spawnButton) spawnButton.onClick.AddListener(() =>
        {
            if (modelPlacement != null)
                modelPlacement.OnSpawnButtonPressed();
            else
                Debug.LogError("[ARUIManager] ModelPlacement not found in scene.");
        });
    }
}
