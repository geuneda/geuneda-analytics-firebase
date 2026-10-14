using System;
using System.Collections.Generic;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Firebase
{
	/// <summary>
	/// Firebase Analytics 의 이름·값 규칙입니다. 이벤트·파라미터 이름은 40자, 사용자 속성 이름은 24자까지이고 영문자로 시작하며 영문자·숫자·밑줄만 씁니다.
	/// <c>firebase_</c>·<c>google_</c>·<c>ga_</c> 접두사와 예약 이름은 쓸 수 없습니다. 문자열 값은 100자(사용자 속성 36자)까지입니다.
	/// </summary>
	internal static class FirebaseNames
	{
		public const int MaxEventNameLength = 40;
		public const int MaxParameterNameLength = 40;
		public const int MaxUserPropertyNameLength = 24;
		public const int MaxParameters = 25;
		public const int MaxStringValueLength = 100;
		public const int MaxUserPropertyValueLength = 36;

		private static readonly string[] ReservedPrefixes = { "firebase_", "google_", "ga_" };

		// Firebase 가 자동으로 기록해 직접 기록할 수 없는 이벤트 이름
		private static readonly HashSet<string> ReservedEvents = new HashSet<string>(StringComparer.Ordinal)
		{
			"ad_activeview", "ad_click", "ad_exposure", "ad_query", "ad_reward", "adunit_exposure", "app_background", "app_clear_data",
			"app_exception", "app_remove", "app_store_refund", "app_store_subscription_cancel", "app_store_subscription_convert",
			"app_store_subscription_renew", "app_uninstall", "app_update", "app_upgrade", "dynamic_link_app_open", "dynamic_link_app_update",
			"dynamic_link_first_open", "error", "first_open", "first_visit", "in_app_purchase", "notification_dismiss", "notification_foreground",
			"notification_open", "notification_receive", "os_update", "session_start", "session_start_with_rollout", "user_engagement"
		};

		private static readonly HashSet<string> ReservedUserProperties = new HashSet<string>(StringComparer.Ordinal)
		{
			"first_open_time", "first_visit_time", "last_deep_link_referrer", "user_id", "first_open_after_install"
		};

		/// <summary>이벤트 이름을 규칙에 맞춥니다(허용되지 않는 문자는 밑줄로). 맞출 수 없거나 예약 이름이면 null 입니다.</summary>
		public static string EventName(string name)
		{
			var sanitized = Sanitize(name, MaxEventNameLength);
			return sanitized == null || ReservedEvents.Contains(sanitized) ? null : sanitized;
		}

		public static string ParameterName(string name)
		{
			return Sanitize(name, MaxParameterNameLength);
		}

		public static string UserPropertyName(string name)
		{
			var sanitized = Sanitize(name, MaxUserPropertyNameLength);
			return sanitized == null || ReservedUserProperties.Contains(sanitized) ? null : sanitized;
		}

		public static string Truncate(string value, int maxLength)
		{
			return value == null || value.Length <= maxLength ? value : value.Substring(0, maxLength);
		}

		private static string Sanitize(string name, int maxLength)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			var builder = new StringBuilder(name.Length);

			foreach (var c in name)
			{
				builder.Append(IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
			}

			var sanitized = builder.ToString();

			if (sanitized.Length > maxLength || !IsAsciiLetter(sanitized[0]))
			{
				return null;
			}

			foreach (var prefix in ReservedPrefixes)
			{
				if (sanitized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
			}

			return sanitized;
		}

		private static bool IsAsciiLetter(char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
		}

		private static bool IsAsciiLetterOrDigit(char c)
		{
			return IsAsciiLetter(c) || (c >= '0' && c <= '9');
		}
	}
}
