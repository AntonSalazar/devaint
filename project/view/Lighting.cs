using Godot;

/// <summary>Свет сцены: окружение с рассеянным светом и солнце с тенями.</summary>
public static class Lighting
{
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

    /// <summary>Солнце сверху и сбоку с тенями.</summary>
    public static DirectionalLight3D CreateSun()
    {
        return new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55, 30, 0),
            ShadowEnabled = true,
            LightEnergy = 1.1f,
        };
    }
}
