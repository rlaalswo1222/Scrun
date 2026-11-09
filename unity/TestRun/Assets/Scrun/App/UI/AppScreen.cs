using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 앱 화면 하나. UXML(Resources 아래)을 불러 Root를 만들고, Navigator가 보이고 숨기는 시점을 알려 준다.
    //   public sealed class CourseListScreen : AppScreen {
    //       protected override string Uxml => "UI/CourseList";            // Assets/Scrun/App/Resources/UI/CourseList.uxml
    //       protected override void OnCreate() => Q<Button>("enter").clicked += () => Nav.Push(new CourseDetailScreen());
    //   }
    public abstract class AppScreen
    {
        public VisualElement Root { get; private set; }
        public Navigator Nav { get; private set; }

        // Resources 기준 UXML 경로(확장자 없이)
        protected abstract string Uxml { get; }

        // 상세·흐름 화면처럼 아래 탭을 숨길지
        public virtual bool HidesTabBar => false;

        // 달리기 화면처럼 가로로 돌릴지(나머지는 세로 고정)
        public virtual bool Landscape => false;

        internal void Create(Navigator nav)
        {
            Nav = nav;
            var tree = Resources.Load<VisualTreeAsset>(Uxml)
                       ?? throw new InvalidOperationException($"UXML을 찾지 못함: Resources/{Uxml}.uxml ({GetType().Name})");
            Root = tree.Instantiate();
            Root.AddToClassList("screen-container");
            OnCreate();
        }

        // 처음 한 번(버튼 연결 등)
        protected virtual void OnCreate() { }
        // 맨 위에 보일 때마다(다시 돌아올 때도)
        protected internal virtual void OnShow() { }
        // 다른 화면에 덮이거나 탭을 바꿀 때
        protected internal virtual void OnHide() { }
        // 뒤로 가서 없어질 때(구독 해제 등)
        protected internal virtual void OnDestroy() { }

        // 이름으로 찾되, 없으면 바로 알려 준다(UXML과 코드가 어긋난 것을 일찍 발견)
        protected T Q<T>(string name) where T : VisualElement =>
            Root.Q<T>(name) ?? throw new InvalidOperationException($"{Uxml}.uxml에 '{name}'({typeof(T).Name})이 없음");
    }
}
