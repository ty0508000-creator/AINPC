using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Controller : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;

    void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // Input System을 통해 WASD 입력 받기 (신규방식)
        moveInput = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
        
        // 애니메이션 속도 설정
        animator.SetFloat("Speed", moveInput.magnitude);

        // 좌우 스프라이트 반전
        if (moveInput.x < 0)
            spriteRenderer.flipX = true;
        else if (moveInput.x > 0)
            spriteRenderer.flipX = false;
    }

    void FixedUpdate()
    {
        // WASD로 캐릭터 이동
        Vector2 moveDirection = moveInput.normalized;
        transform.position += (Vector3)moveDirection * moveSpeed * Time.fixedDeltaTime;
    }
}
