using System.Collections;
using UnityEngine;

public class GreenPlantHitParticles : MonoBehaviour
{
    [SerializeField] private int minCount = 5;
    [SerializeField] private int maxCount = 9;
    public Color MinColor = new Color(0.01f, 0.09f, 0.015f, 1f);
    public Color MaxColor = new Color(0.03f, 0.2f, 0.035f, 1f);
    [SerializeField] private float minLifetime = 0.9f;
    [SerializeField] private float maxLifetime = 1.4f;
    [SerializeField] private float minSpeed = 0.8f;
    [SerializeField] private float maxSpeed = 1.8f;
    [SerializeField] private float gravity = 0.9f;

    private static Mesh triangleMesh;
    private static Mesh squareMesh;
    private static Material sharedMaterial;

    private void Awake()
    {
        ParticleSystem particleSystem = GetComponent<ParticleSystem>();
        if (particleSystem != null)
        {
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Start()
    {
        EnsureAssets();
        int count = Random.Range(minCount, maxCount + 1);

        for (int i = 0; i < count; i++)
        {
            SpawnShape(i % 2 == 0 ? triangleMesh : squareMesh);
        }

        Destroy(gameObject, maxLifetime + 0.15f);
    }

    private void SpawnShape(Mesh mesh)
    {
        GameObject shape = new GameObject(mesh == triangleMesh ? "GreenTriangleParticle" : "GreenSquareParticle");
        shape.transform.SetParent(transform, false);
        shape.transform.localPosition = Vector3.zero;
        shape.transform.localRotation = Random.rotation;
        shape.transform.localScale = Vector3.one * Random.Range(1.4f, 2.2f);

        MeshFilter filter = shape.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = shape.AddComponent<MeshRenderer>();
        Material instanceMaterial = new Material(sharedMaterial);
        Color color = Color.Lerp(MinColor, MaxColor, Random.value);
        instanceMaterial.color = color;
        if (instanceMaterial.HasProperty("_BaseColor")) instanceMaterial.SetColor("_BaseColor", color);
        if (instanceMaterial.HasProperty("_Color")) instanceMaterial.SetColor("_Color", color);
        renderer.sharedMaterial = instanceMaterial;

        Vector3 direction = (transform.forward * 0.7f + Random.insideUnitSphere * 0.8f + Vector3.up * 0.8f).normalized;
        float speed = Random.Range(minSpeed, maxSpeed);
        float lifetime = Random.Range(minLifetime, maxLifetime);
        StartCoroutine(AnimateShape(shape.transform, instanceMaterial, direction * speed, lifetime));
    }

    private IEnumerator AnimateShape(Transform shape, Material material, Vector3 velocity, float lifetime)
    {
        float elapsed = 0f;
        Vector3 startScale = shape.localScale;

        while (elapsed < lifetime)
        {
            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            velocity += Vector3.down * gravity * deltaTime;

            shape.position += velocity * deltaTime;
            shape.Rotate(Random.Range(180f, 420f) * deltaTime, Random.Range(180f, 420f) * deltaTime, Random.Range(180f, 420f) * deltaTime);

            float normalizedTime = Mathf.Clamp01(elapsed / lifetime);
            shape.localScale = Vector3.Lerp(startScale, Vector3.zero, normalizedTime);

            Color color = material.color;
            color.a = 1f - normalizedTime;
            material.color = color;

            yield return null;
        }

        if (shape != null)
            Destroy(shape.gameObject);
    }

    private static void EnsureAssets()
    {
        if (triangleMesh == null)
            triangleMesh = CreateTriangleMesh();

        if (squareMesh == null)
            squareMesh = CreateSquareMesh();

        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");

            sharedMaterial = new Material(shader);
            sharedMaterial.color = new Color(0.02f, 0.14f, 0.025f, 1f);
            if (sharedMaterial.HasProperty("_BaseColor"))
                sharedMaterial.SetColor("_BaseColor", sharedMaterial.color);
            if (sharedMaterial.HasProperty("_Color"))
                sharedMaterial.SetColor("_Color", sharedMaterial.color);
            if (sharedMaterial.HasProperty("_Smoothness"))
                sharedMaterial.SetFloat("_Smoothness", 0.2f);
            if (sharedMaterial.HasProperty("_Metallic"))
                sharedMaterial.SetFloat("_Metallic", 0f);
        }
    }

    private static Mesh CreateTriangleMesh()
    {
        Mesh mesh = new Mesh { name = "RuntimeGreenTrianglePrism" };
        float depth = 0.035f;
        mesh.vertices = new[]
        {
            new Vector3(0f, 0.09f, -depth),
            new Vector3(-0.08f, -0.06f, -depth),
            new Vector3(0.08f, -0.06f, -depth),
            new Vector3(0f, 0.09f, depth),
            new Vector3(-0.08f, -0.06f, depth),
            new Vector3(0.08f, -0.06f, depth)
        };
        mesh.triangles = new[]
        {
            0, 1, 2,
            3, 5, 4,
            0, 3, 4, 0, 4, 1,
            1, 4, 5, 1, 5, 2,
            2, 5, 3, 2, 3, 0
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateSquareMesh()
    {
        Mesh mesh = new Mesh { name = "RuntimeGreenBox" };
        float half = 0.07f;
        float depth = 0.035f;
        mesh.vertices = new[]
        {
            new Vector3(-half, -half, -depth), new Vector3(half, -half, -depth),
            new Vector3(half, half, -depth), new Vector3(-half, half, -depth),
            new Vector3(-half, -half, depth), new Vector3(half, -half, depth),
            new Vector3(half, half, depth), new Vector3(-half, half, depth)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
