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

    [Header("Wall Clearance / Sliding")]
    public float wallClearance = 0.15f;
    [Range(1, 4)] public int slideIterations = 2;

    [Header("Attack")]
    public float attackRange = 1.8f;
    public float attackCooldown = 0.8f;

    [Header("Investigate")]
    public float investigateSearchTime = 2.0f;

    [Header("Memory")]
    public float lastSeenUpdateDelay = 0.2f;
    public Vector3 lastSeenPlayerPos { get; private set; }
    public bool hasLastSeenPos { get; private set; }
    private float lastSeenDelayTimer;

    [Header("Wall-Edge Detour (Raycast-based)")]
    public float blockRayHeight = 0.8f;
    public float blockRayMaxDistance = 25f;
    public float edgeSampleStep = 0.5f;
    public int edgeSampleCount = 20;
    public float edgeOffsetFromWall = 0.6f;

    [Header("Debug")]
    public string currentStateName;
    public bool debugCanSeePlayer { get; private set; }

    [Header("Debug Path Preview")]
    public bool drawPathPreview = true;
    public int previewSteps = 25;
    public float previewStepTime = 0.08f;
    public float previewTurnSpeedMultiplier = 1f;

    [Header("Debug Wall-Edge Detour")]
    public bool debugDrawEdgeSamples = true;

    // Debug move target (what MoveTowards last aimed at)
    public Vector3 debugMoveTarget { get; private set; }
    public bool hasDebugMoveTarget { get; private set; }

    // Debug wall-edge detour visuals
    private Vector3 debugBlockRayStart, debugBlockRayEnd;
    private bool debugPathBlocked;
    private Vector3 debugChosenDetour;
    private bool debugHasDetour;
    private Vector3[] debugLeftSamples;
    private Vector3[] debugRightSamples;
    private int debugLeftCount, debugRightCount;

    // Components
    private Rigidbody rb;
    private CapsuleCollider capsule;

    // FSM
    public StateMachine fsm { get; private set; }

    // States
    private IdleState idleState;
    private PatrolState patrolState;
    private ChaseState chaseState;
    private AttackState attackState;
    private InvestigateState investigateState;

    // Detour internals
    private bool usingDetour;
    private Vector3 detourPoint;

    public bool IsDetouring => usingDetour;
    public Vector3 DetourPoint => detourPoint;

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
            Debug.LogError("EnemyAI: No Rigidbody found. Add one to the Enemy.");

        capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
            Debug.LogWarning("EnemyAI: No CapsuleCollider found. Wall sliding will fall back to SphereCast.");
    }

    private void Start()
    {
        if (player == null)
            Debug.LogWarning("EnemyAI: Player reference not set. Drag Player into EnemyAI inspector.");

        if (patrolPoints == null || patrolPoints.Length == 0)
            Debug.LogWarning("EnemyAI: No patrol points set. Add some to patrolPoints in Inspector.");

        usingDetour = false;
        detourPoint = transform.position;

        fsm.ChangeState(idleState);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Cache vision once per physics step
        debugCanSeePlayer = CanSeePlayer();

        UpdateLastSeen(dt);
        fsm.Tick(dt);
    }

    // -----------------------------
    // Movement
    // -----------------------------

    public void SetDebugMoveTarget(Vector3 pos)
    {
        debugMoveTarget = pos;
        hasDebugMoveTarget = true;
    }

    public void MoveTowards(Vector3 targetPosition, float deltaTime)
    {
        SetDebugMoveTarget(targetPosition);

        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 desiredDir = toTarget.normalized;

        // Rotate towards desired direction
        Quaternion targetRot = Quaternion.LookRotation(desiredDir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * deltaTime);

        // Desired forward step
        Vector3 step = transform.forward * (moveSpeed * deltaTime);

        if (rb != null)
        {
            Vector3 finalMove = ResolveWallSlideMove(rb.position, transform.rotation, step);
            rb.MovePosition(rb.position + finalMove);
        }
        else
        {
            transform.position += step;
        }
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

        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > viewAngle * 0.5f) return false;

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
    // Memory
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

    public void ForceLastSeen(Vector3 pos)
    {
        lastSeenPlayerPos = pos;
        hasLastSeenPos = true;
        lastSeenDelayTimer = 0f;
    }

    // -----------------------------
    // Detour: wall-edge waypoint
    // -----------------------------

    public Vector3 GetMoveTargetWithDetour(Vector3 finalTarget, float deltaTime)
    {
        // If we’re currently going to a detour point, keep doing it until reached
        if (usingDetour)
        {
            if (IsAtPosition(detourPoint))
                usingDetour = false;
            else
                return detourPoint;
        }

        // If the direct route is blocked, try to create a detour waypoint around the wall
        if (TryFindDetourByWallEdge(finalTarget, out Vector3 edgeDetour))
        {
            usingDetour = true;
            detourPoint = edgeDetour;
            return detourPoint;
        }

        return finalTarget;
    }

    public bool TryFindDetourByWallEdge(Vector3 finalTarget, out Vector3 detourPointOut)
    {
        detourPointOut = Vector3.zero;

        Vector3 origin = transform.position + Vector3.up * blockRayHeight;
        Vector3 target = finalTarget + Vector3.up * blockRayHeight;
        Vector3 toTarget = target - origin;

        if (toTarget.sqrMagnitude < 0.0001f) return false;

        float distToTarget = Mathf.Min(blockRayMaxDistance, toTarget.magnitude);
        Vector3 dirToTarget = toTarget.normalized;

        // Debug ray
        debugBlockRayStart = origin;
        debugBlockRayEnd = origin + dirToTarget * distToTarget;

        // If nothing blocks LOS to finalTarget, no detour needed
        debugPathBlocked = Physics.Raycast(origin, dirToTarget, out RaycastHit hit, distToTarget, obstacleMask);
        if (!debugPathBlocked)
        {
            debugHasDetour = false;
            return false;
        }

        Vector3 hitPoint = hit.point;
        Vector3 normal = hit.normal; normal.y = 0f;
        if (normal.sqrMagnitude < 0.0001f) return false;
        normal.Normalize();

        Vector3 tangentA = Vector3.Cross(Vector3.up, normal).normalized;
        Vector3 tangentB = -tangentA;

        if (debugLeftSamples == null || debugLeftSamples.Length != edgeSampleCount)
        {
            debugLeftSamples = new Vector3[edgeSampleCount];
            debugRightSamples = new Vector3[edgeSampleCount];
        }
        debugLeftCount = debugRightCount = 0;

        bool foundA = TrySampleEdge(hitPoint, normal, tangentA, finalTarget, ref debugLeftSamples, ref debugLeftCount, out Vector3 bestA, out float scoreA);
        bool foundB = TrySampleEdge(hitPoint, normal, tangentB, finalTarget, ref debugRightSamples, ref debugRightCount, out Vector3 bestB, out float scoreB);

        if (!foundA && !foundB)
        {
            debugHasDetour = false;
            return false;
        }

        detourPointOut = (foundA && (!foundB || scoreA >= scoreB)) ? bestA : bestB;

        debugChosenDetour = detourPointOut;
        debugHasDetour = true;
        return true;
    }

    private bool TrySampleEdge(
        Vector3 hitPoint,
        Vector3 wallNormal,
        Vector3 tangentDir,
        Vector3 finalTarget,
        ref Vector3[] debugSamples,
        ref int debugCount,
        out Vector3 bestPoint,
        out float bestScore
    )
    {
        bestPoint = Vector3.zero;
        bestScore = float.NegativeInfinity;

        Vector3 originToCheckFrom = transform.position + Vector3.up * blockRayHeight;
        Vector3 finalTargetCheck = finalTarget + Vector3.up * blockRayHeight;

        for (int i = 1; i <= edgeSampleCount; i++)
        {
            Vector3 candidateOnEdge = hitPoint + tangentDir * (edgeSampleStep * i);

            // Push candidate away from wall
            Vector3 candidate = candidateOnEdge + wallNormal * edgeOffsetFromWall;
            candidate.y = transform.position.y;

            if (debugDrawEdgeSamples && debugSamples != null && debugCount < debugSamples.Length)
                debugSamples[debugCount++] = candidate;

            // Not inside wall
            if (Physics.CheckSphere(candidate + Vector3.up * 0.5f, 0.2f, obstacleMask))
                continue;

            // Reachable from enemy (helps stop "behind wall" candidates)
            Vector3 toCandidate = (candidate + Vector3.up * blockRayHeight) - originToCheckFrom;
            float distCand = toCandidate.magnitude;
            if (distCand > 0.01f && Physics.Raycast(originToCheckFrom, toCandidate.normalized, distCand, obstacleMask))
                continue;

            // Candidate must have LOS to final target
            Vector3 candOrigin = candidate + Vector3.up * blockRayHeight;
            Vector3 toFinal = finalTargetCheck - candOrigin;
            float distFinal = toFinal.magnitude;
            if (distFinal > 0.01f && Physics.Raycast(candOrigin, toFinal.normalized, distFinal, obstacleMask))
                continue;

            float score = -Vector3.Distance(candidate, finalTarget);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
            }
        }

        return bestScore > float.NegativeInfinity;
    }

    // -----------------------------
    // Wall clearance / slide
    // -----------------------------

    private Vector3 ResolveWallSlideMove(Vector3 startPos, Quaternion rot, Vector3 desiredMove)
    {
        if (desiredMove.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        float clearance = Mathf.Max(0.001f, wallClearance);

        // Fallback if no capsule
        if (capsule == null)
        {
            Vector3 pos = startPos;
            Vector3 move = desiredMove;

            Vector3 baseOrigin = startPos + Vector3.up * 0.8f;
            float radius = 0.35f;

            for (int i = 0; i < slideIterations; i++)
            {
                float dist = move.magnitude;
                if (dist <= 0.0001f) break;

                if (Physics.SphereCast(baseOrigin + (pos - startPos), radius, move.normalized,
                    out RaycastHit hit, dist + clearance, obstacleMask))
                {
                    float safeDist = Mathf.Max(0f, hit.distance - clearance);
                    Vector3 toContact = move.normalized * safeDist;

                    pos += toContact;

                    Vector3 remaining = move - toContact;
                    move = Vector3.ProjectOnPlane(remaining, hit.normal);
                }
                else
                {
                    pos += move;
                    break;
                }
            }

            return pos - startPos;
        }

        // CapsuleCast using our actual collider size
        void BuildCapsule(Vector3 pos, out Vector3 p1, out Vector3 p2, out float radius)
        {
            float scaleXZ = Mathf.Max(transform.localScale.x, transform.localScale.z);
            float scaleY = transform.localScale.y;

            radius = capsule.radius * scaleXZ;
            float height = Mathf.Max(capsule.height * scaleY, radius * 2f);
            float half = Mathf.Max(0f, (height * 0.5f) - radius);

            Vector3 center = pos + (rot * capsule.center);
            p1 = center + Vector3.up * half;
            p2 = center - Vector3.up * half;
        }

        Vector3 currentPos = startPos;
        Vector3 moveVec = desiredMove;

        for (int i = 0; i < slideIterations; i++)
        {
            float dist = moveVec.magnitude;
            if (dist <= 0.0001f) break;

            BuildCapsule(currentPos, out Vector3 p1, out Vector3 p2, out float r);

            if (Physics.CapsuleCast(p1, p2, r, moveVec.normalized,
                out RaycastHit hit, dist + clearance, obstacleMask))
            {
                float safeDist = Mathf.Max(0f, hit.distance - clearance);
                Vector3 toContact = moveVec.normalized * safeDist;

                currentPos += toContact;

                Vector3 remaining = moveVec - toContact;
                moveVec = Vector3.ProjectOnPlane(remaining, hit.normal);
            }
            else
            {
                currentPos += moveVec;
                break;
            }
        }

        return currentPos - startPos;
    }

    // -----------------------------
    // Gizmos
    // -----------------------------

    private void OnDrawGizmos()
    {
        DrawFOVGizmos();

        if (!Application.isPlaying) return;

        DrawPredictedPathGizmo();
        DrawLastSeenLinks();

        if (hasDebugMoveTarget)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(debugMoveTarget + Vector3.up * 0.1f, 0.2f);
        }

        if (usingDetour)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f); // orange
            Gizmos.DrawWireSphere(detourPoint + Vector3.up * 0.1f, 0.25f);
        }

        // blocked ray
        Gizmos.color = debugPathBlocked ? Color.red : Color.green;
        Gizmos.DrawLine(debugBlockRayStart, debugBlockRayEnd);

        // sample points
        if (debugDrawEdgeSamples)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < debugLeftCount; i++) Gizmos.DrawSphere(debugLeftSamples[i] + Vector3.up * 0.05f, 0.08f);

            Gizmos.color = Color.cyan;
            for (int i = 0; i < debugRightCount; i++) Gizmos.DrawSphere(debugRightSamples[i] + Vector3.up * 0.05f, 0.08f);
        }

        // chosen detour
        if (debugHasDetour)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(debugChosenDetour + Vector3.up * 0.1f, 0.25f);
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
            float ang = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 nextPoint = origin + DirFromAngle(ang) * radius;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }

    private void DrawPredictedPathGizmo()
    {
        if (!drawPathPreview) return;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        Vector3 targetPos = hasDebugMoveTarget ? debugMoveTarget : (transform.position + transform.forward * 5f);

        Gizmos.color = Color.white;

        for (int i = 0; i < Mathf.Max(1, previewSteps); i++)
        {
            Vector3 toTarget = targetPos - pos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) break;

            Vector3 desiredDir = toTarget.normalized;

            Quaternion targetRot = Quaternion.LookRotation(desiredDir, Vector3.up);
            rot = Quaternion.RotateTowards(rot, targetRot, turnSpeed * previewTurnSpeedMultiplier * previewStepTime);

            Vector3 desiredStep = (rot * Vector3.forward) * (moveSpeed * previewStepTime);
            Vector3 moved = ResolveWallSlideMove(pos, rot, desiredStep);
            Vector3 nextPos = pos + moved;

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
