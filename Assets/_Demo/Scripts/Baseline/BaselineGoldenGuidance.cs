using System.Collections.Generic;
using UnityEngine;

// World-space gold wisps follow an already collision-checked route.
public sealed class BaselineGoldenGuidance : MonoBehaviour
{
    const int Capacity = 96;
    readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[Capacity];
    readonly List<Vector3> points = new List<Vector3>(160);
    readonly List<float> distance = new List<float>(160);
    ParticleSystem system;
    Material material;
    float length;
    public int VisibleParticleCount { get; private set; }
    public void SetPath(Vector3[] path)
    {
        points.Clear(); distance.Clear(); length = 0;
        if (path != null)
            foreach (var point in path)
            {
                if (points.Count > 0)
                {
                    var step = Vector3.Distance(points[points.Count - 1], point);
                    if (step < .02f) continue;
                    length += step;
                }
                points.Add(point); distance.Add(length);
                if (length >= 32) break;
            }
        if (points.Count < 2 || length < .1f) ClearParticles();
    }
    public void Clear() { points.Clear(); distance.Clear(); length = 0; ClearParticles(); }
    void Awake()
    {
        system = gameObject.AddComponent<ParticleSystem>();
        var main = system.main;
        main.playOnAwake = false; main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Capacity; main.startSpeed = 0; main.startLifetime = 2;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = false;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var shader = Resources.Load<Shader>("GoldenGuidance");
        if (shader)
        {
            material = new Material(shader) { name = "Golden guidance runtime" };
            renderer.sharedMaterial = material;
        }
        system.Play();
    }
    void LateUpdate()
    {
        if (!system || points.Count < 2 || Time.timeScale <= 0) { ClearParticles(); return; }
        int count = Mathf.Min(Capacity, Mathf.CeilToInt(Mathf.Min(length, 30) * 3));
        float span = Mathf.Min(length, 30);
        float time = Time.time;
        int segment = 1;
        for (int i = 0; i < count; i++)
        {
            float along = Mathf.Repeat(i * .37f + time * 2.7f, span);
            // Particle ordering wraps, so locate its actual segment independently.
            segment = 1;
            while (segment < distance.Count - 1 && distance[segment] < along) segment++;
            float t = Mathf.InverseLerp(distance[segment - 1], distance[segment], along);
            Vector3 direction = (points[segment] - points[segment - 1]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 p = Vector3.Lerp(points[segment - 1], points[segment], t);
            float seed = i * 2.39996f;
            p += Vector3.up * (.28f + .13f * Mathf.Sin(time * 2 + seed));
            p += side * (.12f * Mathf.Sin(time * 1.3f + seed));
            float fade = Mathf.Clamp01(along / 1.4f) * Mathf.Clamp01((span - along) / 2f);
            float twinkle = .6f + .4f * Mathf.Sin(seed + time * 3);
            particles[i] = new ParticleSystem.Particle
            {
                position = p, velocity = Vector3.zero,
                startLifetime = 2, remainingLifetime = 2,
                startSize = (i % 9 == 0 ? .18f : .09f) * (.7f + .3f * twinkle),
                startColor = new Color(1f, .72f + .14f * twinkle, .21f, fade * (.45f + .4f * twinkle)),
                randomSeed = (uint)i + 1
            };
        }
        system.SetParticles(particles, count);
        VisibleParticleCount = count;
    }
    void ClearParticles() { if (system) system.Clear(true); VisibleParticleCount = 0; }
    void OnDisable() { ClearParticles(); }
    void OnDestroy() { if (material) Destroy(material); }
}
