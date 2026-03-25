using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    [Header("Scene Names")]
    public string arSceneName = "ARScene"; // change to your actual AR scene name

    // Called by button
    public void GoToAR()
    {
        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("No model path set. Cannot go to AR.");
            return;
        }

        Debug.Log("Switching to AR scene with model: " + path);
        SceneManager.LoadScene(arSceneName);
    }
}
