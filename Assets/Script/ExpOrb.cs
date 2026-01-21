using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExpOrb : MonoBehaviour
{
    [Header("Value")]
    public int expAmount = 1;

    [Header("Pickup")]
    public string playerTag = "Player";
    public bool destroyOnPickup = true;
    public float pickupTriggerRadius = 0.6f;

    [Header("Physics (Fall Down)")]
    public bool autoSetupRigidbody = true;
    public bool autoCreatePickupTrigger = true;

    [Header("Debug")]
    public bool logPickup = true;
    public bool logMiss = false;

    private bool picked;

    private void Awake()
    {
        var mainCol = GetComponent<Collider>();
        mainCol.isTrigger = false;

        if (autoSetupRigidbody)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        if (autoCreatePickupTrigger)
        {
            SphereCollider trigger = null;
            var cols = GetComponents<Collider>();
            foreach (var c in cols)
            {
                if (c != mainCol && c is SphereCollider sc && sc.isTrigger)
                {
                    trigger = sc;
                    break;
                }
            }
            if (trigger == null)
            {
                trigger = gameObject.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
            }
            trigger.radius = pickupTriggerRadius;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryPickup(other.gameObject);
    }

    private void TryPickup(GameObject other)
    {
        if (picked) return;

        if (!other.CompareTag(playerTag))
        {
            if (logMiss)
                Debug.Log($"[ExpOrb] 태그 불일치: Hit='{other.name}', Tag='{other.tag}'");
            return;
        }

        var playerExp = other.GetComponent<PlayerExp>();
        if (playerExp == null)
        {
            if (logMiss)
                Debug.LogWarning($"[ExpOrb] PlayerExp 없음: '{other.name}'");
            return;
        }

        picked = true;
        playerExp.AddExp(expAmount);

        if (logPickup)
            Debug.Log($"[ExpOrb] +{expAmount} EXP by '{other.name}'");

        if (destroyOnPickup)
            Destroy(gameObject);
    }
}