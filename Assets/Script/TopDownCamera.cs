using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    [Header("📌 따라갈 대상(Player)")]
    public Transform target;

    [Header("📌 카메라 위치 설정")]
    public Vector3 offset = new Vector3(0f, 10f, -10f);
    public float followSpeed = 5f;

    [Header("📌 카메라 회전 설정")]
    public float rotationX = 45f;   // 위에서 내려다보는 각도

    void Start()
    {
        // 시작할 때 카메라 각도 고정
        transform.rotation = Quaternion.Euler(rotationX, 0f, 0f);
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 부드럽게 따라가기
        Vector3 targetPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);
    }
}
