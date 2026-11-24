using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 在作品（例如 group3 / 主視覺）周圍產生飄散的發光粒子。
///
/// 用法：把這個元件加到作品物件上。編輯器會自動建立子物件
/// "ArtworkParticles (auto)"，你可以直接改 Inspector 的參數，
/// 再按元件右上角 ⋮ →「Rebuild Particles」重新套用。
///
/// 為什麼要用子物件 + ScalingMode.Shape：
/// 作品本身是一個被壓扁的 Cube（scale ≈ 3.39 x 5.085 x 0.0034）。
/// 子物件會繼承這個縮放，所以發射範圍剛好貼合畫框；
/// Shape 模式只把縮放套用到「發射形狀」，粒子本身不會被壓扁。
/// </summary>
[DisallowMultipleComponent]
public class ArtworkParticles : MonoBehaviour
{
    public enum EmitArea { Edges, Surface }

    const string ChildName = "ArtworkParticles (auto)";
    const string DefaultMaterialPath = "Assets/ArtworkParticles/ArtworkGlowParticle.mat";

    [Header("外觀")]
    public Material particleMaterial;
    [Tooltip("Edges = 從畫框邊緣飄出；Surface = 從整個畫面浮出")]
    public EmitArea emitArea = EmitArea.Edges;
    public Color colorA = new Color(1f, 0.85f, 0.55f, 1f);   // 暖金
    public Color colorB = new Color(0.7f, 0.85f, 1f, 1f);    // 淡藍
    [Range(0.005f, 0.15f)] public float particleSize = 0.035f;

    [Header("數量與運動")]
    [Range(1f, 200f)] public float emissionRate = 40f;
    [Range(10, 1000)] public int maxParticles = 250;
    [Range(0.5f, 10f)] public float lifetime = 4f;
    [Tooltip("往上飄的速度 (m/s)")]
    public float driftUpSpeed = 0.08f;
    [Tooltip("隨機擾動的強度，越大越像被風吹")]
    public float noiseStrength = 0.15f;

    void Awake()
    {
        // 執行時如果子物件不存在（例如在 Play 模式才加上元件），就即時建立
        if (Application.isPlaying && transform.Find(ChildName) == null)
            Build();
    }

#if UNITY_EDITOR
    // 在編輯器中「加上元件」時呼叫
    void Reset()
    {
        particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        Build();
    }
#endif

    [ContextMenu("Rebuild Particles")]
    public void Build()
    {
        var old = transform.Find(ChildName);
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }

        var go = new GameObject(ChildName);
        go.transform.SetParent(transform, false); // local identity → 貼合作品
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Build Artwork Particles");
#endif

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ConfigureMain(ps);
        ConfigureShape(ps);
        ConfigureMotion(ps);
        ConfigureLifetimeLook(ps);
        ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>());

        ps.Play();
    }

    void ConfigureMain(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;                 // group 切換啟用時，粒子立刻是滿的，不會從零長出來
        main.playOnAwake = true;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.5f, particleSize * 1.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = maxParticles;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = emissionRate;
    }

    void ConfigureShape(ParticleSystem ps)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = emitArea == EmitArea.Edges
            ? ParticleSystemShapeType.BoxEdge
            : ParticleSystemShapeType.Box;
        shape.scale = Vector3.one;           // 1x1x1 = 原始 Cube 大小，再乘上父物件縮放
        shape.randomDirectionAmount = 1f;    // 往四面八方散開
    }

    void ConfigureMotion(ParticleSystem ps)
    {
        // 三個軸必須是同一種曲線模式（都用 TwoConstants），否則 Unity 會報錯
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(driftUpSpeed * 0.4f, driftUpSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var noise = ps.noise;
        noise.enabled = noiseStrength > 0f;
        noise.strength = noiseStrength;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.25f;
        noise.quality = ParticleSystemNoiseQuality.Medium; // Quest 上效能友善
    }

    /// <summary>
    /// 粒子的「呼吸感」：一顆光點從出生到消失，亮度與大小怎麼變化。
    /// 這裡決定了整體氛圍 — 想要更夢幻、更閃爍，主要改這裡。
    /// </summary>
    void ConfigureLifetimeLook(ParticleSystem ps)
    {
        // 淡入 → 維持 → 淡出（避免粒子突然出現/消失）
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0.8f, 0.7f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 長大 → 慢慢縮小
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.3f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f, 0.2f));
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    void ConfigureRenderer(ParticleSystemRenderer r)
    {
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        var mat = particleMaterial;
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) mat = new Material(shader);
            else Debug.LogWarning($"[ArtworkParticles] {name}: 沒有指定 particleMaterial，也找不到 URP 粒子 shader。", this);
        }
        r.sharedMaterial = mat;
    }
}
