using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valebound.Core
{
    /// <summary>
    /// Boot scene entry: warms up, then loads Main scene additively (world is
    /// baked inside Main). Shows the loading screen from the UI canvas.
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private float minBootSeconds = 0.8f;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            var op = SceneManager.LoadSceneAsync(mainSceneName, LoadSceneMode.Additive);
            op.allowSceneActivation = false;
            float t = 0f;
            while (op.progress < 0.9f || t < minBootSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            op.allowSceneActivation = true;
            yield return op;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(mainSceneName));
        }
    }
}
