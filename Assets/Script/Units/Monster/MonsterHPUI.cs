using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class MonsterHPUI : MonoBehaviour
{
    public Slider hpSlider;
    public Transform target;
    public Vector3 offset = new Vector3(0, 2f, 0);

    [Header("Show Option")]
    public bool showOnlyWhenDamaged = true;

    private Camera cam;
    private MonsterStat stat;
    private CanvasGroup cg;
    private bool shown;

    void Awake()
    {
        cam = Camera.main;
        cg = GetComponent<CanvasGroup>();

        // Slider 자동 탐색(인스펙터 미할당 방어)
        if (hpSlider == null)
            hpSlider = GetComponentInChildren<Slider>(true);

        // 타겟 자동 설정(보통 몬스터 루트)
        if (target == null)
            target = transform.root;

        // MonsterStat 자동 탐색
        stat = target != null ? target.GetComponent<MonsterStat>() : null;
        if (stat == null)
            stat = GetComponentInParent<MonsterStat>();

        if (stat == null)
            Debug.LogError($"[MonsterHPUI] MonsterStat을 찾지 못했습니다. target/부모에 MonsterStat이 있어야 합니다.", this);

        // 시작 시 숨김(피격 시 보이게)
        if (showOnlyWhenDamaged) Hide();
        else Show();

        // 슬라이더 기본 세팅
        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = (stat != null && stat.maxHP > 0) ? stat.maxHP : 1f;
            hpSlider.value = (stat != null && stat.hp > 0) ? stat.hp : hpSlider.maxValue;
        }
    }

    void OnEnable()
    {
        if (stat == null) return;

        stat.OnDamaged += HandleDamaged;
        stat.OnHpChanged += HandleHpChanged;
        stat.OnDied += HandleDied;
    }

    void OnDisable()
    {
        if (stat == null) return;

        stat.OnDamaged -= HandleDamaged;
        stat.OnHpChanged -= HandleHpChanged;
        stat.OnDied -= HandleDied;
    }

    void Update()
    {
        if (target == null || cam == null) return;

        transform.position = target.position + offset;
        transform.LookAt(transform.position + cam.transform.forward);
    }

    void HandleDamaged()
    {
        if (showOnlyWhenDamaged && !shown)
            Show();
    }

    void HandleHpChanged(int current, int max)
    {
        if (hpSlider == null) return;

        if (max <= 0) max = 1;
        hpSlider.maxValue = max;
        hpSlider.value = Mathf.Clamp(current, 0, max);
    }

    void HandleDied(MonsterStat m)
    {
        // 필요하면 죽을 때 숨김 처리
        // Hide();
    }

    void Show()
    {
        shown = true;
        cg.alpha = 1f;
        cg.interactable = false;
        cg.blocksRaycasts = false;
    }

    void Hide()
    {
        shown = false;
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;
    }
}
