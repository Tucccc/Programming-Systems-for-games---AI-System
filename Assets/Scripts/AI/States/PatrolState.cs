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
        // If player is seen, immediately switch to chase
        if (enemy.debugCanSeePlayer)   // uses the cached vision from EnemyAI.FixedUpdate
        {
            enemy.GoToChase();
            return;
        }
        
        index = index % enemy.patrolPoints.Length;

        if (enemy.patrolPoints.Length == 0)
            return;

        Transform current = enemy.patrolPoints[index];

        Vector3 finalTarget = current.position;

        Vector3 moveTarget = enemy.GetMoveTargetWithDetour(finalTarget, deltaTime);
        enemy.MoveTowards(moveTarget, deltaTime);

        if (enemy.IsAtPosition(finalTarget))
        {
            index = (index + 1) % enemy.patrolPoints.Length;
        }


    }


    public void Exit()
    {
        Debug.Log("Exited Patrol");
    }
}
