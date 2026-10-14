using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Geuneda.Analytics.Firebase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Firebase.Tests
{
	public sealed class FirebaseAnalyticsSinkTest
	{
		private static readonly AnalyticsAppInfo App = new AnalyticsAppInfo("i", "1.0", "android", "dev", "d", "o");

		private FakeApi _api;

		[SetUp]
		public void SetUp()
		{
			_api = new FakeApi();
		}

		private FirebaseAnalyticsSink Sink(Func<AnalyticsEvent, bool> handle = null)
		{
			var sink = new FirebaseAnalyticsSink(new FirebaseAnalyticsSinkOptions { HandleEvent = handle }, _api);
			sink.MarkReady();
			return sink;
		}

		private static AnalyticsEvent Event(string name, params (string Name, object Value)[] parameters)
		{
			return UserEvent(name, null, null, parameters);
		}

		private static AnalyticsEvent UserEvent(string name, string userId, IReadOnlyDictionary<string, string> properties,
			params (string Name, object Value)[] parameters)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name,
				parameters.Select(p => new AnalyticsParameter(p.Name, AnalyticsValue.FromObject(p.Value))).ToArray(), DateTime.UtcNow, false, 1, null, userId,
				properties, App);
		}

		private static void Send(FirebaseAnalyticsSink sink, params AnalyticsEvent[] events)
		{
			Assert.AreEqual(AnalyticsSendResult.Delivered, sink.SendAsync(events, CancellationToken.None).GetAwaiter().GetResult());
		}

		[Test]
		public void GenericEvent_KeepsValueTypes()
		{
			Send(Sink(), Event("stage_clear", ("stage", 3), ("ratio", 0.5), ("win", true), ("mode", "hard")));

			var (name, parameters, _, _) = _api.Events.Single();
			Assert.AreEqual("stage_clear", name);
			Assert.AreEqual(3L, parameters["stage"]);
			Assert.AreEqual(0.5, parameters["ratio"]);
			Assert.AreEqual(1L, parameters["win"]);
			Assert.AreEqual("hard", parameters["mode"]);
		}

		[Test]
		public void Names_AreSanitizedOrDropped()
		{
			var sink = Sink();

			// LogAssert 는 순서대로 맞춥니다
			LogAssert.Expect(LogType.Warning, new Regex("parameter name 'ga_id' is invalid or reserved"));
			LogAssert.Expect(LogType.Warning, new Regex("event name 'session_start' is invalid or reserved"));
			LogAssert.Expect(LogType.Warning, new Regex("event name '1st' is invalid or reserved"));
			LogAssert.Expect(LogType.Warning, new Regex("event name 'firebase_x' is invalid or reserved"));
			Send(sink, Event("level-up.ok", ("ga_id", 1), ("my key", 2)), Event("session_start"), Event("1st"), Event("firebase_x"),
				Event(new string('a', 41)), Event("session_start"));

			Assert.AreEqual(1, _api.Events.Count);
			Assert.AreEqual("level_up_ok", _api.Events[0].Name);
			CollectionAssert.AreEquivalent(new[] { "my_key" }, _api.Events[0].Parameters.Keys);
		}

		[Test]
		public void Limits_ParameterCountAndValueLength()
		{
			var parameters = Enumerable.Range(0, 30).Select(i => ("p" + i, (object) i)).Append(("text", new string('x', 150))).ToArray();

			LogAssert.Expect(LogType.Warning, new Regex("more than 25 parameters"));
			Send(Sink(), Event("big", parameters), Event("long", ("text", new string('y', 150))));

			Assert.AreEqual(25, _api.Events[0].Parameters.Count);
			Assert.AreEqual(100, ((string) _api.Events[1].Parameters["text"]).Length);
		}

		[Test]
		public void Purchase_BecomesFirebasePurchaseWithItems()
		{
			var purchase = Event(AnalyticsStandardEvents.Purchase, ("product_id", "gem_100"), ("price", 1.5), ("quantity", 2), ("value", 3.0),
				("currency", "USD"), ("transaction_id", "tx"), ("product_name", "Gems"), ("player_level", 9));

			Send(Sink(), purchase);

			var (name, parameters, _, _) = _api.Events.Single();
			Assert.AreEqual("purchase", name);
			Assert.AreEqual("USD", parameters["currency"]);
			Assert.AreEqual(3.0, parameters["value"]);
			Assert.AreEqual("tx", parameters["transaction_id"]);
			Assert.AreEqual(9L, parameters["player_level"]);
			var item = ((IReadOnlyList<Dictionary<string, object>>) parameters["items"]).Single();
			Assert.AreEqual("gem_100", item["item_id"]);
			Assert.AreEqual("Gems", item["item_name"]);
			Assert.AreEqual(1.5, item["price"]);
			Assert.AreEqual(2L, item["quantity"]);
			Assert.IsFalse(parameters.ContainsKey("product_id"), "the product goes in items");
		}

		[Test]
		public void AdImpression_BecomesFirebaseAdImpression()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.AdImpression, ("ad_platform", "AppLovin"), ("ad_source", "AdMob"), ("ad_format", "rewarded"),
				("ad_unit_name", "u1"), ("value", 0.002), ("currency", "USD")));

			var (name, parameters, _, _) = _api.Events.Single();
			Assert.AreEqual("ad_impression", name);
			Assert.AreEqual("AppLovin", parameters["ad_platform"]);
			Assert.AreEqual("AdMob", parameters["ad_source"]);
			Assert.AreEqual("rewarded", parameters["ad_format"]);
			Assert.AreEqual("u1", parameters["ad_unit_name"]);
			Assert.AreEqual(0.002, parameters["value"]);
		}

		[Test]
		public void HandleEvent_LetsTheGameSendItself()
		{
			var handled = new List<string>();
			Send(Sink(e =>
			{
				if (e.Name != "special")
				{
					return false;
				}

				handled.Add(e.Name);
				return true;
			}), Event("special"), Event("normal"));

			CollectionAssert.AreEqual(new[] { "special" }, handled);
			CollectionAssert.AreEqual(new[] { "normal" }, _api.Events.Select(e => e.Name));
		}

		[Test]
		public void UserInfo_IsHeldUntilReady()
		{
			var sink = new FirebaseAnalyticsSink(null, _api);

			Assert.IsFalse(sink.IsReady);
			sink.SetUserId("u1");
			sink.SetUserProperty("tier", new string('g', 40));
			sink.SetUserProperty("tier", "gold");
			Assert.AreEqual(0, _api.UserIds.Count + _api.UserProperties.Count);

			sink.MarkReady();

			CollectionAssert.AreEqual(new[] { "u1" }, _api.UserIds);
			CollectionAssert.AreEqual(new[] { ("tier", "gold") }, _api.UserProperties);

			sink.SetUserId(null);
			sink.SetUserProperty("tier", new string('s', 40));
			sink.SetUserProperty("tier", null);
			Assert.AreEqual(null, _api.UserIds[1]);
			Assert.AreEqual(36, _api.UserProperties[1].Value.Length);
			Assert.AreEqual(("tier", (string) null), _api.UserProperties[2]);
		}

		[Test]
		public void Events_AreAttributedToTheUserAtLogTime()
		{
			var sink = new FirebaseAnalyticsSink(null, _api);
			sink.SetUserId("B");
			sink.SetUserProperty("tier", "silver");
			sink.MarkReady();

			var first = UserEvent("first", "A", new Dictionary<string, string> { { "tier", "gold" }, { "region", "kr" } });
			var second = UserEvent("second", "B", new Dictionary<string, string> { { "tier", "silver" } });
			Send(sink, first, second);

			Assert.AreEqual(("A", "gold", "kr"), (_api.Events[0].UserId, _api.Events[0].Properties["tier"], _api.Events[0].Properties["region"]));
			Assert.AreEqual(("B", "silver"), (_api.Events[1].UserId, _api.Events[1].Properties["tier"]));
			Assert.IsFalse(_api.Events[1].Properties.ContainsKey("region"), "a property missing from the snapshot is cleared");
			Assert.AreEqual("B", _api.CurrentUserId, "the current user is restored after the batch");
			Assert.AreEqual("silver", _api.CurrentProperties["tier"]);
		}

		[Test]
		public void FailedBatch_StillRestoresTheCurrentUser()
		{
			var sink = new FirebaseAnalyticsSink(new FirebaseAnalyticsSinkOptions { HandleEvent = _ => throw new InvalidOperationException("game bug") }, _api);
			sink.SetUserId("B");
			sink.SetUserProperty("tier", "silver");
			sink.MarkReady();

			Assert.Throws<InvalidOperationException>(() =>
				sink.SendAsync(new[] { UserEvent("first", "A", new Dictionary<string, string> { { "tier", "gold" } }) }, CancellationToken.None)
					.GetAwaiter().GetResult());

			Assert.AreEqual("B", _api.CurrentUserId);
			Assert.AreEqual("silver", _api.CurrentProperties["tier"]);
		}

		[Test]
		public void PartiallyAppliedSnapshot_IsAppliedAgainNextTime()
		{
			var sink = Sink();
			var snapshot = new Dictionary<string, string> { { "tier", "gold" }, { "region", "kr" } };
			_api.FailPropertyName = "region";

			Assert.Throws<InvalidOperationException>(() =>
				sink.SendAsync(new[] { UserEvent("first", "A", snapshot) }, CancellationToken.None).GetAwaiter().GetResult());
			Assert.AreEqual(0, _api.Events.Count);
			Assert.IsFalse(_api.CurrentProperties.ContainsKey("tier"), "the partly applied snapshot is rolled back");

			_api.FailPropertyName = null;
			Send(sink, UserEvent("second", "A", snapshot));

			Assert.AreEqual("gold", _api.Events[0].Properties["tier"]);
			Assert.AreEqual("kr", _api.Events[0].Properties["region"]);
		}

		[Test]
		public void Removal_ReachesTheSdkEvenForPropertiesTheSinkNeverSaw()
		{
			var previous = Sink();
			previous.SetUserProperty("tier", "gold");

			var before = new FirebaseAnalyticsSink(null, _api);
			before.SetUserProperty("tier", null);
			Assert.AreEqual("gold", _api.CurrentProperties["tier"], "nothing is sent before ready");
			before.MarkReady();
			Assert.IsFalse(_api.CurrentProperties.ContainsKey("tier"));

			previous.SetUserProperty("tier", "gold");
			var after = Sink();
			after.SetUserProperty("tier", null);
			Assert.IsFalse(_api.CurrentProperties.ContainsKey("tier"));
		}

		[Test]
		public void SameSnapshot_DoesNotCallTheSdkAgain()
		{
			var sink = Sink();
			var properties = new Dictionary<string, string> { { "tier", "gold" } };
			sink.SetUserId("A");
			sink.SetUserProperty("tier", "gold");
			var calls = _api.UserIds.Count + _api.UserProperties.Count;

			Send(sink, UserEvent("a", "A", properties), UserEvent("b", "A", properties));

			Assert.AreEqual(calls, _api.UserIds.Count + _api.UserProperties.Count);
		}

		[Test]
		public void MarkReady_ThatFails_CanBeRetried()
		{
			var sink = new FirebaseAnalyticsSink(null, _api);
			sink.SetUserId("u1");
			sink.SetUserProperty("tier", "gold");
			_api.FailProperties = true;

			Assert.Throws<InvalidOperationException>(() => sink.MarkReady());
			Assert.IsFalse(sink.IsReady);

			_api.FailProperties = false;
			sink.MarkReady();

			Assert.IsTrue(sink.IsReady);
			Assert.AreEqual("gold", _api.CurrentProperties["tier"]);
			Assert.AreEqual(1, _api.UserIds.Count, "the user id that already went through is not sent again");
		}

		[Test]
		public void StandardFields_AreLimitedTo100Characters()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.Purchase, ("product_id", new string('p', 150)), ("price", 1.0), ("quantity", 1), ("value", 1.0),
					("currency", "USD"), ("product_name", new string('n', 150))),
				Event(AnalyticsStandardEvents.AdImpression, ("ad_platform", new string('a', 150)), ("value", 1.0), ("currency", "USD")));

			var item = ((IReadOnlyList<Dictionary<string, object>>) _api.Events[0].Parameters["items"]).Single();
			Assert.AreEqual(100, ((string) item["item_id"]).Length);
			Assert.AreEqual(100, ((string) item["item_name"]).Length);
			Assert.AreEqual(100, ((string) _api.Events[1].Parameters["ad_platform"]).Length);
		}

		[Test]
		public void WithService_QueuedEventsKeepTheirUser()
		{
			var directory = Path.Combine(Path.GetTempPath(), "geuneda-analytics-firebase-tests", Guid.NewGuid().ToString("N"));
			var service = new AnalyticsService(new AnalyticsOptions { StorageDirectory = directory });

			try
			{
				var sink = new FirebaseAnalyticsSink(null, _api);
				service.AddSink(sink);
				service.SetUserId("A");
				service.LogEvent("before_switch");
				service.SetUserId("B");
				sink.MarkReady();
				service.LogEvent("after_switch");

				CollectionAssert.AreEqual(new[] { ("before_switch", "A"), ("after_switch", "B") }, _api.Events.Select(e => (e.Name, e.UserId)));
				Assert.AreEqual("B", _api.CurrentUserId);
			}
			finally
			{
				service.Dispose();

				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}
		}

		[Test]
		public void InvalidUserPropertyNames_AreSkipped()
		{
			var sink = Sink();

			LogAssert.Expect(LogType.Warning, new Regex("user property name 'user_id' is invalid or reserved"));
			LogAssert.Expect(LogType.Warning, new Regex("user property name 'a_very_long_property_name_x' is invalid or reserved"));
			sink.SetUserProperty("user_id", "x");
			sink.SetUserProperty("a_very_long_property_name_x", "x");

			Assert.AreEqual(0, _api.UserProperties.Count);
		}

		/// <summary>호출을 기록하고, 이벤트마다 그 순간의 SDK 사용자 상태를 함께 남기는 대역입니다.</summary>
		[Test]
		public void CancelledToken_ReturnsRetryWithoutSending()
		{
			var result = Sink().SendAsync(new[] { Event("e") }, new CancellationToken(true)).GetAwaiter().GetResult();

			Assert.AreEqual(AnalyticsSendResult.Retry, result);
			Assert.AreEqual(0, _api.Events.Count);
		}

		private sealed class FakeApi : IFirebaseAnalyticsApi
		{
			public List<(string Name, Dictionary<string, object> Parameters, string UserId, Dictionary<string, string> Properties)> Events { get; } =
				new List<(string, Dictionary<string, object>, string, Dictionary<string, string>)>();

			public List<string> UserIds { get; } = new List<string>();
			public List<(string Name, string Value)> UserProperties { get; } = new List<(string, string)>();
			public string CurrentUserId { get; private set; }
			public Dictionary<string, string> CurrentProperties { get; } = new Dictionary<string, string>();
			public bool FailProperties { get; set; }
			public string FailPropertyName { get; set; }

			public void LogEvent(string name, IReadOnlyList<FirebaseParameter> parameters)
			{
				Events.Add((name, parameters.ToDictionary(p => p.Name, p => p.Value), CurrentUserId, new Dictionary<string, string>(CurrentProperties)));
			}

			public void SetUserId(string userId)
			{
				UserIds.Add(userId);
				CurrentUserId = userId;
			}

			public void SetUserProperty(string name, string value)
			{
				if (FailProperties || name == FailPropertyName)
				{
					throw new InvalidOperationException("sdk not ready");
				}

				UserProperties.Add((name, value));

				if (value == null)
				{
					CurrentProperties.Remove(name);
				}
				else
				{
					CurrentProperties[name] = value;
				}
			}
		}
	}
}
