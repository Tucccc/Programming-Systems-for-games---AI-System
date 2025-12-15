public class StateMachine
{
    public IState CurrentState { get; private set; }

    public void ChangeState(IState newState)
    {
        if (newState == null)
        {
            UnityEngine.Debug.LogError("StateMachine: newState is null");
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
