using UnityEngine;

public class DisplayComponent : MonoBehaviour
{
    public GameManager.GameState[] preferableGameState;

    protected virtual void Awake()
    {
        GameEvents.OnGameStateChange += SetChildrenActive;
    }

    protected virtual void OnDestroy()
    {
        GameEvents.OnGameStateChange -= SetChildrenActive;
    }

    void Start()
    {
        SetChildrenActive(GameManager.gameState);
    }

    protected virtual void SetChildrenActive(GameManager.GameState gameState)
    {
        bool activeState = false;
        foreach(GameManager.GameState state in preferableGameState) 
        { 
            if (state == gameState) 
            {
                activeState = true;
            }
        }
        
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(activeState);
        }
    }
}
