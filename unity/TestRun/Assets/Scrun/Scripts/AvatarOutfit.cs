using UnityEngine;

namespace Scrun
{
    // 새 아바타 러닝복 6세트(노션 BL-34, 계획 0067). 아바타 v10 FBX 안에 옷 메시 Outfit_L1–L6이 같은 뼈대로 들어 있고,
    // 레벨에 맞는 하나만 켠다. 몸·옷 6벌이 팔레트 한 장(재질 하나)을 같이 써서 갈아입어도 그리기 호출이 안 늘어남.
    //   Lv.1 기본 러닝복 → 2 기능성 → 3 스포츠(헤어밴드·손목밴드) → 4 퍼포먼스(싱글렛·러닝 캡) → 5 레이스(번호표 앞뒤) → 6 엘리트
    // 레벨은 서버 값을 그대로 넣는다(내 레벨 = 마이·결과 화면의 레벨, 다른 러너 = RemoteRunner.Level). 러닝 중에는 바꾸지 않는다(BL-34 ④)
    [DisallowMultipleComponent]
    public class AvatarOutfit : MonoBehaviour
    {
        public const int MinLevel = 1, MaxLevel = 6;

        [SerializeField, Range(MinLevel, MaxLevel)] int level = MinLevel;

        Renderer[] outfits;   // 0 = Lv.1

        public int Level
        {
            get => level;
            set { level = Mathf.Clamp(value, MinLevel, MaxLevel); Apply(); }
        }

        void Awake() => Apply();

        // 레벨 모름(null)이나 범위 밖이면 Lv.1(기본 러닝복). Lv.6 넘는 레벨도 엘리트 그대로(BL-34 ⑥ 배지 프레임으로만 구분)
        public void SetLevel(int? serverLevel) => Level = serverLevel ?? MinLevel;

        void Apply()
        {
            if (outfits == null || outfits.Length != MaxLevel || System.Array.Exists(outfits, r => r == null)) Find();
            for (int i = 0; i < outfits.Length; i++)
                if (outfits[i]) outfits[i].enabled = i == level - 1;
        }

        void Find()
        {
            outfits = new Renderer[MaxLevel];
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name;
                if (n.Length == 9 && n.StartsWith("Outfit_L") && n[8] >= '1' && n[8] <= '6')
                    outfits[n[8] - '1'] = r;
            }
        }

#if UNITY_EDITOR
        void OnValidate() => Apply();
#endif
    }
}
