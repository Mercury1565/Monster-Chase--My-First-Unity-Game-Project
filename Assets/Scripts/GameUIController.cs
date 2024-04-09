using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameUIController : MonoBehaviour
{
    public void RestartButton()
    {
        SceneManager.LoadScene("Game Play");
    }

    public void HomeButton()
    {
        SceneManager.LoadScene("Main Menu");
    }

}
