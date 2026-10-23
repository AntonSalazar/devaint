using Godot;

/// <summary>Тесты настроек света <see cref="Lighting"/>.</summary>
public class LightingTest : TestCase
{
    /// <summary>Тени солнца одной картой на дальность, которая покрывает камеру и кадр, с маленькими смещениями.</summary>
    public void TestSunShadowsFitOrthographicCamera()
    {
        DirectionalLight3D sun = Lighting.CreateSun();

        Check(sun.ShadowEnabled, "shadows are on");
        CheckEqual(DirectionalLight3D.ShadowMode.Orthogonal, sun.DirectionalShadowMode, "single shadow map");
        Check(sun.DirectionalShadowMaxDistance >= IsoCamera.Distance + 9f, $"shadow distance {sun.DirectionalShadowMaxDistance} covers the camera distance and the frame");
        Check(sun.DirectionalShadowMaxDistance <= 40f, $"shadow distance {sun.DirectionalShadowMaxDistance} is small enough for fine shadow texels");
        Check(sun.ShadowNormalBias <= 1.0f, $"normal bias {sun.ShadowNormalBias} is small enough not to leak through thin walls");
        sun.Free();
    }
}
