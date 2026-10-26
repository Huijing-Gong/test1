using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleTrigger : MonoBehaviour
{
    private bool battleStarted = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !battleStarted)
        {
            battleStarted = true;

            Debug.Log("Battle started!");

            SceneManager.LoadScene("BattleScene");
        }
    }
}