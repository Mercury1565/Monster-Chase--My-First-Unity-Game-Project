using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    private Transform player;
    private Vector3 cameraPos;

    [SerializeField]
    private float minX , maxX;

    void Start()
    {
        // The postition of the player will be attached on player
        // We locate the player object by finding the object with the tag "Player"
        player = GameObject.FindWithTag("Player").transform;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        // This sections every frame as well as Update(), but it will be called after all the Update() funs

        // If the player is destroyed, we will not be able to follow the player
        if (!player)
            return;

        cameraPos = transform.position;
        cameraPos.x = player.position.x;

        // We will limit the camera's movement to the minX and maxX values
        if(cameraPos.x < minX)
            cameraPos.x = minX;
        
        if(cameraPos.x > maxX)
            cameraPos.x = maxX;
        

        transform.position = cameraPos;
    }
}
