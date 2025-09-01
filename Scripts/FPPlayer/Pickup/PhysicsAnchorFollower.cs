using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PhysicsAnchorFollower : MonoBehaviour
{
    [SerializeField] Transform follow; // set to FPController.CameraTransform
    Rigidbody rb;

    void Awake() { rb = GetComponent<Rigidbody>(); }

    void FixedUpdate()
    {
        if (!follow) return;
        rb.MovePosition(follow.position);
        rb.MoveRotation(follow.rotation);
    }
}
