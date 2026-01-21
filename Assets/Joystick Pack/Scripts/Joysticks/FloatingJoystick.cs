using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class FloatingJoystick : Joystick
{
    private Vector2 defaultPosition;  // 처음 위치 저장

    protected override void Start()
    {
        base.Start();

        // 처음 위치 저장
        defaultPosition = background.anchoredPosition;

        // 시작할 때 조이스틱 보이기
        background.gameObject.SetActive(true);
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        // 터치한 위치로 이동
        background.anchoredPosition = ScreenPointToAnchoredPosition(eventData.position);

        background.gameObject.SetActive(true);

        base.OnPointerDown(eventData);
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        // 손을 떼면 → 처음 위치로 돌아가기
        background.anchoredPosition = defaultPosition;

        background.gameObject.SetActive(true);

        base.OnPointerUp(eventData);
    }
}
