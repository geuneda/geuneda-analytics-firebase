using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Firebase
{
	/// <summary><see cref="FirebaseAnalyticsSink"/> 설정입니다.</summary>
	public sealed class FirebaseAnalyticsSinkOptions
	{
		/// <summary>
		/// 게임이 이벤트를 직접 보낼 때 씁니다. true 를 돌려주면 이 sink 는 그 이벤트를 보내지 않습니다(예: 특정 이벤트를 다른 Firebase 이벤트로 바꿔 보냄).
		/// 메인 스레드에서 이벤트마다 불립니다. null 이면 모든 이벤트를 기본 변환으로 보냅니다.
		/// </summary>
		public Func<AnalyticsEvent, bool> HandleEvent { get; set; }
	}
}
