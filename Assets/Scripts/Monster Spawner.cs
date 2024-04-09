using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterSpawner : MonoBehaviour
{

    [SerializeField]
    private GameObject[] monsters; // This will hold all the available monsters

    [SerializeField]
    private Transform leftPosition , rightPosition; 

    private GameObject spawnedMonster; // This will hold the monster that we will spawn

    // We will now randomly decide which moster to spawn and from which side to spawn it
    private int randomIndex;
    private int randomSide;

    void Start()
    {
        StartCoroutine(SpawnMonster());
    }

    // Update is called once per frame
    IEnumerator SpawnMonster()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(1, 5)); // Wait for a random amount of time before spawning the monster
            randomIndex = Random.Range(0, monsters.Length); // Randomly select a monster
            randomSide = Random.Range(0, 2); // Randomly select a side

            spawnedMonster = Instantiate(monsters[randomIndex]); // Spawn the monster

            if (randomSide == 0)
            {
                // Left Side
                spawnedMonster.transform.position = leftPosition.position;
                spawnedMonster.GetComponent<MonsterScript>().speed = Random.Range(4, 10); // Random speed
            }
            else
            {
                // Right Side
                spawnedMonster.transform.position = rightPosition.position;
                spawnedMonster.GetComponent<MonsterScript>().speed = -Random.Range(4, 10); // Random speed to the left
                spawnedMonster.transform.localScale = new Vector3(-1, 1, 1); // Flip the sprite
            }

        }
        
    }
}
