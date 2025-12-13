using UnityEngine;

public interface IState
{
    void Enter();
    void Tick(float deltaTime);

    void Exit();
}
