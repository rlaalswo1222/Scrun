using UnityEditor;
using UnityEngine;
using static ScrunEditorUtil;

// 잎 텍스처 한 장(2×2 칸, 알파 포함). 색은 거의 회색 명암만 담고 실제 색(초록·분홍·주황)은 메쉬 정점 색으로 입힌다
// → 한 장으로 모든 나무·덤불을 그림(그리기 호출·메모리 절약).
//  왼쪽 아래 = 흰색 꽉 찬 칸(정점 색만 쓰는 면, UV 0,0 근처) + 원거리 침엽수 실루엣(사진 가지)
//  오른쪽 아래 = 활엽수 잎 뭉치(사진 잎) / 왼쪽 위 = 침엽수 가지(사진 전나무 잔가지) / 오른쪽 위 = 깃털잎(야자·고사리, 코드로 그림)
// 사진 칸은 unity/tools/foliage_photo_atlas.py가 Poly Haven(CC0) fir_tree_01·tree_small_02로 합성한 PNG에서 복사한다.
public static class FoliageAtlas
{
    public const string Name = "Sty_FoliageAtlas";
    // 프로젝트 폴더 기준(Assets 밖: 원본 합성 이미지는 가져오지 않고 픽셀만 읽음)
    const string PhotoPath = "../tools/foliage_src/Sty_FoliagePhoto.png";
    const int Size = 2048, Q = Size / 2;

    public static readonly Rect Solid = new(0.02f, 0.02f, 0.14f, 0.14f);
    public static readonly Rect ConiferFar = new(0.25f, 0f, 0.25f, 0.5f);
    public static readonly Rect Leaf = new(0.5f, 0f, 0.5f, 0.5f);
    public static readonly Rect Needle = new(0f, 0.5f, 0.5f, 0.5f);
    public static readonly Rect Frond = new(0.5f, 0.5f, 0.5f, 0.5f);
    public static Vector2 SolidUV => Solid.center;

    static Color[] px;
    static System.Random rng;
    static float R() => (float)rng.NextDouble();

    public static Texture2D Build()
    {
        // 사진 칸(활엽수 잎·침엽수 가지·원거리 침엽수·흰 칸)은 합성 PNG를 그대로(PNG는 위가 0 → RawImage는 아래가 0으로 읽음)
        var photo = RawImage.Load(PhotoPath);
        if (photo.W != Size || photo.H != Size) throw new System.Exception($"{PhotoPath} 크기 {photo.W}x{photo.H} (2048 필요)");
        px = photo.Px;
        rng = new System.Random(4242);
        for (int y = Q; y < Size; y++)
            for (int x = Q; x < Size; x++) px[y * Size + x] = new Color(0, 0, 0, 0);
        FrondLeaf(Q, Q);

        var tex = SaveTexture(Name, Size, Size, px);
        var path = AssetDatabase.GetAssetPath(tex);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.alphaIsTransparency = true;
        ti.mipMapsPreserveCoverage = true;   // 멀리서(작은 밉) 잎이 녹아 사라지지 않게
        ti.alphaTestReferenceValue = 0.45f;
        ti.maxTextureSize = Size;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        px = null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------- 칠하기 도구 ----------
    // 덮어 칠하기(앞에 칠한 것 위로). a = 이 픽셀의 덮임(0~1)
    static void Put(int x, int y, Color c, float a)
    {
        if (a <= 0f || x < 0 || y < 0 || x >= Size || y >= Size) return;
        int i = y * Size + x;
        var d = px[i];
        float outA = a + d.a * (1f - a);
        if (outA <= 0f) return;
        var rgb = (new Color(c.r, c.g, c.b) * a + new Color(d.r, d.g, d.b) * d.a * (1f - a)) / outA;
        rgb.a = outA;
        px[i] = rgb;
    }

    // 잎 하나(아몬드 모양): 중심 c, 방향 dir, 길이 len, 폭 wid. 반쪽은 밝고 반쪽은 어둡게, 가장자리·잎맥은 진하게
    static void PaintLeaf(int ox, int oy, Vector2 c, Vector2 dir, float len, float wid, float value, float warm)
    {
        dir.Normalize();
        var side = new Vector2(-dir.y, dir.x);
        var a = c - dir * len * 0.5f;
        float reach = len * 0.5f + wid;
        int x0 = Mathf.FloorToInt(c.x - reach), x1 = Mathf.CeilToInt(c.x + reach);
        int y0 = Mathf.FloorToInt(c.y - reach), y1 = Mathf.CeilToInt(c.y + reach);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || y < 0 || x >= Q || y >= Q) continue;
                var p = new Vector2(x + 0.5f, y + 0.5f) - a;
                float t = Vector2.Dot(p, dir) / len;
                if (t < 0f || t > 1f) continue;
                float hw = wid * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)), 0.75f);
                float s = Vector2.Dot(p, side);
                float edge = hw - Mathf.Abs(s);   // px 단위 안쪽 거리
                if (edge < -1f) continue;
                float cover = Mathf.Clamp01(edge + 0.5f);
                float v = value * (s > 0 ? 1.08f : 0.9f);                  // 반쪽 명암
                v *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(edge / 2.2f));    // 테두리 선
                if (Mathf.Abs(s) < 0.7f && t > 0.08f && t < 0.85f) v *= 0.82f;   // 잎맥
                v = Mathf.Clamp01(v);
                Put(ox + x, oy + y, new Color(v * (1f + 0.06f * warm), v, v * (1f - 0.1f * warm)), cover);
            }
    }

    // 굵기 있는 선(잎자루)
    static void PaintLine(int ox, int oy, Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
    {
        var ab = b - a; float L = ab.magnitude; if (L < 0.5f) return;
        var dir = ab / L;
        float reach = Mathf.Max(w0, w1) + 1f;
        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach);
        int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || y < 0 || x >= Q || y >= Q) continue;
                var p = new Vector2(x + 0.5f, y + 0.5f) - a;
                float t = Mathf.Clamp01(Vector2.Dot(p, dir) / L);
                float d = (p - dir * t * L).magnitude;
                float w = Mathf.Lerp(w0, w1, t) * 0.5f;
                float cover = Mathf.Clamp01(w - d + 0.5f);
                if (cover <= 0f) continue;
                Put(ox + x, oy + y, Color.Lerp(c0, c1, t), cover);
            }
    }

    // ---------- 깃털잎(야자·고사리) ----------
    // u = 잎자루에서 끝. 양옆으로 긴 작은잎이 앞으로 비스듬히
    static void FrondLeaf(int ox, int oy)
    {
        float k = Q / 512f;   // 선 굵기는 512칸 기준으로 정했음
        float y0 = Q * 0.5f;
        var a = new Vector2(Q * 0.03f, y0); var b = new Vector2(Q * 0.97f, y0);
        for (float t = 0.08f; t < 0.98f; t += 0.022f)
        {
            var basePt = Vector2.Lerp(a, b, t);
            float prof = Mathf.Sin(Mathf.PI * Mathf.Lerp(0.12f, 1f, t));
            float len = Q * 0.36f * prof;
            for (int side = -1; side <= 1; side += 2)
            {
                float ang = (52f + 10f * R()) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), side * Mathf.Sin(ang));
                float v = (side > 0 ? 0.95f : 0.78f) * (0.88f + 0.16f * R());
                PaintLeaf(ox, oy, basePt + dir * len * 0.5f, dir, len, Mathf.Max(6f * k, len * 0.16f), v, 0.3f);
            }
        }
        PaintLine(ox, oy, a, b, 6f * k, 2.5f * k, new Color(0.62f, 0.6f, 0.45f), new Color(0.7f, 0.68f, 0.5f));
    }
}
