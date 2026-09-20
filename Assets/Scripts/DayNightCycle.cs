using UnityEngine;

[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    [Header("Previsualización en Editor")]
    [Range(0f, 1f)]
    public float controlTiempoManual = 0f;

    [Header("Duraciones")]
    [Tooltip("Tiempo total en segundos (ej. 180s o 10s para pruebas)")]
    public float duracionTotalSegundos = 180f;
    [Tooltip("Segundos antes del final en que sale el sol")]
    public float duracionAmanecerSegundos = 5f;

    [Header("Objetos en Escena")]
    public Transform lunaTransform;
    public Transform solTransform;
    public Light directionalLight;

    [Header("Puntos de Trayectoria Luna (Izquierda -> Derecha)")]
    public Vector3 posLunaInicio = new Vector3(-35f, 60f, 75f);
    public Vector3 posLunaFin = new Vector3(35f, 15f, 75f);
    public Vector3 rotLunaInicio = new Vector3(25f, 45f, 0f);
    public Vector3 rotLunaFin = new Vector3(15f, -45f, 0f);

    [Header("Puntos de Trayectoria Sol (Mismo lado izquierdo -> Arriba)")]
    public Vector3 posSolInicio = new Vector3(-35f, -10f, 75f);
    public Vector3 posSolCenit = new Vector3(-35f, 65f, 75f);
    public Vector3 rotSolInicio = new Vector3(-15f, 45f, 0f);
    public Vector3 rotSolMediodia = new Vector3(60f, 25f, 0f);

    [Header("Iluminación")]
    public Color colorNoche = new Color(0.12f, 0.18f, 0.38f);
    public Color colorAmanecer = new Color(1f, 0.6f, 0.3f);
    public Color colorDia = new Color(1f, 0.95f, 0.85f);
    public float intensidadNoche = 0.2f;
    public float intensidadDia = 1.3f;

    public Color ambienteNoche = new Color(0.04f, 0.05f, 0.12f);
    public Color ambienteDia = new Color(0.6f, 0.7f, 0.8f);

    private float tiempoActual = 0f;

    private void Start()
    {
        if (Application.isPlaying)
        {
            tiempoActual = 0f;
            ActualizarCiclo(0f);
        }
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            if (tiempoActual < duracionTotalSegundos)
            {
                tiempoActual += Time.deltaTime;
                controlTiempoManual = Mathf.Clamp01(tiempoActual / duracionTotalSegundos);
                ActualizarCiclo(controlTiempoManual);
            }
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ActualizarCiclo(controlTiempoManual);
        }
    }

    public void ActualizarCiclo(float tGlobal)
    {
        float umbralAmanecer = Mathf.Clamp01((duracionTotalSegundos - duracionAmanecerSegundos) / duracionTotalSegundos);

        // --- FASE 1: NOCHE (Luna cruzando el firmamento) ---
        if (tGlobal < umbralAmanecer)
        {
            if (lunaTransform != null && !lunaTransform.gameObject.activeSelf)
                lunaTransform.gameObject.SetActive(true);

            if (solTransform != null && solTransform.gameObject.activeSelf)
                solTransform.gameObject.SetActive(false);

            float tLuna = umbralAmanecer > 0f ? (tGlobal / umbralAmanecer) : 0f;

            // Movimiento físico y rotación
            if (lunaTransform != null)
            {
                lunaTransform.position = Vector3.Lerp(posLunaInicio, posLunaFin, tLuna);
                lunaTransform.rotation = Quaternion.Euler(Vector3.Lerp(rotLunaInicio, rotLunaFin, tLuna));
            }

            // Luz direccional
            if (directionalLight != null)
            {
                directionalLight.transform.rotation = Quaternion.Euler(Vector3.Lerp(rotLunaInicio, rotLunaFin, tLuna));
                directionalLight.color = Color.Lerp(colorNoche, colorAmanecer, tLuna * 0.2f);
                directionalLight.intensity = Mathf.Lerp(intensidadNoche, 0.35f, tLuna);
            }

            RenderSettings.ambientLight = Color.Lerp(ambienteNoche, colorAmanecer * 0.2f, tLuna);
            if (RenderSettings.fog) RenderSettings.fogColor = RenderSettings.ambientLight;
        }
        // --- FASE 2: AMANECER (Sol saliendo en los últimos segundos) ---
        else
        {
            if (lunaTransform != null && lunaTransform.gameObject.activeSelf)
                lunaTransform.gameObject.SetActive(false);

            if (solTransform != null && !solTransform.gameObject.activeSelf)
                solTransform.gameObject.SetActive(true);

            float divisor = 1f - umbralAmanecer;
            float tSol = divisor > 0.0001f ? (tGlobal - umbralAmanecer) / divisor : 1f;

            // Movimiento físico y rotación
            if (solTransform != null)
            {
                solTransform.position = Vector3.Lerp(posSolInicio, posSolCenit, tSol);
                solTransform.rotation = Quaternion.Euler(Vector3.Lerp(rotSolInicio, rotSolMediodia, tSol));
            }

            // Luz direccional
            if (directionalLight != null)
            {
                directionalLight.transform.rotation = Quaternion.Euler(Vector3.Lerp(rotSolInicio, rotSolMediodia, tSol));
                directionalLight.color = Color.Lerp(colorAmanecer, colorDia, tSol);
                directionalLight.intensity = Mathf.Lerp(0.35f, intensidadDia, tSol);
            }

            RenderSettings.ambientLight = Color.Lerp(colorAmanecer * 0.3f, ambienteDia, tSol);
            if (RenderSettings.fog) RenderSettings.fogColor = RenderSettings.ambientLight;
        }
    }
}