using UnityEngine.UIElements;

namespace Scrun.App
{
    // 코스 목록·상세가 같이 쓰는 조각(모양은 Course.uss)
    static class CourseParts
    {
        // 코스 그림 자리. 썸네일이 없으면(null) 기본 그림: 하늘색 칸 + 코스 이름 첫 글자(상준 그림이 오면 바꿈)
        public static VisualElement Thumb(Course c)
        {
            var thumb = new VisualElement();
            thumb.AddToClassList("course-thumb");
            if (string.IsNullOrEmpty(c.ThumbnailUrl))
            {
                var mark = new Label(CourseText.Initial(c.Name));
                mark.AddToClassList("course-thumb-mark");
                thumb.Add(mark);
            }
            // 썸네일 주소가 생기면 여기서 내려받아 배경으로(서버가 아직 null만 줌)
            return thumb;
        }

        // Chip/Live "● 지금 12명 달리는 중". 아무도 없으면 회색 칩
        public static VisualElement LiveChip(int runners)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("live-chip");
            chip.EnableInClassList("live-chip--idle", runners <= 0);
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("live-dot");
            chip.Add(dot);
            chip.Add(UiExtensions.Text(CourseText.RunningNow(runners), "live-text"));
            return chip;
        }
    }
}
