using System;

namespace Webdav2Csv {
	static class StringExtensions {
		public static bool HasTrailingSlash(this string self) {
			return self != null && self.EndsWith("/", StringComparison.InvariantCulture);
		}
		public static string TryRemoveTrailingSlash(this string self) {
			if (self == null) return null;
			return self.HasTrailingSlash() ? self.Substring(0, self.Length-1) : self;
		}
	}
}