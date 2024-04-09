using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    [SerializeField] // This will make the fields visible on the inspector panel
    private float moveForce = 10f;
    [SerializeField]
    private float jumpForce = 14f;

    private float movementX;

    private Rigidbody2D myRigidBody;
    private SpriteRenderer mySprite;
    private Animator animator;

    private string WALK_ANIMATION = "Walk"; 
    /*
    Refrence to the boolean variable in the animator. Note that the string you pass here must match 
    with the variable name you attached on the arrows on the animator    
    */
    private string GROUND_TAG = "Ground";
    private string ENEMY_TAG = "Enemy";
    private bool IsGrounded = true;


    private void Awake(){
        myRigidBody = GetComponent<Rigidbody2D>();
        mySprite = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    void Start()
    {
    }

    void Update()
    {
        PlayerMoveKeyboard();
        AnimatePlayer();
        PlayerJump();
    }

    void FixedUpdate()
    {
        // This section will not be called every frame, but in every fixed frame interval
        // Usually used to play around with the physics systems

        PlayerJump();
    }
    void PlayerMoveKeyboard()
    {
        movementX = Input.GetAxisRaw("Horizontal");
        transform.position += new Vector3(movementX, 0f, 0f) * Time.deltaTime * moveForce; 
    }
    void AnimatePlayer()
    {
        if (movementX < 0)
        {
            // Moving to the left, flip the sprite
            animator.SetBool(WALK_ANIMATION, true);
            mySprite.flipX = true;
        }
        else if (movementX > 0)
        {
            // Moving to the right 
            animator.SetBool(WALK_ANIMATION, true);
            mySprite.flipX = false;
        }
        else
        {
            animator.SetBool(WALK_ANIMATION, false);
        }
    }

    public void PlayerJump()
    {
        // Understand the difference between GetButtonDown, GetButtonUp and GetButton

        if (Input.GetButtonDown("Jump") && IsGrounded) // Referening the key assigned for jump (ie, space for PC)
        {
            IsGrounded = false;
            myRigidBody.AddForce(new Vector2(0f , jumpForce) , ForceMode2D.Impulse);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // This function will be called when the player collides with another object. 

        // setting the IsGround flag to true when the player hits the ground
        if (collision.gameObject.CompareTag(GROUND_TAG))
        {
            IsGrounded = true;
        }
        if (collision.gameObject.CompareTag(ENEMY_TAG))
        {
            Destroy(gameObject); // Player is destroyed
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // This will be operated when the player hits an enemy with a trigger collider (ie the ghost)
        // Hence, this will make the ghost destroy the player but goes right through the other enemies

        if (collision.gameObject.CompareTag(ENEMY_TAG))
        {
            Destroy(gameObject); // Player is destroyed
        }
    }

}
