using UnityEngine;

public class AttackState : IState
{
    private readonly EnemyAI enemy;
    private float cooldownTimer;

    public AttackState(EnemyAI enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        enemy.currentStateName = "Attack";
        Debug.Log("Entered Attack");
        cooldownTimer = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (enemy.player == null)
        {
            enemy.GoToPatrol();
            return;
        }

        // Always face the player while attacking
        enemy.FaceTowards(enemy.player.position, deltaTime);

        // If player moved away, go back to chase
        if (!enemy.IsPlayerInAttackRange())
        {
            enemy.GoToChase();
            return;
        }

        // Attack "event" every cooldown
        cooldownTimer -= deltaTime;
        if (cooldownTimer <= 0f)
        {
            cooldownTimer = enemy.attackCooldown;
            Debug.Log("ATTACK! (placeholder)");
            // Later you can trigger animation / reduce HP etc.
        }
    }

    public void Exit()
    {
        Debug.Log("Exited Attack");
    }
}
