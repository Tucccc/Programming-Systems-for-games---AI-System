using UnityEngine;

public class StateMachine : MonoBehaviour
{
    public IState CurrentState { get; private set; }

    public void ChangeState(IState newState)
    {
        if(newState == null)
        {
            Debug.LogError("StateMachine is null");
            return;
        }

        CurrentState?.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    public void Tick(float deltaTime)
    {
        CurrentState?.Tick(deltaTime);
    }

}