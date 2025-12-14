using UnityEngine;

public class PatrolState : IState
{
    private readonly EnemyAI enemy;
    private int index;

    public PatrolState(EnemyAI enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        enemy.currentStateName = "Patrol";
        Debug.Log("Entered Patrol");

        // Start at the first waypoint
        index = 0;
    }

    public void Tick(float deltaTime)
    {
        if (enemy.CanSeePlayer())
        {
            enemy.GoToChase();
            return;
        }

        var points = enemy.patrolPoints;
        if (points == null || points.Length == 0)
            return;

        Transform current = points[index];
        if (current == null)
            return;

        enemy.MoveTowards(current.position, deltaTime);

        if (enemy.IsAtPosition(current.position))
        {
            index = (index + 1) % points.Length;
        }
    }

    public void Exit()
    {
        Debug.Log("Exited Patrol");
    }
}
