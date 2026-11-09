using System.Runtime.CompilerServices;

// 테스트가 Session.SetTokens 같은 내부 메서드로 상황을 만들 수 있게, 에디터 창이 저장된 로그인을 지울 수 있게
[assembly: InternalsVisibleTo("Scrun.App.Tests")]
[assembly: InternalsVisibleTo("Scrun.App.Editor")]
