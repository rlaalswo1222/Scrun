using UnityEngine;

namespace Scrun
{
    // 도로망 위 코스 하나를 트랙으로. 러너·봇·고스트는 이 거리만 주고받는다.
    // 코스를 바꾸면(SetRoute) 같은 도로망 위 다른 경로를 달린다.
    [ExecuteAlways]
    public class RouteTrack : Track
    {
        [SerializeField] RoadNetwork network;
        [SerializeField] string routeId;

        Polyline line;

        public string RouteId => routeId;
        public string RouteName => network ? network.FindRoute(routeId)?.name : null;

        public void Init(RoadNetwork net, string route)
        {
            network = net;
            routeId = route;
            line = null;
        }

        public void SetRoute(string route)
        {
            routeId = route;
            line = null;
        }

        void OnEnable() => line = null;
        void OnValidate() => line = null;

        Polyline Line => line ??= network.BuildRoute(routeId);

        public override float LapLength => Line.Length;
        public override Vector3 GetPoint(float distance) => Line.GetPoint(distance);
        public override Vector3 GetForward(float distance) => Line.GetForward(distance);
    }
}
