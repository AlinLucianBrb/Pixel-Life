using UnityEngine;

public class SetterWrapper : MonoBehaviour
{
    public void SetVolume(float volume)
    {
        GameManager.Instance.SetSFXVolume(volume);
        GameManager.Instance.SetBGMVolume(volume);
    }

    public void SetResolution() => GameManager.Instance.SetResolution();
    public void SetFullScreen() => GameManager.Instance.SetFullScreen();
    public void PlaySFX(string sfxName = "UI") => GameManager.Instance.PlaySFX(sfxName);
    public void StartGame() => GameManager.Instance.NewGame();
    public void LoadScene(int index) => GameManager.Instance.LoadScene(index);
    public void QuitGame() => GameManager.Instance.QuitGame();
    public void SetGameStateToGame() => GameManager.Instance.SetGameState(GameManager.GameState.Game);
    public void SetGameStateToPause() => GameManager.Instance.SetGameState(GameManager.GameState.Pause);
    public void SetGameStateToMainMenu() => GameManager.Instance.SetGameState(GameManager.GameState.MainMenu);
}
