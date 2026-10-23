using Godot;

/// <summary>Свет сцены: окружение с рассеянным светом и солнце с тенями.</summary>
public static class Lighting
{
    /// <summary>Дальность теней солнца: камера в 20 м от персонажа плюс глубина кадра.</summary>
    public const float ShadowDistance = 30f;

    /// <summary>Окружение: тёмный фон и мягкий рассеянный свет.</summary>
    public static WorldEnvironment CreateEnvironment()
    {
        return new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.08f, 0.09f, 0.11f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.78f, 0.85f),
                AmbientLightEnergy = 0.6f,
            },
        };
    }

    /// <summary>Солнце сверху и сбоку с тенями, настроенными под ортографическую камеру.</summary>
    public static DirectionalLight3D CreateSun()
    {
        // Смещения теней задаются в точках карты, поэтому тонкая стена 10 см держит свет, только если
        // точка карты мелкая. По умолчанию тени растянуты на 100 м и разбиты по дальности для
        // перспективы; у ортографической камеры кадр на одной дальности, хватает одной карты на 30 м.
        // Проверено снимками в углу комнаты при солнце утром, днём, в зените, вечером и с севера:
        // солнце будет двигаться, настройки не должны зависеть от его положения.
        return new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55, 30, 0),
            ShadowEnabled = true,
            LightEnergy = 1.1f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = ShadowDistance,
            ShadowBias = 0.05f,
            ShadowNormalBias = 1.0f,
        };
    }
}
