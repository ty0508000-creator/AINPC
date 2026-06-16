using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Controller : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    public float MoveSpeed => moveSpeed;
    public Vector2 LastMoveDir { get; private set; } = Vector2.right;

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Player_Attack playerAttack;
    private ControlManager controlManager;

    void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        playerAttack = GetComponent<Player_Attack>();
        controlManager = GetComponent<ControlManager>();
    }

    void Update()
    {
        bool blocked =
            (playerAttack != null && playerAttack.IsInvincible) ||
            (controlManager != null && !controlManager.IsPlayerControlled) ||
            DialogueManager.IsDialogueOpen;

        if (blocked)
        {
            moveInput = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
        else
        {
            moveInput = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
            if (moveInput != Vector2.zero)
                LastMoveDir = moveInput.normalized;
        }

        animator.SetFloat("Speed", moveInput.magnitude);

        if (moveInput.x < 0)
            spriteRenderer.flipX = true;
        else if (moveInput.x > 0)
            spriteRenderer.flipX = false;
    }

    void FixedUpdate()
    {
        if ((playerAttack != null && playerAttack.IsInvincible) ||
            (controlManager != null && !controlManager.IsPlayerControlled) ||
            DialogueManager.IsDialogueOpen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        rb.linearVelocity = moveInput.normalized * moveSpeed;
    }
}
