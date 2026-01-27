using UnityEngine;

public class DamagePopupSpawner : MonoBehaviour
{
    public static DamagePopupSpawner Instance { get; private set; }

    [Header("Prefab")]
    public DamagePopup popupPrefab;

    [Header("Optional")]
    public Transform worldParent;
    public Camera targetCamera;

    [Header("Offsets (Base)")]
    public float extraUpOffset = 0.2f;

    [Tooltip("스폰 순간 옆으로 '팍' 나타나는 느낌이 있으면 0으로 두세요.")]
    public float randomJitter = 0.0f;

    // =========================
    // ✅ PlayerTaken 전용 보정(카메라 기준)
    // - x: 화면 오른쪽(+)
    // - y: 화면 위쪽(+)
    // 단위는 '월드 단위(m)' 입니다.
    // =========================
    [Header("PlayerTaken Offset (Camera Space)")]
    public Vector2 playerTakenCameraOffset = new Vector2(0.25f, 0.0f);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (targetCamera == null) targetCamera = Camera.main;
    }

    // ✅ 신버전: 타입 포함
    public void Spawn(Transform target, Transform attacker, int damage, bool isCritical, DamagePopupType type)
    {
        if (popupPrefab == null || target == null) return;

        if (targetCamera == null) targetCamera = Camera.main;

        Vector3 basePos = GetHeadWorldPosition(target);

        // ✅ PlayerTaken만 "화면 기준" 오프셋 적용 (몬스터 쪽은 영향 없음)
        if (type == DamagePopupType.PlayerTaken && targetCamera != null)
        {
            basePos += targetCamera.transform.right * playerTakenCameraOffset.x;
            basePos += targetCamera.transform.up * playerTakenCameraOffset.y;
        }

        // (선택) 겹침 방지 지터
        Vector3 jitter = Vector3.zero;
        if (randomJitter > 0f)
        {
            jitter = new Vector3(
                Random.Range(-randomJitter, randomJitter),
                0f,
                Random.Range(-randomJitter, randomJitter)
            );
        }

        DamagePopup popup = Instantiate(popupPrefab, basePos + jitter, Quaternion.identity, worldParent);
        popup.Init(damage, isCritical, type, attacker, target, targetCamera);
    }

    // ✅ 구버전 호환: 기존 호출이 있다면 컴파일 깨지지 않게 유지(기본은 PlayerDeal)
    public void Spawn(Transform target, Transform attacker, int damage, bool isCritical)
    {
        Spawn(target, attacker, damage, isCritical, DamagePopupType.PlayerDeal);
    }

    private Vector3 GetHeadWorldPosition(Transform target)
    {
        Renderer r = target.GetComponentInChildren<Renderer>();
        if (r != null)
            return r.bounds.center + Vector3.up * (r.bounds.extents.y + extraUpOffset);

        Collider c = target.GetComponentInChildren<Collider>();
        if (c != null)
            return c.bounds.center + Vector3.up * (c.bounds.extents.y + extraUpOffset);

        return target.position + Vector3.up * (2.0f + extraUpOffset);
    }
}
