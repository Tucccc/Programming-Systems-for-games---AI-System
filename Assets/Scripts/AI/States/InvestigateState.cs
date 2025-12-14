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
        // If we see player again, resume chase immediately
        if (enemy.CanSeePlayer())
        {
            enemy.GoToChase();
            return;
        }

        // If we don't even have a last seen position, give up
        if (!enemy.hasLastSeenPos)
        {
            enemy.GoToPatrol();
            return;
        }

        // Move to last seen position
        enemy.MoveTowards(enemy.lastSeenPlayerPos, deltaTime);

        // Once reached, "search" for a short time then give up
        if (enemy.IsAtPosition(enemy.lastSeenPlayerPos))
        {
            enemy.FaceTowards(enemy.lastSeenPlayerPos + enemy.transform.forward, deltaTime);

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
