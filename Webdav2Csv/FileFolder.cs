using System.Collections.Generic;

namespace net.kvdb.webdav {
	public class FileFolder {
		public FileFolder() {
			PropNameToProp = new Dictionary<string, Prop>();
		}



		public string Href { get; set; }

		public bool IsFolder => PropNameToProp.ContainsKey("isFolder");

		public string RelativePath { get; set; }

		public Dictionary<string, Prop> PropNameToProp { get; }
	}
}