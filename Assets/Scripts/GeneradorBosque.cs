using UnityEngine;

public class GeneradorBosque : MonoBehaviour
{
    [Header("Referencias")]
    public Transform contenedorArboles; // Objeto 'Tree'
    public Transform ground;            // Objeto 'Ground'

    [Header("Configuración de Densidad")]
    [Range(50, 600)] public int cantidadArboles = 250; // Sube a 250-350 para bosque tupido
    public float margenBorde = 3.5f;     // Distancia de seguridad hacia adentro del suelo
    public float radioSeguroCentro = 6f; // Zona libre para tienda y fogata
    public float ajusteAlturaY = -0.05f; // Un milímetro enterrado para evitar luz por debajo

    [Header("Corrección de Inclinación")]
    public Vector3 rotacionAdicional = Vector3.zero;

    [ContextMenu("1. Repoblar Bosque")]
    public void Generar()
    {
        if (contenedorArboles == null || ground == null)
        {
            Debug.LogError("Asigna 'contenedorArboles' y 'ground' en el Inspector.");
            return;
        }

        Limpiar();

        int totalTipos = contenedorArboles.childCount;
        if (totalTipos == 0) return;

        GameObject[] modelos = new GameObject[totalTipos];
        for (int i = 0; i < totalTipos; i++)
        {
            modelos[i] = contenedorArboles.GetChild(i).gameObject;
        }

        Collider col = ground.GetComponent<Collider>();
        Bounds bounds = col != null ? col.bounds : new Bounds(ground.position, new Vector3(25, 1, 25));

        GameObject grupoBosque = new GameObject("Bosque_Generado");
        grupoBosque.transform.SetParent(ground.parent);

        int arbolesColocados = 0;
        int intentos = 0;
        int maxIntentos = cantidadArboles * 10; // Para no congelar el editor si no hay espacio

        while (arbolesColocados < cantidadArboles && intentos < maxIntentos)
        {
            intentos++;

            // Muestreo con margen interno estricto
            float x = Random.Range(bounds.min.x + margenBorde, bounds.max.x - margenBorde);
            float z = Random.Range(bounds.min.z + margenBorde, bounds.max.z - margenBorde);

            // Descartar si cae dentro del campamento central
            if (Vector2.Distance(new Vector2(x, z), new Vector2(ground.position.x, ground.position.z)) < radioSeguroCentro)
                continue;

            Vector3 rayStart = new Vector3(x, bounds.max.y + 25f, z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 60f))
            {
                // Solo plantar si golpea la superficie del Ground (ignora rocas)
                if (hit.transform == ground)
                {
                    GameObject arbolElegido = modelos[Random.Range(0, totalTipos)];

                    Quaternion giroY = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    Quaternion correccion = Quaternion.Euler(rotacionAdicional);
                    Quaternion rotFinal = giroY * arbolElegido.transform.rotation * correccion;

                    GameObject nuevo = Instantiate(arbolElegido, hit.point, rotFinal, grupoBosque.transform);
                    nuevo.name = arbolElegido.name;

                    float escala = Random.Range(0.85f, 1.3f);
                    nuevo.transform.localScale = arbolElegido.transform.localScale * escala;

                    // Alinear la base exacta de la malla contra el suelo
                    Renderer[] renderers = nuevo.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        Bounds b = renderers[0].bounds;
                        for (int r = 1; r < renderers.Length; r++)
                        {
                            b.Encapsulate(renderers[r].bounds);
                        }

                        float desfaseY = hit.point.y - b.min.y;
                        nuevo.transform.position += Vector3.up * (desfaseY + ajusteAlturaY);
                    }

                    arbolesColocados++;
                }
            }
        }

        contenedorArboles.gameObject.SetActive(false);
        Debug.Log($"Bosque generado: {arbolesColocados} árboles colocados perfectamente dentro del terreno.");
    }

    [ContextMenu("2. Borrar Bosque Generado")]
    public void Limpiar()
    {
        GameObject grupoBosque = GameObject.Find("Bosque_Generado");
        if (grupoBosque != null) DestroyImmediate(grupoBosque);
        if (contenedorArboles != null) contenedorArboles.gameObject.SetActive(true);
    }
}