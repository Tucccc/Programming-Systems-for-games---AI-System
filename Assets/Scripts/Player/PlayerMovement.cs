using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;

    private Rigidbody rb;
    private float moveX;
    private float moveZ;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        moveX = Input.GetAxis("Horizontal"); // A/D
        moveZ = Input.GetAxis("Vertical");   // W/S
    }

    void FixedUpdate()
    {
        Vector3 movement = new Vector3(moveX, 0f, moveZ);
        rb.MovePosition(rb.position + movement * speed * Time.fixedDeltaTime);
    }
}
