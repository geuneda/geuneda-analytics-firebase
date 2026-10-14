using System.Collections.Generic;
using Firebase.Analytics;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Firebase
{
	/// <summary>Firebase 로 보낼 파라미터 하나입니다. 테스트가 SDK 없이 변환 결과를 확인하도록 SDK 형식과 분리했습니다.</summary>
	internal readonly struct FirebaseParameter
	{
		private FirebaseParameter(string name, object value)
		{
			Name = name;
			Value = value;
		}

		public string Name { get; }

		/// <summary><c>string</c>, <c>long</c>, <c>double</c>, 또는 상품 목록(<see cref="IReadOnlyList{T}"/> of 사전)</summary>
		public object Value { get; }

		public static FirebaseParameter Of(string name, string value)
		{
			return new FirebaseParameter(name, value);
		}

		public static FirebaseParameter Of(string name, long value)
		{
			return new FirebaseParameter(name, value);
		}

		public static FirebaseParameter Of(string name, double value)
		{
			return new FirebaseParameter(name, value);
		}

		public static FirebaseParameter Items(string name, IReadOnlyList<Dictionary<string, object>> items)
		{
			return new FirebaseParameter(name, items);
		}
	}

	/// <summary>Firebase Analytics SDK 호출입니다. 테스트는 기록하는 구현으로 바꿉니다.</summary>
	internal interface IFirebaseAnalyticsApi
	{
		void LogEvent(string name, IReadOnlyList<FirebaseParameter> parameters);

		void SetUserId(string userId);

		void SetUserProperty(string name, string value);
	}

	/// <summary>실제 <c>FirebaseAnalytics</c> 를 부릅니다.</summary>
	internal sealed class FirebaseAnalyticsApi : IFirebaseAnalyticsApi
	{
		public void LogEvent(string name, IReadOnlyList<FirebaseParameter> parameters)
		{
			var converted = new Parameter[parameters.Count];

			for (var i = 0; i < parameters.Count; i++)
			{
				converted[i] = ToParameter(parameters[i]);
			}

			FirebaseAnalytics.LogEvent(name, converted);
		}

		public void SetUserId(string userId)
		{
			FirebaseAnalytics.SetUserId(userId);
		}

		public void SetUserProperty(string name, string value)
		{
			FirebaseAnalytics.SetUserProperty(name, value);
		}

		private static Parameter ToParameter(FirebaseParameter parameter)
		{
			switch (parameter.Value)
			{
				case long integer:
					return new Parameter(parameter.Name, integer);
				case double number:
					return new Parameter(parameter.Name, number);
				case IReadOnlyList<Dictionary<string, object>> items:
					var list = new List<IDictionary<string, object>>(items.Count);

					foreach (var item in items)
					{
						list.Add(item);
					}

					return new Parameter(parameter.Name, list);
				default:
					return new Parameter(parameter.Name, (string) parameter.Value);
			}
		}
	}
}
