using System.Collections.Generic;
using System.Net;

namespace net.kvdb.webdav {
	public class ListResult {
		public HttpStatusCode statusCode;
		public List<FileFolder> files = new List<FileFolder>();
	}
}