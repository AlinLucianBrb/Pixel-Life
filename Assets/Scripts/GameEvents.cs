using System;

public static class GameEvents
{
    public static event Action OnGameWon;
    public static event Action<GameManager.GameState> OnGameStateChange;
    public static event Action OnGameStart;

    public static void GameWon() => OnGameWon?.Invoke();
    public static void GameStateChanged(GameManager.GameState gameState) => OnGameStateChange?.Invoke(gameState);
    public static void GameStart() => OnGameStart?.Invoke();
}
