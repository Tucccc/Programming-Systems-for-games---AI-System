using UnityEngine;

public class InvestigateState : IState
{
    private readonly EnemyAI enemy;
    private float searchTimer;

    public InvestigateState(EnemyAI enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        enemy.currentStateName = "Investigate";
        Debug.Log("Entered Investigate");
        searchTimer = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (enemy.debugCanSeePlayer)
        {
            enemy.GoToChase();
            return;
        }

        if (!enemy.hasLastSeenPos)
        {
            enemy.GoToPatrol();
            return;
        }

        Vector3 finalTarget = enemy.lastSeenPlayerPos;

        Vector3 moveTarget = enemy.GetMoveTargetWithDetour(finalTarget, deltaTime);
        enemy.MoveTowards(moveTarget, deltaTime);

        if (enemy.IsAtPosition(finalTarget))
        {
            searchTimer += deltaTime;
            if (searchTimer >= enemy.investigateSearchTime)
            {
                enemy.GoToPatrol();
            }
        }
    }


    public void Exit()
    {
        Debug.Log("Exited Investigate");
    }
}
