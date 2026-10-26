using Godot;

/// <summary>Общий материал стен и объектов со срезом и проверки, закрывают ли кусок стены или объект персонажа.</summary>
public static class Cutaway
{
    /// <summary>Путь шейдера среза.</summary>
    public const string ShaderPath = "res://view/wall_cutaway.gdshader";

    /// <summary>Высота тела, которую должна закрыть стена, чтобы её резать.</summary>
    public const float BodyHeight = 1.7f;

    /// <summary>Полуширина тела поперёк взгляда: лучи к камере идут от центра и от обоих краёв.</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Насколько персонаж должен отойти от плоскости куска, чтобы считаться за ним: в дверном проёме срез не мигает.</summary>
    public const float SideEpsilon = 0.05f;

    /// <summary>Длина луча к камере при проверке объекта: дальше камеры изометрии.</summary>
    private const float RayLength = 30f;

    /// <summary>Высоты над ногами, от которых идут лучи к камере при проверке объекта: ноги, пояс и голова.</summary>
    private static readonly float[] _rayHeights = [0.1f, BodyHeight / 2, BodyHeight - 0.1f];

    /// <summary>Материал стен, один на все чанки; какие куски опущены, задают данные экземпляров.</summary>
    public static ShaderMaterial Material { get; } = new() { Shader = GD.Load<Shader>(ShaderPath) };

    /// <summary>Закрывает ли кусок стены тело персонажа от камеры: хоть один из лучей от центра и краёв тела к камере проходит через кусок.</summary>
    public static bool Occludes(Transform3D piece, Vector3 feet, Vector3 back)
    {
        // Лучи к камере от центра тела и от его краёв поперёк взгляда: кусок режется, если хоть один
        // луч проходит через него. Запас вдоль стены тут не годится: конец куска рядом с телом
        // бывает позади него, и тогда тело он не закрывает
        Vector3 side = new Vector3(back.Z, 0, -back.X).Normalized() * BodyRadius;
        return Hits(piece, feet, back) || Hits(piece, feet + side, back) || Hits(piece, feet - side, back);
    }

    /// <summary>Закрывает ли бокс объекта тело от камеры: хоть один луч к камере от ног, пояса или головы, от центра или краёв тела проходит через бокс.</summary>
    public static bool OccludesBox(Transform3D box, Vector3 feet, Vector3 back)
    {
        // Бокс объекта не повёрнут, только растянут: его границы - центр плюс-минус половина масштаба
        Vector3 size = box.Basis.Scale.Abs();
        Aabb bounds = new(box.Origin - (size / 2), size);
        Vector3 side = new Vector3(back.Z, 0, -back.X).Normalized() * BodyRadius;
        foreach (float height in _rayHeights)
        {
            for (int across = -1; across <= 1; across++)
            {
                Vector3 from = feet + (side * across) + (Vector3.Up * height);
                if (bounds.IntersectsSegment(from, from + (back.Normalized() * RayLength)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Проходит ли луч к камере от точки на полу через кусок стены, считая рост тела над ней.</summary>
    private static bool Hits(Transform3D piece, Vector3 feet, Vector3 back)
    {
        // Кусок - единичный бокс, растянутый матрицей: X - длина, Y - высота, Z - толщина, начало - центр.
        // Луч от макушки идёт выше луча от ног на рост: подъём вдоль стены не меняет расстояние до её плоскости
        Vector3 length = piece.Basis.X;
        Vector3 height = piece.Basis.Y;
        Vector3 normal = piece.Basis.Z.Normalized();
        float toward = back.Dot(normal);
        float distance = (piece.Origin - feet).Dot(normal);
        if (Mathf.Abs(toward) < 1e-5f || Mathf.Abs(distance) < SideEpsilon || distance / toward <= 0)
        {
            return false;
        }

        Vector3 hit = feet + (back * (distance / toward)) - piece.Origin;
        float along = Mathf.Abs(hit.Dot(length) / length.Length());
        float up = hit.Dot(height) / height.Length();
        float half = height.Length() / 2;
        return along <= length.Length() / 2 && up <= half && up + BodyHeight >= -half;
    }
}
