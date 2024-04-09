using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterScript : MonoBehaviour
{
    [HideInInspector]
    public float speed; // This need to be public because we will access it on the MonsterSpawner script


    private Rigidbody2D myRigidBody;
    void Awake()
    {
        myRigidBody = GetComponent<Rigidbody2D>();

    }
    void Start()
    {
    }

    void FixedUpdate()
    {
        myRigidBody.velocity = new Vector2(speed, myRigidBody.velocity.y);
    }
}
