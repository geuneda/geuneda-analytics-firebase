using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Firebase
{
	/// <summary>
	/// Firebase Analytics 로 이벤트를 보내는 sink 입니다. 이름은 Firebase 규칙에 맞추고(맞출 수 없거나 예약 이름이면 그 이벤트·파라미터를 버리고 Warning 한 번),
	/// 파라미터는 25개, 문자열 값은 100자까지 보냅니다. 표준 이벤트는 Firebase 권장 이벤트(<c>purchase</c> 와 상품 목록, <c>ad_impression</c>)로 바꿉니다.
	/// </summary>
	/// <remarks>
	/// Firebase 초기화(<c>FirebaseApp.CheckAndFixDependenciesAsync</c>)는 게임이 하고, 끝나면 <see cref="MarkReady"/> 를 부릅니다. 그 전에 기록한 이벤트는 분석 서비스가 큐에 두었다가 보내고,
	/// 사용자 ID·속성은 이 sink 가 들고 있다가 준비되면 넘깁니다. 이벤트마다 기록할 때의 사용자 ID·속성을 SDK 에 맞춘 뒤 보내고, 배치가 끝나면 지금 상태로 되돌립니다
	/// (기다렸거나 저장했던 이벤트가 그 뒤에 로그인한 사용자에게 붙지 않게). 다시 보낸 이벤트의 시각은 Firebase 가 받은 시각입니다.
	/// </remarks>
	public sealed class FirebaseAnalyticsSink : IAnalyticsSink
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics.Firebase");

		private readonly IFirebaseAnalyticsApi _api;
		private readonly Func<AnalyticsEvent, bool> _handleEvent;
		private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<FirebaseParameter> _parameters = new List<FirebaseParameter>();

		// 분석 서비스가 알려 준 지금 사용자 상태(원래 이름)
		private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.Ordinal);
		private string _userId;

		// SDK 에 마지막으로 넘긴 상태(Firebase 이름, 자른 값)
		private readonly Dictionary<string, string> _sdkProperties = new Dictionary<string, string>(StringComparer.Ordinal);
		private bool _sdkUserIdKnown;
		private string _sdkUserId;
		private IReadOnlyDictionary<string, string> _sdkPropertiesSource;

		// 준비 전에 지우라고 한 속성(Firebase 이름). 이 sink 가 모르는 속성(이전 실행에서 SDK 에 남은 것)도 지우도록 MarkReady 때 넘깁니다
		private readonly HashSet<string> _pendingRemovals = new HashSet<string>(StringComparer.Ordinal);

		public FirebaseAnalyticsSink(FirebaseAnalyticsSinkOptions options = null) : this(options, new FirebaseAnalyticsApi())
		{
		}

		internal FirebaseAnalyticsSink(FirebaseAnalyticsSinkOptions options, IFirebaseAnalyticsApi api)
		{
			_api = api;
			_handleEvent = options?.HandleEvent;
		}

		public AnalyticsSinks Id => AnalyticsSinks.Firebase;

		public int MaxBatchSize => 50;

		public TimeSpan BatchDelay => TimeSpan.Zero;

		public bool IsReady { get; private set; }

		/// <summary>
		/// Firebase 초기화가 끝났음을 알립니다. 들고 있던 사용자 ID·속성을 넘긴 뒤 준비 상태가 되고, 분석 서비스가 1초 안에 기다리던 이벤트를 보냅니다.
		/// 넘기다 SDK 가 예외를 던지면 준비 상태가 되지 않으니 다시 부를 수 있습니다. 메인 스레드에서 부릅니다.
		/// </summary>
		public void MarkReady()
		{
			if (IsReady)
			{
				return;
			}

			foreach (var name in new List<string>(_pendingRemovals))
			{
				RemoveProperty(name);
				_pendingRemovals.Remove(name);
			}

			ApplyUser(_userId, _properties);
			IsReady = true;
		}

		public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			// SDK 호출은 동기라 배치 도중에는 취소되지 않습니다. 서비스를 이미 닫았으면 보내지 않고 저장해 두도록 재시도로 돌려줍니다
			if (cancellationToken.IsCancellationRequested)
			{
				return UniTask.FromResult(AnalyticsSendResult.Retry);
			}

			var completed = false;

			try
			{
				foreach (var analyticsEvent in events)
				{
					// 게임이 직접 보내는 이벤트도 기록할 때의 사용자에게 붙습니다
					ApplyUser(analyticsEvent.UserId, analyticsEvent.UserProperties);

					if (_handleEvent != null && _handleEvent(analyticsEvent))
					{
						continue;
					}

					Send(analyticsEvent);
				}

				completed = true;
			}
			finally
			{
				RestoreCurrentUser(completed);
			}

			return UniTask.FromResult(AnalyticsSendResult.Delivered);
		}

		public void SetUserId(string userId)
		{
			_userId = userId;

			if (IsReady)
			{
				ApplyUserId(userId);
			}
		}

		public void SetUserProperty(string name, string value)
		{
			if (value != null)
			{
				_properties[name] = value;
				_pendingRemovals.Remove(FirebaseNames.UserPropertyName(name) ?? string.Empty);

				if (IsReady)
				{
					ApplyUser(_userId, _properties);
				}

				return;
			}

			_properties.Remove(name);
			var propertyName = FirebaseNames.UserPropertyName(name);

			if (propertyName == null)
			{
				WarnOnce("property:" + name, $"Firebase user property name '{name}' is invalid or reserved; it is not sent");
				return;
			}

			// 지우라는 요청은 이 sink 가 그 속성을 모르더라도(이전 실행에서 남은 값) SDK 에 넘깁니다
			if (IsReady)
			{
				RemoveProperty(propertyName);
			}
			else
			{
				_pendingRemovals.Add(propertyName);
			}
		}

		private void RemoveProperty(string propertyName)
		{
			_sdkPropertiesSource = null;
			_api.SetUserProperty(propertyName, null);
			_sdkProperties.Remove(propertyName);
		}

		/// <summary>배치가 끝나면(예외로 끝나도) SDK 를 지금 사용자 상태로 되돌립니다. 예외로 끝난 경우 되돌리기 실패는 원래 예외를 가리지 않게 로그로만 남깁니다.</summary>
		private void RestoreCurrentUser(bool completed)
		{
			if (completed)
			{
				ApplyUser(_userId, _properties);
				return;
			}

			try
			{
				ApplyUser(_userId, _properties);
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Could not restore the current Firebase user after a failed batch");
			}
		}

		/// <summary>SDK 의 사용자 ID·속성을 <paramref name="userId"/>·<paramref name="properties"/> 로 맞춥니다. 바뀐 것만 넘기고, 없어진 속성은 지웁니다.</summary>
		private void ApplyUser(string userId, IReadOnlyDictionary<string, string> properties)
		{
			ApplyUserId(userId);

			// 분석 서비스는 속성이 바뀔 때만 새 사전을 만들므로 같은 사전이면 이미 맞춰져 있습니다
			if (ReferenceEquals(properties, _sdkPropertiesSource) && !ReferenceEquals(properties, _properties))
			{
				return;
			}

			// 중간에 실패하면 SDK 상태가 어느 사전과도 같지 않으므로 다시 비교하도록 지웁니다
			_sdkPropertiesSource = null;
			var target = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var property in properties)
			{
				var name = FirebaseNames.UserPropertyName(property.Key);

				if (name == null)
				{
					WarnOnce("property:" + property.Key, $"Firebase user property name '{property.Key}' is invalid or reserved; it is not sent");
					continue;
				}

				target[name] = FirebaseNames.Truncate(property.Value, FirebaseNames.MaxUserPropertyValueLength);
			}

			foreach (var name in new List<string>(_sdkProperties.Keys))
			{
				if (!target.ContainsKey(name))
				{
					_api.SetUserProperty(name, null);
					_sdkProperties.Remove(name);
				}
			}

			foreach (var property in target)
			{
				if (!_sdkProperties.TryGetValue(property.Key, out var current) || current != property.Value)
				{
					_api.SetUserProperty(property.Key, property.Value);
					_sdkProperties[property.Key] = property.Value;
				}
			}

			_sdkPropertiesSource = properties;
		}

		private void ApplyUserId(string userId)
		{
			if (_sdkUserIdKnown && _sdkUserId == userId)
			{
				return;
			}

			_api.SetUserId(userId);
			_sdkUserId = userId;
			_sdkUserIdKnown = true;
		}

		private void Send(AnalyticsEvent analyticsEvent)
		{
			_parameters.Clear();

			if (AnalyticsPurchase.TryRead(analyticsEvent, out var purchase))
			{
				AddPurchase(purchase);
				AddExtras(analyticsEvent, AnalyticsPurchase.IsStandardParameter);
				Log(AnalyticsStandardEvents.Purchase);
				return;
			}

			if (AnalyticsAdImpression.TryRead(analyticsEvent, out var impression))
			{
				AddAdImpression(impression);
				AddExtras(analyticsEvent, AnalyticsAdImpression.IsStandardParameter);
				Log(AnalyticsStandardEvents.AdImpression);
				return;
			}

			var name = FirebaseNames.EventName(analyticsEvent.Name);

			if (name == null)
			{
				WarnOnce("event:" + analyticsEvent.Name, $"Firebase event name '{analyticsEvent.Name}' is invalid or reserved; the event is not sent to Firebase");
				return;
			}

			AddExtras(analyticsEvent, _ => false);
			Log(name);
		}

		private void AddPurchase(AnalyticsPurchase purchase)
		{
			_parameters.Add(FirebaseParameter.Of("currency", purchase.Currency));
			_parameters.Add(FirebaseParameter.Of("value", purchase.Value));

			if (purchase.TransactionId != null)
			{
				_parameters.Add(FirebaseParameter.Of("transaction_id", FirebaseNames.Truncate(purchase.TransactionId, FirebaseNames.MaxStringValueLength)));
			}

			var item = new Dictionary<string, object>
			{
				{ "item_id", FirebaseNames.Truncate(purchase.ProductId, FirebaseNames.MaxStringValueLength) },
				{ "price", purchase.Price },
				{ "quantity", (long) purchase.Quantity }
			};

			if (purchase.ProductName != null)
			{
				item.Add("item_name", FirebaseNames.Truncate(purchase.ProductName, FirebaseNames.MaxStringValueLength));
			}

			_parameters.Add(FirebaseParameter.Items("items", new[] { item }));
		}

		private void AddAdImpression(AnalyticsAdImpression impression)
		{
			_parameters.Add(FirebaseParameter.Of(AnalyticsStandardParameters.AdPlatform, FirebaseNames.Truncate(impression.Platform, FirebaseNames.MaxStringValueLength)));
			AddOptional(AnalyticsStandardParameters.AdSource, impression.Source);
			AddOptional(AnalyticsStandardParameters.AdFormat, impression.Format);
			AddOptional(AnalyticsStandardParameters.AdUnitName, impression.UnitName);
			AddOptional(AnalyticsStandardParameters.AdPlacement, impression.Placement);
			_parameters.Add(FirebaseParameter.Of("value", impression.Revenue));
			_parameters.Add(FirebaseParameter.Of("currency", impression.Currency));
		}

		private void AddOptional(string name, string value)
		{
			if (value != null)
			{
				_parameters.Add(FirebaseParameter.Of(name, FirebaseNames.Truncate(value, FirebaseNames.MaxStringValueLength)));
			}
		}

		private void AddExtras(AnalyticsEvent analyticsEvent, Func<string, bool> skip)
		{
			foreach (var parameter in analyticsEvent.Parameters)
			{
				if (skip(parameter.Name))
				{
					continue;
				}

				var name = FirebaseNames.ParameterName(parameter.Name);

				if (name == null)
				{
					WarnOnce("parameter:" + parameter.Name, $"Firebase parameter name '{parameter.Name}' is invalid or reserved; it is not sent");
					continue;
				}

				if (Contains(name))
				{
					continue;
				}

				if (_parameters.Count >= FirebaseNames.MaxParameters)
				{
					WarnOnce("count:" + analyticsEvent.Name, $"Firebase event '{analyticsEvent.Name}' has more than {FirebaseNames.MaxParameters} parameters; the rest are not sent");
					return;
				}

				_parameters.Add(Convert(name, parameter.Value));
			}
		}

		private bool Contains(string name)
		{
			foreach (var parameter in _parameters)
			{
				if (parameter.Name == name)
				{
					return true;
				}
			}

			return false;
		}

		private static FirebaseParameter Convert(string name, AnalyticsValue value)
		{
			switch (value.Type)
			{
				case AnalyticsValueType.Integer:
					return FirebaseParameter.Of(name, value.IntegerValue);
				case AnalyticsValueType.Float:
					return FirebaseParameter.Of(name, value.FloatValue);
				case AnalyticsValueType.Boolean:
					return FirebaseParameter.Of(name, value.BooleanValue ? 1L : 0L);
				default:
					return FirebaseParameter.Of(name, FirebaseNames.Truncate(value.StringValue, FirebaseNames.MaxStringValueLength));
			}
		}

		private void Log(string name)
		{
			_api.LogEvent(name, _parameters.ToArray());
			_parameters.Clear();
		}

		private void WarnOnce(string key, string message)
		{
			if (_warned.Add(key))
			{
				Logger.Warning(message);
			}
		}
	}
}
