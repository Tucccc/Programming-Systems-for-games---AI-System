using UnityEngine;

public class ChaseState : IState
{
    private readonly EnemyAI enemy;
    private float lostTimer;

    public ChaseState(EnemyAI enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        enemy.currentStateName = "Chase";
        Debug.Log("Entered Chase");
        lostTimer = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (enemy.player == null)
        {
            enemy.GoToPatrol();
            return;
        }

        // If we can attack, do that first
        if (enemy.IsPlayerInAttackRange())
        {
            enemy.GoToAttack();
            return;
        }

        // Decide target:
        // - If player is visible, chase live position
        // - If not visible, chase last seen position (if available)
        bool canSee = enemy.debugCanSeePlayer;

        Vector3 targetPos;
        if (canSee)
        {
            targetPos = enemy.player.position;
        }
        else if (enemy.hasLastSeenPos)
        {
            targetPos = enemy.lastSeenPlayerPos;
        }
        else
        {
            // No info about where player is, give up
            enemy.GoToPatrol();
            return;
        }

        enemy.MoveTowards(targetPos, deltaTime);

        // Lost sight timer: only counts when we CAN'T see the player
        if (!canSee)
        {
            lostTimer += deltaTime;

            if (lostTimer >= enemy.lostTimeBeforeReturn)
            {
                // Go investigate/search instead of magically chasing through walls
                enemy.GoToInvestigate();
            }
        }
        else
        {
            lostTimer = 0f;
        }
    }

    public void Exit()
    {
        Debug.Log("Exited Chase");
    }
}
