using UnityEngine;
using TMPro;

public class Speedometer : MonoBehaviour
{
    public RK4Orbit orbitScript;
    [SerializeField] private float c = 5000f;
    [SerializeField] private TMP_Text label;

    void Update()
    {
        if (orbitScript == null || label == null) return;
        float fraction = orbitScript.currentVelocity.magnitude / c;
        label.text = $"{fraction:F6}/c";
    }
}
