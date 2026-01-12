using UnityEngine;

public class PauseComponent : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (GameManager.gameState == GameManager.GameState.Game)
            {
                GameManager.Instance.SetGameState(GameManager.GameState.Pause);
            }
            else if (GameManager.gameState == GameManager.GameState.Pause)
            {
                GameManager.Instance.SetGameState(GameManager.GameState.Game);
            }
        }
    }
}
