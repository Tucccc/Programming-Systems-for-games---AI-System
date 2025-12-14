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

    [Header("Chase Behaviour")]
    public float lostTimeBeforeReturn = 1.0f;

    [Header("Detection")]
    public float viewDistance = 8f;
    [Range(0f, 180f)] public float viewAngle = 90f;
    public LayerMask obstacleMask;

    [Header("Obstacle Avoidance Tuning")]
    public float avoidRayLength = 1.5f;
    [Range(0, 8)] public int sideRaysPerSide = 1;     // 0 allowed in inspector, but clamped internally
    [Range(5f, 90f)] public float sideRaySpreadAngle = 45f;
    public float steerBias = 0.2f;

    [Header("Attack")]
    public float attackRange = 1.8f;
    public float attackCooldown = 0.8f;

    [Header("Investigate")]
    public float investigateSearchTime = 2.0f;

    [Header("Memory")]
    public float lastSeenUpdateDelay = 0.2f; // "ping" delay
    public Vector3 lastSeenPlayerPos { get; private set; }
    public bool hasLastSeenPos { get; private set; }
    private float lastSeenDelayTimer;

    [Header("Debug")]
    public string currentStateName;
    public bool debugCanSeePlayer { get; private set; }

    [Header("Debug Path Preview")]
    public bool drawPathPreview = true;
    public int previewSteps = 25;
    public float previewStepTime = 0.08f;
    public float previewTurnSpeedMultiplier = 1f;

    // Debug steering info (for gizmos)
    private Vector3 debugDesiredDir;
    private Vector3 debugSteerDir;

    // Debug move target (what the AI is actually moving toward)
    public Vector3 debugMoveTarget { get; private set; }
    public bool hasDebugMoveTarget { get; private set; }
    public void SetDebugMoveTarget(Vector3 pos)
    {
        debugMoveTarget = pos;
        hasDebugMoveTarget = true;
    }

    // Other
    private Rigidbody rb;
    public StateMachine fsm { get; private set; }

    // States
    private IdleState idleState;
    private PatrolState patrolState;
    private ChaseState chaseState;
    private AttackState attackState;
    private InvestigateState investigateState;

    private void Awake()
    {
        fsm = new StateMachine();

        idleState = new IdleState(this);
        patrolState = new PatrolState(this);
        chaseState = new ChaseState(this);
        attackState = new AttackState(this);
        investigateState = new InvestigateState(this);

        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("EnemyAI: No Rigidbody found. Add a Rigidbody to the Enemy (Is Kinematic = true recommended).");
        }
    }

    private void Start()
    {
        if (player == null)
            Debug.LogWarning("EnemyAI: Player reference not set. Drag Player into EnemyAI inspector.");

        if (patrolPoints == null || patrolPoints.Length == 0)
            Debug.LogWarning("EnemyAI: No patrol points set. Add some to patrolPoints in Inspector.");

        fsm.ChangeState(idleState);
    }

    // Tick ONLY here (physics step), since we use Rigidbody.MovePosition.
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Cache visibility once per physics tick (prevents flip-flop/jitter)
        debugCanSeePlayer = CanSeePlayer();

        // Update memory and state machine once
        UpdateLastSeen(dt);
        fsm.Tick(dt);
    }

    // -----------------------------
    // Movement helpers
    // -----------------------------

    public void MoveTowards(Vector3 targetPosition, float deltaTime)
    {
        SetDebugMoveTarget(targetPosition);

        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 desiredDir = toTarget.normalized;
        Vector3 steerDir = GetSteeringDirection(desiredDir);

        // Rotate towards steering direction
        Quaternion targetRot = Quaternion.LookRotation(steerDir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * deltaTime);

        // Move forward (physics-friendly)
        Vector3 step = transform.forward * (moveSpeed * deltaTime);

        if (rb != null)
            rb.MovePosition(rb.position + step);
        else
            transform.position += step; // fallback
    }

    public void FaceTowards(Vector3 targetPosition, float deltaTime)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * deltaTime);
    }

    public bool IsAtPosition(Vector3 targetPosition)
    {
        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = targetPosition; b.y = 0f;
        return Vector3.Distance(a, b) <= waypointReachDistance;
    }

    // -----------------------------
    // Perception
    // -----------------------------

    public bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 origin = transform.position + Vector3.up * 1.0f;
        Vector3 toPlayer = (player.position + Vector3.up * 1.0f) - origin;

        float dist = toPlayer.magnitude;
        if (dist > viewDistance) return false;

        Vector3 dir = toPlayer.normalized;

        // FOV check
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > viewAngle * 0.5f) return false;

        // Line of sight check (walls block vision)
        if (Physics.Raycast(origin, dir, dist, obstacleMask))
            return false;

        return true;
    }

    public bool IsPlayerInAttackRange()
    {
        if (player == null) return false;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        return Vector3.Distance(a, b) <= attackRange;
    }

    // -----------------------------
    // Memory: last seen position
    // -----------------------------

    public void UpdateLastSeen(float deltaTime)
    {
        if (player == null) return;

        if (debugCanSeePlayer)
        {
            lastSeenDelayTimer += deltaTime;
            if (lastSeenDelayTimer >= lastSeenUpdateDelay)
            {
                lastSeenPlayerPos = player.position;
                hasLastSeenPos = true;
                lastSeenDelayTimer = 0f;
            }
        }
        else
        {
            lastSeenDelayTimer = 0f;
        }
    }

    // -----------------------------
    // Obstacle avoidance (scalable rays)
    // -----------------------------

    public Vector3 GetSteeringDirection(Vector3 desiredDir)
    {
        Vector3 origin = transform.position + Vector3.up * 0.8f;
        debugDesiredDir = desiredDir;

        // Forward test
        if (!Physics.Raycast(origin, desiredDir, avoidRayLength, obstacleMask))
        {
            debugSteerDir = desiredDir;
            return desiredDir;
        }

        int rays = Mathf.Max(1, sideRaysPerSide);

        Vector3 bestDir = desiredDir;
        float bestScore = float.NegativeInfinity;

        for (int i = 1; i <= rays; i++)
        {
            float t = i / (float)rays;
            float angle = Mathf.Lerp(0f, sideRaySpreadAngle, t);

            Vector3 leftDir = Quaternion.AngleAxis(-angle, Vector3.up) * desiredDir;
            Vector3 rightDir = Quaternion.AngleAxis(angle, Vector3.up) * desiredDir;

            bool leftBlocked = Physics.Raycast(origin, leftDir, avoidRayLength, obstacleMask);
            bool rightBlocked = Physics.Raycast(origin, rightDir, avoidRayLength, obstacleMask);

            if (!leftBlocked)
            {
                float score = Vector3.Dot(transform.forward, leftDir) + steerBias;
                if (score > bestScore) { bestScore = score; bestDir = leftDir; }
            }

            if (!rightBlocked)
            {
                float score = Vector3.Dot(transform.forward, rightDir) + steerBias;
                if (score > bestScore) { bestScore = score; bestDir = rightDir; }
            }
        }

        debugSteerDir = (bestScore > float.NegativeInfinity) ? bestDir : desiredDir;
        return debugSteerDir;
    }

    // Used by predicted path gizmo (doesn't depend on transform.forward)
    private Vector3 GetSteeringDirectionFrom(Vector3 pos, Quaternion rot, Vector3 desiredDir)
    {
        Vector3 origin = pos + Vector3.up * 0.8f;

        if (!Physics.Raycast(origin, desiredDir, avoidRayLength, obstacleMask))
            return desiredDir;

        int rays = Mathf.Max(1, sideRaysPerSide);

        Vector3 bestDir = desiredDir;
        float bestScore = float.NegativeInfinity;

        Vector3 forward = rot * Vector3.forward;

        for (int i = 1; i <= rays; i++)
        {
            float t = i / (float)rays;
            float angle = Mathf.Lerp(0f, sideRaySpreadAngle, t);

            Vector3 leftDir = Quaternion.AngleAxis(-angle, Vector3.up) * desiredDir;
            Vector3 rightDir = Quaternion.AngleAxis(angle, Vector3.up) * desiredDir;

            bool leftBlocked = Physics.Raycast(origin, leftDir, avoidRayLength, obstacleMask);
            bool rightBlocked = Physics.Raycast(origin, rightDir, avoidRayLength, obstacleMask);

            if (!leftBlocked)
            {
                float score = Vector3.Dot(forward, leftDir);
                if (score > bestScore) { bestScore = score; bestDir = leftDir; }
            }

            if (!rightBlocked)
            {
                float score = Vector3.Dot(forward, rightDir);
                if (score > bestScore) { bestScore = score; bestDir = rightDir; }
            }
        }

        return bestDir;
    }

    // -----------------------------
    // Gizmos
    // -----------------------------

    private void OnDrawGizmos()
    {
        DrawFOVGizmos();

        if (!Application.isPlaying) return;

        DrawObstacleAvoidanceGizmosMulti();
        DrawPredictedPathGizmo();
        DrawLastSeenLinks();

        // Show the current move target (what MoveTowards was last called with)
        if (hasDebugMoveTarget)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(debugMoveTarget + Vector3.up * 0.1f, 0.2f);
        }
    }

    private void DrawFOVGizmos()
    {
        float radius = Mathf.Max(0.01f, viewDistance);

        bool sees = Application.isPlaying && debugCanSeePlayer;
        bool chasing = Application.isPlaying && currentStateName == "Chase";

        if (sees) Gizmos.color = Color.green;
        else if (chasing) Gizmos.color = new Color(1f, 0.65f, 0f);
        else Gizmos.color = Color.red;

        Vector3 origin = transform.position + Vector3.up * 1.0f;

        float halfAngle = viewAngle * 0.5f;
        Vector3 leftDir = DirFromAngle(-halfAngle);
        Vector3 rightDir = DirFromAngle(halfAngle);

        Gizmos.DrawLine(origin, origin + leftDir * radius);
        Gizmos.DrawLine(origin, origin + rightDir * radius);

        int segments = 24;
        Vector3 prevPoint = origin + DirFromAngle(-halfAngle) * radius;

        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 nextPoint = origin + DirFromAngle(angle) * radius;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }

    private void DrawObstacleAvoidanceGizmosMulti()
    {
        Vector3 origin = transform.position + Vector3.up * 0.8f;

        // Forward
        bool forwardBlocked = Physics.Raycast(origin, debugDesiredDir, avoidRayLength, obstacleMask);
        Gizmos.color = forwardBlocked ? Color.red : Color.green;
        Gizmos.DrawRay(origin, debugDesiredDir * avoidRayLength);

        int rays = Mathf.Max(1, sideRaysPerSide);

        for (int i = 1; i <= rays; i++)
        {
            float t = i / (float)rays;
            float angle = Mathf.Lerp(0f, sideRaySpreadAngle, t);

            Vector3 leftDir = Quaternion.AngleAxis(-angle, Vector3.up) * debugDesiredDir;
            Vector3 rightDir = Quaternion.AngleAxis(angle, Vector3.up) * debugDesiredDir;

            bool leftBlocked = Physics.Raycast(origin, leftDir, avoidRayLength, obstacleMask);
            bool rightBlocked = Physics.Raycast(origin, rightDir, avoidRayLength, obstacleMask);

            Gizmos.color = leftBlocked ? Color.red : Color.green;
            Gizmos.DrawRay(origin, leftDir * avoidRayLength);

            Gizmos.color = rightBlocked ? Color.red : Color.green;
            Gizmos.DrawRay(origin, rightDir * avoidRayLength);
        }

        // Chosen steering direction
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, debugSteerDir * avoidRayLength);
    }

    private void DrawPredictedPathGizmo()
    {
        if (!drawPathPreview) return;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        // IMPORTANT: preview the ACTUAL movement target (set by MoveTowards)
        Vector3 targetPos = hasDebugMoveTarget ? debugMoveTarget : (transform.position + transform.forward * 5f);

        Gizmos.color = Color.white;

        for (int i = 0; i < Mathf.Max(1, previewSteps); i++)
        {
            Vector3 toTarget = targetPos - pos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) break;

            Vector3 desiredDir = toTarget.normalized;
            Vector3 steerDir = GetSteeringDirectionFrom(pos, rot, desiredDir);

            Quaternion targetRot = Quaternion.LookRotation(steerDir, Vector3.up);
            rot = Quaternion.RotateTowards(rot, targetRot, turnSpeed * previewTurnSpeedMultiplier * previewStepTime);

            Vector3 nextPos = pos + (rot * Vector3.forward) * (moveSpeed * previewStepTime);

            Gizmos.DrawLine(pos + Vector3.up * 0.05f, nextPos + Vector3.up * 0.05f);
            pos = nextPos;
        }
    }

    private void DrawLastSeenLinks()
    {
        if (!hasLastSeenPos) return;

        Vector3 last = lastSeenPlayerPos + Vector3.up * 0.2f;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(last, 0.2f);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.0f, last);

        if (player != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(player.position + Vector3.up * 1.0f, last);
        }
    }

    private Vector3 DirFromAngle(float angleDegrees)
    {
        float rad = (transform.eulerAngles.y + angleDegrees) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    // -----------------------------
    // State transitions
    // -----------------------------

    public void GoToIdle() => fsm.ChangeState(idleState);
    public void GoToPatrol() => fsm.ChangeState(patrolState);
    public void GoToChase() => fsm.ChangeState(chaseState);
    public void GoToAttack() => fsm.ChangeState(attackState);
    public void GoToInvestigate() => fsm.ChangeState(investigateState);
}
