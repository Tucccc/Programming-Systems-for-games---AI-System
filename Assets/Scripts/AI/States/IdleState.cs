using UnityEngine;

public class IdleState : IState
{
    private readonly EnemyAI enemy;
    private float timer;
    private const float IdleTime = 2f;

    public IdleState(EnemyAI enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        timer = 0f;
        enemy.currentStateName = "Idle";
        Debug.Log("Entered Idle");
    }

    public void Tick(float deltaTime)
    {
        timer += deltaTime;

        if (timer >= IdleTime)
        {
            // Switch to Patrol after waiting
            enemy.GoToPatrol();
        }
    }

    public void Exit()
    {
        Debug.Log("Exited Idle");
    }
}
