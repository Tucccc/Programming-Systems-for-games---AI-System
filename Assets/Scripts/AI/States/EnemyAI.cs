using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float turnSpeed = 360f;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public float waypointReachDistance = 0.6f;

    [Header("Debug")]
    public string currentStateName;

    public StateMachine fsm { get; private set; }

    // States
    private IdleState idleState;
    private PatrolState patrolState;

    private void Awake()
    {
        fsm = new StateMachine();

        idleState = new IdleState(this);
        patrolState = new PatrolState(this);
    }

    private void Start()
    {
        if (player == null)
            Debug.LogWarning("EnemyAI: Player reference not set. Drag Player into EnemyAI inspector.");

        if (patrolPoints == null || patrolPoints.Length == 0)
            Debug.LogWarning("EnemyAI: No patrol points set. Add some to patrolPoints in Inspector.");

        fsm.ChangeState(idleState);
    }

    private void Update()
    {
        fsm.Tick(Time.deltaTime);
    }

    public void MoveTowards(Vector3 targetPosition, float deltaTime)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * deltaTime);

        transform.position += transform.forward * (moveSpeed * deltaTime);
    }

    public bool IsAtPosition(Vector3 targetPosition)
    {
        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = targetPosition; b.y = 0f;
        return Vector3.Distance(a, b) <= waypointReachDistance;
    }

    public void GoToIdle() => fsm.ChangeState(idleState);
    public void GoToPatrol() => fsm.ChangeState(patrolState);
}
